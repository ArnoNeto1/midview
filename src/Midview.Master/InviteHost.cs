using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

public enum InviteEnd
{
    // The colleague got the class.
    Given,

    // Someone sent a wrong secret: the invite ended at once.
    WrongCode,

    Expired,
    Cancelled,
}

// How an invite ended. Given: TeacherName got Result's machines; ListSent
// is false if the connection dropped before the list reached them.
public sealed record InviteOutcome(InviteEnd End, string? TeacherName = null, InviteResult? Result = null, bool ListSent = false);

// Teacher A's side of "Dar acesso a um colega" (see TeacherInvite): makes
// the code, answers "who has an invite open?" on UDP and takes one colleague
// over TLS, only while the invite lasts. The code's secret is the
// permission: whoever sends it gets the class (giveClass tells every
// machine to trust them, and says which did). One connection at a time; the
// first wrong secret ends the invite.
public sealed class InviteHost : IDisposable
{
    public const string WrongCodeText = "Código errado. O convite foi cancelado; peça outro ao colega.";
    public const string GoneText = "O convite expirou ou foi cancelado. Peça outro código ao colega.";

    // _state: open, then either ended (expired, cancelled, wrong secret) or
    // handing over (the right secret came) and then ended. Only one of them
    // wins the switch from open, so an invite that expires while the class
    // is being given still ends as given.
    private const int StateOpen = 0;
    private const int StateHandingOver = 1;
    private const int StateEnded = 2;

    private readonly X509Certificate2 _certificate;
    private readonly string _teacherName;
    private readonly Func<TeacherGrant, InviteResult> _giveClass;
    private readonly Action<string>? _joining;
    private readonly TcpListener _tcp;
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<InviteOutcome> _outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _state = StateOpen;
    private int _busy;
    private volatile bool _cancelled;

    private InviteHost(
        X509Certificate2 certificate, string teacherName, Func<TeacherGrant, InviteResult> giveClass,
        Action<string>? joining, TcpListener tcp, UdpClient udp)
    {
        _certificate = certificate;
        _teacherName = teacherName;
        _giveClass = giveClass;
        _joining = joining;
        _tcp = tcp;
        _udp = udp;
        Code = TeacherInvite.NewCode(certificate.Thumbprint);
        TcpPort = ((IPEndPoint)tcp.LocalEndpoint).Port;
        UdpPort = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }

    public string Code { get; }

    public int TcpPort { get; }

    public int UdpPort { get; }

    public Task<InviteOutcome> Outcome => _outcome.Task;

    // Opens the invite for lifetime. joining gets the colleague's computer
    // name (on a background thread) once the right secret arrived, before
    // the machines are told. Throws SocketException when a port can't be
    // had (port 0, for tests, takes any free one).
    public static InviteHost Open(
        X509Certificate2 certificate, string teacherName, Func<TeacherGrant, InviteResult> giveClass, TimeSpan lifetime,
        Action<string>? joining = null, int tcpPort = TeacherInvite.Port, int udpPort = TeacherInvite.Port)
    {
        var tcp = new TcpListener(IPAddress.Any, tcpPort);
        tcp.Start();
        UdpClient? udp = null;
        try
        {
            udp = new UdpClient(new IPEndPoint(IPAddress.Any, udpPort));
            udp.Client.IOControl(DiscoveryClient.SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null);
        }
        catch
        {
            udp?.Dispose();
            tcp.Stop();
            throw;
        }

        var host = new InviteHost(certificate, teacherName, giveClass, joining, tcp, udp);
        host._stop.Token.Register(host.OnStopped);
        host._stop.CancelAfter(lifetime);
        _ = host.AcceptAsync();
        _ = host.AnswerProbesAsync();
        return host;
    }

    public static string SameIdentityText(string teacherName) =>
        $"Este computador já usa a mesma identidade de professor que {teacherName}; ele já tem acesso às mesmas máquinas.";

    // Closing the window: the invite ends, unless the class is already being
    // given (that finishes, and Outcome says so).
    public void Dispose()
    {
        _cancelled = true;
        _stop.Cancel();
    }

