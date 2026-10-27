using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// Keeps the student's keyboard and mouse locked on one machine's Session for
// as long as it runs, while the teacher controls that machine through
// Midview.Desk, which can't lock the input itself: one HoldInputLock
// connection, kept up with heartbeats. A lost connection -- the Session
// restarted, the student logged off and on, the network dropped -- is opened
// again and the hold taken again. Dispose closes the connection, which gives
// the student the keyboard and mouse back at once; if the teacher's PC dies
// instead, the Session's own receive timeout does.
//
// Only for a Session that knows HoldInputLock. One from before takes the
// connection for a control one: it takes the Session's single slot, keeping
// every other Master out, and streams the screen at a Master that never reads
// it, locking nothing. Whoever starts this keeper must know the Session is
// current.
public sealed class ControlLockKeeper : IDisposable
{
    // Inside the 5 s the Session's input lock waits for a heartbeat, and well
    // inside the 15 s its receive timeout allows.
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private const int IoTimeoutMs = 10_000;

    private readonly KnownMachine _machine;
    private readonly X509Certificate2 _masterCertificate;
    private readonly int _port;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private readonly object _gate = new();
    private TcpClient? _connection;

    public ControlLockKeeper(KnownMachine machine, X509Certificate2 masterCertificate, int port = ConnectionClient.SessionPort)
    {
        _machine = machine;
        _masterCertificate = masterCertificate;
        _port = port;
        _thread = new Thread(Run) { IsBackground = true, Name = $"Control lock {machine.Name}" };
    }

    public void Start() => _thread.Start();

    public void Dispose()
    {
        _stop.Cancel();
        lock (_gate)
        {
            // Now, not at the next heartbeat: the student gets the keyboard and mouse back at once.
            _connection?.Dispose();
        }
    }

    private void Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                Hold();
            }
            catch (Exception)
            {
                // Nothing to report: the next try is only RetryInterval away,
                // and a Session that lost the connection has ended the hold.
                if (_stop.IsCancellationRequested)
                {
                    return;
                }
            }
            _stop.Token.WaitHandle.WaitOne(RetryInterval);
        }
    }

    // Holds until the keeper is disposed. A connection that can't be opened or
    // kept up throws, and Run tries again.
    private void Hold()
    {
        using var tcpClient = TcpConnect.WithTimeout(_machine.HostAddress, _port, ConnectTimeout, _stop.Token);
        lock (_gate)
        {
            if (_stop.IsCancellationRequested)
            {
                return;
            }
            _connection = tcpClient;
        }

        try
        {
            // The handshake's reads and the sends; nothing is read after it.
            tcpClient.ReceiveTimeout = IoTimeoutMs;
            tcpClient.SendTimeout = IoTimeoutMs;
            using var channel = SecureChannel.AuthenticateAsClient(
                tcpClient, _machine.HostAddress, _masterCertificate, new TrustStore(new[] { _machine.SessionThumbprint }));

            // The Session answers nothing: it locks the input, and keeps it
            // locked until this connection ends.
            channel.Send(new Message(MessageType.HoldInputLock, Array.Empty<byte>()));

            while (!_stop.Token.WaitHandle.WaitOne(HeartbeatInterval))
            {
                channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));
            }
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
