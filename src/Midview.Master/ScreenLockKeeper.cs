using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// Keeps one machine's screen locked for as long as it runs: one LockScreen
// connection, kept up with heartbeats. A lost connection -- the machine
// restarted, the student logged off and on, the network dropped -- is
// opened again and the screen locked again, so restarting is no way out.
// Dispose closes the connection, which unlocks the machine at once; if the
// teacher's PC dies instead, the Session's own timeout unlocks it.
public sealed class ScreenLockKeeper : IDisposable
{
    // Well inside the Session's 15 s receive timeout.
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(3);

    // A machine that can't lock its screen won't start to by itself soon.
    private static readonly TimeSpan RefusedRetryInterval = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private const int ReplyTimeoutMs = 10_000;

    private readonly KnownMachine _machine;
    private readonly X509Certificate2 _masterCertificate;
    private readonly int _port;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private readonly object _gate = new();
    private TcpClient? _connection;

    public ScreenLockKeeper(KnownMachine machine, X509Certificate2 masterCertificate, int port = ConnectionClient.SessionPort)
    {
        _machine = machine;
        _masterCertificate = masterCertificate;
        _port = port;
        _thread = new Thread(Run) { IsBackground = true, Name = $"Screen lock {machine.Name}" };
    }

    // Raised on the keeper's thread whenever the machine's state is known:
    // locked ("tela bloqueada"), or not and why.
    public event Action<bool, string>? StatusChanged;

    public KnownMachine Machine => _machine;

    public void Start() => _thread.Start();

    public void Dispose()
    {
        _stop.Cancel();
        lock (_gate)
        {
            // Now, not at the next heartbeat: the student gets the screen back at once.
            _connection?.Dispose();
        }
    }

    private void Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            var wait = RetryInterval;
            try
            {
                wait = KeepLocked();
            }
            catch (Exception ex)
            {
                if (_stop.IsCancellationRequested)
                {
                    return;
                }
                StatusChanged?.Invoke(false, FailureText.Describe(ex));
            }
            _stop.Token.WaitHandle.WaitOne(wait);
        }
    }

    // Returns how long to wait before trying again.
    private TimeSpan KeepLocked()
    {
        using var tcpClient = TcpConnect.WithTimeout(_machine.HostAddress, _port, ConnectTimeout, _stop.Token);
        lock (_gate)
        {
            if (_stop.IsCancellationRequested)
            {
                return TimeSpan.Zero;
            }
            _connection = tcpClient;
        }

        try
        {
            tcpClient.ReceiveTimeout = ReplyTimeoutMs;
            tcpClient.SendTimeout = ReplyTimeoutMs;
            using var channel = SecureChannel.AuthenticateAsClient(
                tcpClient, _machine.HostAddress, _masterCertificate, new TrustStore(new[] { _machine.SessionThumbprint }));
            channel.Send(new Message(MessageType.LockScreen, Array.Empty<byte>()));

            // A Session from before the screen lock takes it for a control
            // connection and starts sending its screen instead of answering.
            var reply = channel.Receive();
            if (reply.Type != MessageType.CommandAck)
            {
                StatusChanged?.Invoke(false, "Midview antigo: sem bloqueio de tela");
                return RefusedRetryInterval;
            }

            var (locked, detail) = CommandResult.Decode(reply.Payload);
            StatusChanged?.Invoke(locked, detail);
            if (!locked)
            {
                return RefusedRetryInterval;
            }

            while (!_stop.Token.WaitHandle.WaitOne(HeartbeatInterval))
            {
                channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));
            }
            return TimeSpan.Zero;
        }
        finally
        {
            lock (_gate)
            {
                _connection = null;
            }
        }
    }
}