    private void OnStopped()
    {
        if (Interlocked.CompareExchange(ref _state, StateEnded, StateOpen) == StateOpen)
        {
            Finish(new InviteOutcome(_cancelled ? InviteEnd.Cancelled : InviteEnd.Expired));
        }
    }

    private void Finish(InviteOutcome outcome)
    {
        if (!_outcome.TrySetResult(outcome))
        {
            return;
        }
        _tcp.Stop();
        _udp.Dispose();
        _stop.Cancel();
    }

    private async Task AcceptAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _tcp.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            // One colleague at a time: another connection meanwhile is
            // closed unanswered.
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                client.Dispose();
                continue;
            }

            // Its own thread, as the Agent does: the exchange blocks on the
            // network, and giving the class waits on every machine.
            _ = Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        Handle(client);
                    }
                    finally
                    {
                        client.Dispose();
                        Volatile.Write(ref _busy, 0);
                    }
                },
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    private void Handle(TcpClient client)
    {
        var timeout = (int)TeacherInvite.JoinTimeout.TotalMilliseconds;
        client.ReceiveTimeout = timeout;
        client.SendTimeout = timeout;
        try
        {
            using var channel = SecureChannel.AuthenticateAsServerAcceptingAnyClient(client, _certificate);
            var message = channel.Receive(TeacherInvite.MaxJoinLength);
            if (message.Type != MessageType.InviteJoin || InviteJoinRequest.Decode(message.Payload) is not { } join)
            {
                return;
            }

            if (!TeacherInvite.SameSecret(join.Secret, TeacherInvite.Secret(Code)))
            {
                // An honest colleague's typo never gets here (their side
                // checks the code against this certificate first): this is
                // someone guessing.
                var first = Interlocked.CompareExchange(ref _state, StateEnded, StateOpen) == StateOpen;
                TrySend(channel, TeacherInvite.Refused(first ? WrongCodeText : GoneText));
                if (first)
                {
                    Finish(new InviteOutcome(InviteEnd.WrongCode));
                }
                return;
            }

            // A copy of this teacher's own master-identity.pfx: it already
            // reaches every machine this one does.
            if (string.Equals(channel.RemoteThumbprint, _certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                channel.Send(TeacherInvite.Refused(SameIdentityText(_teacherName)));
                return;
            }

            if (Interlocked.CompareExchange(ref _state, StateHandingOver, StateOpen) != StateOpen)
            {
                channel.Send(TeacherInvite.Refused(GoneText));
                return;
            }

            GiveTheClass(channel, Pairing.CleanTeacherName(join.TeacherName));
        }
        catch (Exception ex) when (ex is IOException or SocketException or AuthenticationException or InvalidDataException or ObjectDisposedException)
        {
            // A colleague whose code points elsewhere hangs up after the
            // handshake, and a stranger may send nothing: the invite stays
            // open.
        }
    }

    private void GiveTheClass(SecureChannel channel, string teacherName)
    {
        var result = InviteResult.Empty;
        var sent = false;
        try
        {
            _joining?.Invoke(teacherName);
            result = _giveClass(new TeacherGrant(channel.RemoteThumbprint, teacherName));
            channel.Send(new Message(MessageType.InviteDone, result.Encode()));
            sent = true;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // The colleague left; the machines were told anyway.
        }
        finally
        {
            Volatile.Write(ref _state, StateEnded);
            Finish(new InviteOutcome(InviteEnd.Given, teacherName, result, sent));
        }
    }

    private static void TrySend(SecureChannel channel, Message message)
    {
        try
        {
            channel.Send(message);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // Gone already.
        }
    }

    private async Task AnswerProbesAsync()
    {
        while (true)
        {
            UdpReceiveResult received;
            try
            {
                received = await _udp.ReceiveAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // A reset left by an earlier reply that couldn't be
                // delivered, or the socket closing as the invite ends.
                if (_stop.IsCancellationRequested)
                {
                    return;
                }
                continue;
            }

            if (TeacherInvite.ParseProbe(received.Buffer) is not { } probe)
            {
                continue;
            }

            try
            {
                await _udp.SendAsync(TeacherInvite.EncodeReply(probe, _teacherName, TcpPort), received.RemoteEndPoint, _stop.Token);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // The colleague's PC went away, or the invite ended.
            }
        }
    }
}
