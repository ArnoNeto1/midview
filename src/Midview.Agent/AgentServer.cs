using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Midview.Protocol;

namespace Midview.Agent;

public sealed class AgentServer
{
    // Generous for a LAN round trip, but short enough that a connection which
    // never finishes its TLS handshake is dropped quickly.
    private const int TimeoutMs = 10_000;

    // A pairing waits on the teacher, who has Pairing.ConfirmTimeout to
    // compare the codes; a little more, so the Agent's own deadline is
    // what ends it.
    private static readonly int PairingTimeoutMs = (int)(Pairing.ConfirmTimeout + TimeSpan.FromSeconds(30)).TotalMilliseconds;

    private readonly TcpListener _listener;
    private readonly X509Certificate2 _serverCertificate;
    private readonly TrustStore _masterTrustStore;
    private readonly SiteBlockCommandHandler _commandHandler;
    private readonly PowerCommandHandler? _powerHandler;
    private readonly Func<IPAddress, byte[]?> _findWakeAddress;
    private readonly ScheduledShutdown? _scheduledShutdown;
    private readonly UpdateReceiver? _updateReceiver;
    private readonly AppBlockCommandHandler? _appBlock;
    private readonly LabConfigCommandHandler? _labConfig;
    private readonly ProgramUninstaller? _uninstaller;
    private readonly PendingUninstalls? _pendingUninstalls;
    private readonly PairingService? _pairing;
    private readonly ClassLessonHandler? _classLesson;

    // findWakeAddress is for tests: the MAC address of the card holding a
    // local address (WakeAddressLookup.Find everywhere else).
    public AgentServer(
        TcpListener listener, X509Certificate2 serverCertificate, TrustStore masterTrustStore,
        SiteBlockCommandHandler commandHandler, PowerCommandHandler? powerHandler = null,
        Func<IPAddress, byte[]?>? findWakeAddress = null, ScheduledShutdown? scheduledShutdown = null,
        UpdateReceiver? updateReceiver = null, AppBlockCommandHandler? appBlock = null,
        LabConfigCommandHandler? labConfig = null, ProgramUninstaller? uninstaller = null, PairingService? pairing = null,
        PendingUninstalls? pendingUninstalls = null, ClassLessonHandler? classLesson = null)
    {
        _classLesson = classLesson;
        _listener = listener;
        _serverCertificate = serverCertificate;
        _masterTrustStore = masterTrustStore;
        _commandHandler = commandHandler;
        _powerHandler = powerHandler;
        _findWakeAddress = findWakeAddress ?? WakeAddressLookup.Find;
        _scheduledShutdown = scheduledShutdown;
        _updateReceiver = updateReceiver;
        _appBlock = appBlock;
        _labConfig = labConfig;
        _uninstaller = uninstaller;
        _pendingUninstalls = pendingUninstalls;
        _pairing = pairing;
    }

    public void Run(CancellationToken cancellationToken)
    {
        _listener.Start();
        using var registration = cancellationToken.Register(() => _listener.Stop());

        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient tcpClient;
            try
            {
                tcpClient = _listener.AcceptTcpClient();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Each connection on its own thread: anything on the LAN can open a
            // TCP connection and then send nothing, and handled inline that
            // blocked every command -- the teacher's unblock included. A
            // dedicated thread rather than a thread-pool task, since the
            // handler blocks (up to TimeoutMs on a stalled handshake): a few
            // stalled connections must not starve the pool the rest of the
            // Agent runs on.
            _ = Task.Factory.StartNew(
                () => HandleClientSafely(tcpClient), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    private void HandleClientSafely(TcpClient tcpClient)
    {
        try
        {
            HandleClient(tcpClient);
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Error handling a client connection: {ex.Message}");
        }
    }

    private void HandleClient(TcpClient tcpClient)
    {
        using var client = tcpClient;
        client.ReceiveTimeout = TimeoutMs;
        client.SendTimeout = TimeoutMs;
        using var channel = SecureChannel.AuthenticateAsServerAcceptingAnyClient(tcpClient, _serverCertificate);
        var message = channel.Receive();

        // Anyone may ask to pair (PairingService decides who may); any other
        // message needs a teacher this machine trusts. Any certificate gets
        // through the handshake now, so this is what keeps strangers out.
        if (message.Type == MessageType.PairRequest)
        {
            if (_pairing is null)
            {
                channel.Send(Pairing.Refused("Esta máquina não aceita pareamento."));
                return;
            }
            client.ReceiveTimeout = PairingTimeoutMs;
            _pairing.Handle(
                channel.RemoteThumbprint, _serverCertificate.Thumbprint, message.Payload,
                channel.Receive, channel.Send, WakeAddressText(client));
            return;
        }

        if (!_masterTrustStore.IsTrusted(channel.RemoteThumbprint))
        {
            AgentLog.Warn($"Refused {message.Type} from a teacher this machine doesn't trust ({channel.RemoteThumbprint}).");
            return;
        }

        if (message.Type == MessageType.AllowNewTeacher)
        {
            var (allowed, allowDetail) = _pairing?.AllowNewTeacher() ?? (false, "esta máquina não aceita pareamento");
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(allowed, allowDetail)));
            return;
        }

        if (message.Type == MessageType.TrustTeacher)
        {
            var (trusted, trustDetail) = _pairing?.TrustTeacher(channel.RemoteThumbprint, message.Payload)
                ?? (false, "esta máquina não aceita outro professor");
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(trusted, trustDetail)));
            return;
        }

