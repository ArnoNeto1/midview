using System.Net;
using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Midview.Session;
using Xunit;

public class ScreenLockKeeperTests
{
    [Fact]
    public async Task TheScreenStaysLocked_UntilTheKeeperIsDisposed()
    {
        var screen = new FakeScreenLock();
        using var sessionCertificate = SelfSignedCertificate.Create("session-under-test");
        using var masterCertificate = SelfSignedCertificate.Create("master-under-test");
        using var session = new ThumbnailPollerTests.LoopbackSession(
            0, sessionCertificate, masterCertificate, screenLock: screen);
        var statuses = new StatusLog();
        var keeper = new ScreenLockKeeper(session.Machine(), session.MasterCertificate, session.Port);
        keeper.StatusChanged += statuses.Add;

        keeper.Start();
        await WaitUntil(() => screen.Showing && statuses.Count > 0);
        Assert.Equal((true, "tela bloqueada"), statuses.Last());

        // Past a few heartbeats, well inside the Session's 15 s timeout.
        await Task.Delay(ScreenLockKeeper.HeartbeatInterval * 2);
        Assert.True(screen.Showing);

        keeper.Dispose();
        await WaitUntil(() => !screen.Showing);
    }

    // A machine that was off, or restarted to get out of the lock, is locked
    // again as soon as it's back.
    [Fact]
    public async Task AMachineThatComesBack_IsLockedAgain()
    {
        var port = FreePort();
        using var sessionCertificate = SelfSignedCertificate.Create("session-under-test");
        using var masterCertificate = SelfSignedCertificate.Create("master-under-test");
        var machine = new KnownMachine("Aluno-1", "127.0.0.1", sessionCertificate.Thumbprint, null);
        var statuses = new StatusLog();
        using var keeper = new ScreenLockKeeper(machine, masterCertificate, port);
        keeper.StatusChanged += statuses.Add;

        keeper.Start();
        await WaitUntil(() => statuses.Count > 0);
        Assert.False(statuses.Last().Locked); // nothing listening yet

        var screen = new FakeScreenLock();
        using var session = new ThumbnailPollerTests.LoopbackSession(
            port, sessionCertificate, masterCertificate, screenLock: screen);
        await WaitUntil(() => screen.Showing && statuses.Last().Locked, TimeSpan.FromSeconds(15));
        Assert.Equal((true, "tela bloqueada"), statuses.Last());
    }

    [Fact]
    public async Task AMachineThatCantLockItsScreen_SaysWhy()
    {
        using var session = new ThumbnailPollerTests.LoopbackSession();
        var statuses = new StatusLog();
        using var keeper = new ScreenLockKeeper(session.Machine(), session.MasterCertificate, session.Port);
        keeper.StatusChanged += statuses.Add;

        keeper.Start();
        await WaitUntil(() => statuses.Count > 0);

        Assert.Equal((false, "esta máquina não bloqueia a tela"), statuses.Last());
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition");
            await Task.Delay(20);
        }
    }

    private sealed class StatusLog
    {
        private readonly List<(bool Locked, string Detail)> _statuses = new();

        public int Count
        {
            get
            {
                lock (_statuses)
                {
                    return _statuses.Count;
                }
            }
        }

        public void Add(bool locked, string detail)
        {
            lock (_statuses)
            {
                _statuses.Add((locked, detail));
            }
        }

        public (bool Locked, string Detail) Last()
        {
            lock (_statuses)
            {
                return _statuses[^1];
            }
        }
    }

    private sealed class FakeScreenLock : IScreenLockDisplay
    {
        private bool _showing;

        public bool Showing => Volatile.Read(ref _showing);

        public void Show() => Volatile.Write(ref _showing, true);

        public void Hide() => Volatile.Write(ref _showing, false);
    }
}
