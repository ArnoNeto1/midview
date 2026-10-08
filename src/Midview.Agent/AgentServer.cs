using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
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
    private readonly PairingService? _pairing;

    // findWakeAddress is for tests: the MAC address of the card holding a
    // local address (WakeAddressLookup.Find everywhere else).
    public AgentServer(
        TcpListener listener, X509Certificate2 serverCertificate, TrustStore masterTrustStore,
        SiteBlockCommandHandler commandHandler, PowerCommandHandler? powerHandler = null,
        Func<IPAddress, byte[]?>? findWakeAddress = null, ScheduledShutdown? scheduledShutdown = null,
        UpdateReceiver? updateReceiver = null, AppBlockCommandHandler? appBlock = null,
        LabConfigCommandHandler? labConfig = null, PairingService? pairing = null)
    {
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