        // Power commands say what happened (a CommandResult, whose first
        // byte is the same 1/0 the site-blocking commands answer with).
        if (message.Type == MessageType.Power)
        {
            var (succeeded, detail) = _powerHandler?.Handle(message.Payload)
                ?? (false, "esta máquina não aceita desligamento");
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(succeeded, detail)));
            return;
        }

        if (message.Type == MessageType.SetPowerSchedule)
        {
            var (succeeded, detail) = _scheduledShutdown?.Handle(message.Payload)
                ?? (false, "esta máquina não aceita agendamento");
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(succeeded, detail)));
            return;
        }

        // The one exchange that takes more than one message each way (see
        // NetworkUpdate); the receiver answers everything itself.
        if (message.Type == MessageType.UpdateOffer)
        {
            if (_updateReceiver is null)
            {
                channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(false, "esta máquina não aceita atualização pela rede")));
                return;
            }
            _updateReceiver.Handle(message.Payload, channel.Receive, channel.Send);
            return;
        }

        if (message.Type == MessageType.GetUpdateStatus && _updateReceiver is not null)
        {
            channel.Send(new Message(MessageType.UpdateStatus, _updateReceiver.Status(message.Payload).Encode()));
            return;
        }

        if (message.Type == MessageType.SetAppBlock)
        {
            var (succeeded, detail) = _appBlock?.Handle(message.Payload)
                ?? (false, "esta máquina não aceita bloqueio de programas");
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(succeeded, detail)));
            return;
        }

        if (message.Type == MessageType.GetAppBlock && _appBlock is not null)
        {
            channel.Send(new Message(MessageType.AppBlockStatus, _appBlock.Current.Encode()));
            return;
        }

        if (message.Type == MessageType.SetLabConfig)
        {
            var (succeeded, detail) = _labConfig?.Handle(message.Payload)
                ?? (false, "esta máquina não aceita o painel do laboratório");
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(succeeded, detail)));
            return;
        }

        if (message.Type == MessageType.ResetLabConfig)
        {
            var (succeeded, detail) = _labConfig?.Reset()
                ?? (false, "esta máquina não aceita o painel do laboratório");
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(succeeded, detail)));
            return;
        }

        if (message.Type == MessageType.GetLabConfig && _labConfig is not null)
        {
            channel.Send(new Message(MessageType.LabConfigStatus, _labConfig.Current.Encode()));
            return;
        }

        if (message.Type == MessageType.ClearChromeProfiles)
        {
            var (succeeded, detail) = _labConfig?.ClearChromeProfiles()
                ?? (false, "esta máquina não aceita o painel do laboratório");
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(succeeded, detail)));
            return;
        }

        if (message.Type == MessageType.ClearDownloads)
        {
            // A missing or unknown scope byte must never fall through to the
            // destructive "delete everything".
            (bool Succeeded, string Detail) result =
                message.Payload.Length != 1 || !Enum.IsDefined((DownloadsScope)message.Payload[0])
                    ? (false, "pedido inválido")
                    : _labConfig?.ClearDownloads((DownloadsScope)message.Payload[0])
                      ?? (false, "esta máquina não aceita o painel do laboratório");
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(result.Succeeded, result.Detail)));
            return;
        }

        // Only with the handler: without it both fall through to the default
        // below, CommandAck [0], which is what an Agent from before the class
        // folder answers and what the Master shows as "Midview antigo".
        if (message.Type == MessageType.SetClassLesson && _classLesson is not null)
        {
            var (succeeded, detail) = _classLesson.Handle(message.Payload, channel.RemoteThumbprint, RemoteAddressText(client));
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(succeeded, detail)));
            return;
        }

        if (message.Type == MessageType.GetClassLesson && _classLesson is not null)
        {
            channel.Send(new Message(MessageType.ClassLessonStatus, _classLesson.Current.Encode()));
            return;
        }

        // The class folders' home, for two teacher PCs in one lab, kept by the
        // lesson handler; likewise only when it keeps one, so an Agent without
        // it says "no home" the old way (CommandAck [0]).
        if (message.Type == MessageType.ClaimClassHome && _classLesson is { KeepsHome: true })
        {
            var (succeeded, detail) = ClassHomeClaim.TryDecode(message.Payload, out var claim)
                ? _classLesson.ClaimHome(channel.RemoteThumbprint, RemoteAddressText(client), claim!)
                : (false, "pedido inválido");
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(succeeded, detail)));
            return;
        }

        if (message.Type == MessageType.GetClassHome && _classLesson is { KeepsHome: true })
        {
            channel.Send(new Message(MessageType.ClassHomeStatus, ClassHome.Encode(_classLesson.CurrentHome)));
            return;
        }

        // The home PC asks whether a colleague's Master is a teacher this
        // machine trusts, before serving it the class folder window.
        if (message.Type == MessageType.IsTeacherTrusted)
        {
            var thumbprint = Encoding.ASCII.GetString(message.Payload);
            var (succeeded, detail) = TeacherInvite.IsThumbprint(thumbprint)
                ? (_masterTrustStore.IsTrusted(thumbprint), "")
                : (false, "pedido inválido");
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(succeeded, detail)));
            return;
        }

        if (message.Type == MessageType.GetInstalledPrograms && _uninstaller is not null)
        {
            channel.Send(new Message(MessageType.InstalledPrograms, ProgramList.Encode(_uninstaller.ListInstalled())));
            return;
        }

        if (message.Type == MessageType.UninstallPrograms)
        {
            var (succeeded, detail) = _uninstaller is null
                ? (false, "esta máquina não desinstala programas")
                : ProgramList.TryDecode(message.Payload, out var names)
                    ? UninstallAndRemember(_uninstaller, names)
                    : (false, "pedido inválido");
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(succeeded, detail)));
            return;
        }

        if (message.Type == MessageType.GetWakeAddress)
        {
            var localAddress = ((IPEndPoint)client.Client.LocalEndPoint!).Address;
            var macAddress = _findWakeAddress(localAddress) ?? Array.Empty<byte>();
            channel.Send(new Message(MessageType.WakeAddress, macAddress));
            return;
        }

        var success = _commandHandler.Handle(message.Type, message.Payload);
        channel.Send(new Message(MessageType.CommandAck, new[] { (byte)(success ? 1 : 0) }));
    }

    // Remembered before anything runs, so the students who aren't logged on
    // lose their own copies at their next logon (PendingUninstallWorker) even
    // if this machine restarts midway through the uninstalls.
    private (bool Succeeded, string Detail) UninstallAndRemember(ProgramUninstaller uninstaller, IReadOnlyList<string> names)
    {
        _pendingUninstalls?.Add(names);
        return uninstaller.UninstallMany(names);
    }

    // Where the teacher's connection came from, as the class home keeps it:
    // an IPv4 address in IPv6 form is written as plain IPv4.
    private static string RemoteAddressText(TcpClient client)
    {
        var address = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }

    // The MAC address of the card this connection came in on, as the
    // classroom list keeps it ("AA-BB-CC-DD-EE-FF"), or null.
    private string? WakeAddressText(TcpClient client)
    {
        var localAddress = ((IPEndPoint)client.Client.LocalEndPoint!).Address;
        return _findWakeAddress(localAddress) is { } mac && WakeOnLan.IsUsableMacAddress(mac)
            ? WakeOnLan.FormatMacAddress(mac)
            : null;
    }
}
