using Midview.Agent;
using Xunit;

public class FakeSessionProcessLauncher : ISessionProcessLauncher
{
    private readonly Queue<bool> _launchResults;
    private readonly HashSet<int> _runningProcessIds = new();
    private int _nextProcessId = 1;

    public FakeSessionProcessLauncher(params bool[] launchResults)
    {
        _launchResults = new Queue<bool>(launchResults);
    }

    public int LaunchAttempts { get; private set; }

    public bool TryLaunch(out int processId)
    {
        LaunchAttempts++;
        processId = 0;
        if (_launchResults.Count == 0 || !_launchResults.Dequeue())
        {
            return false;
        }
        processId = _nextProcessId++;
        _runningProcessIds.Add(processId);
        return true;
    }

    public bool IsRunning(int processId) => _runningProcessIds.Contains(processId);

    public void SimulateCrash(int processId) => _runningProcessIds.Remove(processId);
}

public class SessionSupervisorTests
{
    [Fact]
    public void EnsureRunning_NoProcessYet_LaunchesOne()
    {
        var launcher = new FakeSessionProcessLauncher(true);
        var supervisor = new SessionSupervisor(launcher, TimeSpan.FromSeconds(5));

        supervisor.EnsureRunning();

        Assert.Equal(1, launcher.LaunchAttempts);
    }

    [Fact]
    public void EnsureRunning_ProcessStillRunning_DoesNotRelaunch()
    {
        var launcher = new FakeSessionProcessLauncher(true);
        var supervisor = new SessionSupervisor(launcher, TimeSpan.FromSeconds(5));
        supervisor.EnsureRunning();

        supervisor.EnsureRunning();

        Assert.Equal(1, launcher.LaunchAttempts);
    }

    [Fact]
    public void EnsureRunning_ProcessCrashed_RelaunchesIt()
    {
        var launcher = new FakeSessionProcessLauncher(true, true);
        var supervisor = new SessionSupervisor(launcher, TimeSpan.FromSeconds(5));
        supervisor.EnsureRunning();
        launcher.SimulateCrash(1);

        supervisor.EnsureRunning();

        Assert.Equal(2, launcher.LaunchAttempts);
    }

    [Fact]
    public void EnsureRunning_LaunchFails_DoesNotThrow()
    {
        var launcher = new FakeSessionProcessLauncher(false);
        var supervisor = new SessionSupervisor(launcher, TimeSpan.FromSeconds(5));

        supervisor.EnsureRunning();

        Assert.Equal(1, launcher.LaunchAttempts);
    }
}
