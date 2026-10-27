using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class ControlLockKeeperTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly X509Certificate2 _masterCertificate = SelfSignedCertificate.Create("master-under-test");
    private readonly X509Certificate2 _sessionCertificate = SelfSignedCertificate.Create("session-under-test");

    public void Dispose()
    {
        _masterCertificate.Dispose();
        _sessionCertificate.Dispose();
    }

    private KnownMachine Machine => new("Aluno-1", "127.0.0.1", _sessionCertificate.Thumbprint, null);

    // The hold is the first thing a Session hears, then only heartbeats, one
    // every few seconds: the Session lets go of a connection that is silent
    // for 15 s.
    [Fact]
    public async Task Holds_by_sending_HoldInputLock_then_heartbeats()
    {
        using var session = new ScriptedSession(_sessionCertificate, _masterCertificate);
        using var keeper = new ControlLockKeeper(Machine, _masterCertificate, session.Port);

        keeper.Start();
        using var channel = await session.Accept().WaitAsync(Timeout);
        var heard = await Hear(channel, 3).WaitAsync(Timeout);

        Assert.Equal(
            new[] { MessageType.HoldInputLock, MessageType.Heartbeat, MessageType.Heartbeat },
            heard.Select(message => message.Type));
        // Spread out over time, not sent in a burst.
        Assert.True(
            Stopwatch.GetElapsedTime(heard[1].At, heard[2].At) >= ControlLockKeeper.HeartbeatInterval / 3);
    }

    // The student gets the keyboard and mouse back when the teacher is done:
    // the connection ends with the keeper, not at its next heartbeat.
    [Fact]
    public async Task Disposing_the_keeper_stops_the_hold()
    {
        using var session = new ScriptedSession(_sessionCertificate, _masterCertificate);
        using var keeper = new ControlLockKeeper(Machine, _masterCertificate, session.Port);
        keeper.Start();
        using var channel = await session.Accept().WaitAsync(Timeout);
        await Hear(channel, 1).WaitAsync(Timeout);
        var ending = EndOf(channel);

        var disposedAt = Stopwatch.GetTimestamp();
        keeper.Dispose();

        AssertEndedAtOnce(disposedAt, await ending.WaitAsync(Timeout));
    }

    // Also when the keeper is still in the handshake with a Session that took
    // the connection and never answers: it must not wait for the handshake's
    // own timeout to give up on the connection, or a Session that comes round
    // late would still be asked to hold.
    [Fact]
    public async Task Disposing_the_keeper_in_the_middle_of_the_handshake_closes_the_connection_at_once()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var keeper = new ControlLockKeeper(Machine, _masterCertificate, ((IPEndPoint)listener.LocalEndpoint).Port);
            keeper.Start();
            using var client = await listener.AcceptTcpClientAsync().WaitAsync(Timeout);
            var stream = client.GetStream();
            // The Master's side of the handshake has begun, and waits for an answer.
            var hello = await OnAThreadOfItsOwn(() => stream.Read(new byte[4096])).WaitAsync(Timeout);
            Assert.True(hello > 0);
            var ending = EndOf(stream);

            var disposedAt = Stopwatch.GetTimestamp();
            keeper.Dispose();

            AssertEndedAtOnce(disposedAt, await ending.WaitAsync(Timeout));
        }
        finally
        {
            listener.Stop();
        }
    }

    // A Session that restarted, or a student who logged off and on, is held
    // again as soon as it is back.
    [Fact]
    public async Task A_dropped_hold_is_taken_again()
    {
        using var session = new ScriptedSession(_sessionCertificate, _masterCertificate);
        using var keeper = new ControlLockKeeper(Machine, _masterCertificate, session.Port);
        keeper.Start();

        using (var first = await session.Accept().WaitAsync(Timeout))
        {
            Assert.Equal(MessageType.HoldInputLock, (await Hear(first, 1).WaitAsync(Timeout))[0].Type);
        } // the Session's end of the connection goes away

        using var second = await session.Accept().WaitAsync(Timeout);
        Assert.Equal(MessageType.HoldInputLock, (await Hear(second, 1).WaitAsync(Timeout))[0].Type);
    }

    // Only the machine's own Session may be asked to hold: anything else that
    // answers on its port, a student's program included, is turned away.
    [Fact]
    public async Task A_server_that_is_not_the_Session_is_refused()
    {
        using var impostor = SelfSignedCertificate.Create("impostor-under-test");
        using var session = new ScriptedSession(impostor, _masterCertificate);
        using var keeper = new ControlLockKeeper(Machine, _masterCertificate, session.Port);

        keeper.Start();

        // The Master walks out of the handshake, which the other end can see
        // only once it is over, so the hold is never asked: all the Session
        // hears is the connection ending.
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var channel = await session.Accept();
            await Hear(channel, 1);
        }).WaitAsync(Timeout);
    }

    // Dispose has to end the connection at once, not at the keeper's next
    // heartbeat: the hold has only just begun, so that is the whole 3 s away.
    private static void AssertEndedAtOnce(long disposedAt, long endedAt)
    {
        var elapsed = Stopwatch.GetElapsedTime(disposedAt, endedAt);
        Assert.True(
            elapsed < ControlLockKeeper.HeartbeatInterval / 2,
            $"The connection ended {elapsed.TotalMilliseconds:F0} ms after Dispose");
    }

    // On a thread of its own, not the pool's: a loaded test run can leave a
    // pooled read unstarted for longer than the closing it waits for takes.
    private static Task<T> OnAThreadOfItsOwn<T>(Func<T> work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    // What the Session hears next on a connection, and when.
    private static Task<List<(MessageType Type, long At)>> Hear(SecureChannel channel, int count) =>
        OnAThreadOfItsOwn(() =>
        {
            var heard = new List<(MessageType, long)>();
            while (heard.Count < count)
            {
                var message = channel.Receive();
                heard.Add((message.Type, Stopwatch.GetTimestamp()));
            }
            return heard;
        });

    // When the end of a connection reaches the Session's read of it, whatever
    // arrives before being ignored. Taken on the reading thread, so that the
    // test's own scheduling doesn't show in the time.
    private static Task<long> EndOf(SecureChannel channel) =>
        OnAThreadOfItsOwn(() =>
        {
            try
            {
                while (true)
                {
                    channel.Receive();
                }
            }
            catch (IOException)
            {
                return Stopwatch.GetTimestamp();
            }
        });

    // The same for a connection still short of its TLS handshake.
    private static Task<long> EndOf(NetworkStream stream) =>
        OnAThreadOfItsOwn(() =>
        {
            var buffer = new byte[4096];
            try
            {
                while (stream.Read(buffer, 0, buffer.Length) > 0)
                {
                }
            }
            catch (IOException)
            {
            }
            return Stopwatch.GetTimestamp();
        });

    // A Session on loopback as far as a hold goes: each connection comes
    // when the test asks for it, and the test plays the Session on it.
    private sealed class ScriptedSession : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly X509Certificate2 _sessionCertificate;
        private readonly string _masterThumbprint;

        public ScriptedSession(X509Certificate2 sessionCertificate, X509Certificate2 masterCertificate)
        {
            _sessionCertificate = sessionCertificate;
            _masterThumbprint = masterCertificate.Thumbprint;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        }

        public int Port { get; }

        // The next connection, once its handshake is done.
        public Task<SecureChannel> Accept() =>
            OnAThreadOfItsOwn(() =>
            {
                var client = _listener.AcceptTcpClient();
                try
                {
                    client.ReceiveTimeout = 20_000;
                    client.SendTimeout = 20_000;
                    return SecureChannel.AuthenticateAsServer(
                        client, _sessionCertificate, new TrustStore(new[] { _masterThumbprint }));
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            });

        public void Dispose() => _listener.Stop();
    }
}
