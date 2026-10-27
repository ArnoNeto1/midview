using System.Security.Principal;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

// One pass of the Agent's loop: Session and the Desk helper are each kept running
// through a supervisor of their own. The real launches are verified live (they need
// the SYSTEM service and someone at the console); here both are fakes. Nobody is
// logged on and no lesson is on, so the loop's other work has nothing to do.
public sealed class MidviewAgentWorkerTests
{
    [Fact]
    public async Task EachPass_KeepsBothTheSessionAndTheDeskHelperRunning()
    {
        var session = new FakeSessionProcessLauncher(true);
        var helper = new FakeSessionProcessLauncher(true);
        var worker = Worker(session, helper, TimeSpan.FromMinutes(1));

        // The first pass runs before StartAsync returns; the loop then waits for the next poll.
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, session.LaunchAttempts);
        Assert.Equal(1, helper.LaunchAttempts);
    }

    // A helper left by an earlier Agent (a restart, a crash, an update) still holds
    // its port and its exe, and the supervisor can't adopt it: it doesn't know its
    // pid. So it is ended once, before the first launch -- not on every pass, which
    // would end the helper the supervisor itself started.
    [Fact]
    public async Task AStrayHelper_IsEndedOnce_BeforeTheFirstLaunch()
    {
        var session = new FakeSessionProcessLauncher(true);
        var helper = new FakeSessionProcessLauncher(false); // never starts, so every pass tries again
        var ended = 0;
        var launchesBefore = -1;
        var worker = Worker(session, helper, TimeSpan.FromMilliseconds(10), clearStrayHelper: () =>
        {
            if (ended++ == 0)
            {
                launchesBefore = helper.LaunchAttempts;
            }
        });

        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => helper.LaunchAttempts >= 3);
        var endedWhileRunning = ended;
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, endedWhileRunning);
        Assert.Equal(0, launchesBefore);
    }

    // The service stopping is the one time a SYSTEM process is sure to be allowed to end
    // the SYSTEM helper; without this a plain Stop-Service would leave it running.
    [Fact]
    public async Task StopAsync_ends_the_helper()
    {
        var session = new FakeSessionProcessLauncher(true);
        var helper = new FakeSessionProcessLauncher(true);
        var ended = 0;
        var worker = Worker(session, helper, TimeSpan.FromMinutes(1), clearStrayHelper: () => ended++);

        await worker.StartAsync(CancellationToken.None);
        Assert.Equal(1, ended); // the leftover one, at start
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, ended);
    }

    // Starting is how the Agent listens for the teacher: nothing about a leftover
    // helper may stand in the way.
    [Fact]
    public async Task AStrayHelperThatCannotBeEnded_DoesNotKeepTheAgentFromRunning()
    {
        var session = new FakeSessionProcessLauncher(true);
        var helper = new FakeSessionProcessLauncher(true);
        var log = new List<string>();
        var worker = Worker(session, helper, TimeSpan.FromMinutes(1),
            clearStrayHelper: () => throw new InvalidOperationException("no process list"), log: log.Add);

        await worker.StartAsync(CancellationToken.None);
        Assert.Contains("no process list", Assert.Single(log));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, session.LaunchAttempts);
        Assert.Equal(1, helper.LaunchAttempts);
    }

    // Ending a leftover helper and the log are injected, so no real process is ended
    // (a helper someone is running on this machine stays) and the real agent.log is
    // left alone.
    private static MidviewAgentWorker Worker(
        FakeSessionProcessLauncher session, FakeSessionProcessLauncher helper, TimeSpan poll,
        Action? clearStrayHelper = null, Action<string>? log = null)
    {
        var me = WindowsIdentity.GetCurrent().User!;
        return new MidviewAgentWorker(
            new SessionSupervisor(session, poll),
            new HelperSupervisor(new SessionSupervisor(helper, poll)),
            new StudentFolderGuard(() => null, me),
            new ClassLessonHandler(
                Path.Combine(Path.GetTempPath(), $"midview-lesson-{Guid.NewGuid():N}.json"),
                () => null, _ => null, new AppBlockEnforcer(new NoProcesses()), me, () => DateTime.Now),
            clearStrayHelper ?? (() => { }), log ?? (_ => { }));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "the loop didn't get that far in time");
    }

    private sealed class NoProcesses : IProcessInspector
    {
        public IReadOnlyList<RunningProcess> ListInteractive() => Array.Empty<RunningProcess>();

        public ProcessFacts? Inspect(RunningProcess process) => null;

        public bool End(RunningProcess process) => false;

        public void Notify(int sessionId, string message)
        {
        }
    }
}
