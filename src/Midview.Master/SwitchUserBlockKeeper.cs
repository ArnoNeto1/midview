using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// Hides "Switch user" on one machine for as long as its screen is locked, by
// holding a HoldSwitchUserBlock connection to its Agent (HideFastUserSwitching
// is an HKLM policy the student-level Session can't set). Mirrors
// ScreenLockKeeper: a lost connection -- the machine restarted, the network
// dropped -- is opened again, so restarting is no way out, and Dispose frees
// the block at once; if the teacher's PC dies instead, the Agent's own receive
// timeout frees it. Best-effort and silent: a machine with no Agent thumbprint
// or an older Agent simply isn't covered, and the screen lock still works.
public sealed class SwitchUserBlockKeeper : IDisposable
{
    // Well inside the Agent's 10 s receive timeout.
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(3);

    // An older Agent won't learn to block switching user by itself soon.
    private static readonly TimeSpan RefusedRetryInterval = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private const int ReplyTimeoutMs = 10_000;

    private readonly KnownMachine _machine;
    private readonly string _agentThumbprint;
    private readonly X509Certificate2 _masterCertificate;
    private readonly int _port;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private readonly object _gate = new();
    private TcpClient? _connection;

    public SwitchUserBlockKeeper(
        KnownMachine machine, string agentThumbprint, X509Certificate2 masterCertificate,
        int port = ConnectionClient.AgentPort)
    {
        _machine = machine;
        _agentThumbprint = agentThumbprint;
        _masterCertificate = masterCertificate;
        _port = port;
        _thread = new Thread(Run) { IsBackground = true, Name = $"Switch-user block {machine.Name}" };
    }

    public void Start() => _thread.Start();

    public void Dispose()
    {
        _stop.Cancel();
        lock (_gate)
        {
            // Now, not at the next heartbeat: switching user comes back at once.
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
                wait = KeepBlocked();
            }
            catch
            {
                if (_stop.IsCancellationRequested)
                {
                    return;
                }
                // Best-effort: any failure just waits and tries again.
            }
            _stop.Token.WaitHandle.WaitOne(wait);
        }
    }

    // Returns how long to wait before trying again.
    private TimeSpan KeepBlocked()
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
                tcpClient, _machine.HostAddress, _masterCertificate, new TrustStore(new[] { _agentThumbprint }));
            channel.Send(new Message(MessageType.HoldSwitchUserBlock, Array.Empty<byte>()));

            var reply = channel.Receive();
            if (reply.Type != MessageType.CommandAck || !CommandResult.Decode(reply.Payload).Succeeded)
            {
                // An older Agent that doesn't know HoldSwitchUserBlock.
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
