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
        var me = WindowsIdentity.GetCurrent().User!;
        var worker = new MidviewAgentWorker(
            new SessionSupervisor(session, TimeSpan.FromMinutes(1)),
            new HelperSupervisor(new SessionSupervisor(helper, TimeSpan.FromMinutes(1))),
            new StudentFolderGuard(() => null, me),
            new ClassLessonHandler(
                Path.Combine(Path.GetTempPath(), $"midview-lesson-{Guid.NewGuid():N}.json"),
                () => null, _ => null, new AppBlockEnforcer(new NoProcesses()), me, () => DateTime.Now));

        // The first pass runs before StartAsync returns; the loop then waits for the next poll.
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, session.LaunchAttempts);
        Assert.Equal(1, helper.LaunchAttempts);
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
