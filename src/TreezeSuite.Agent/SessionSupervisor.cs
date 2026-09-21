namespace TreezeSuite.Agent;

public sealed class SessionSupervisor
{
    private readonly ISessionProcessLauncher _launcher;
    private int? _currentProcessId;

    public SessionSupervisor(ISessionProcessLauncher launcher, TimeSpan pollInterval)
    {
        _launcher = launcher;
        PollInterval = pollInterval;
    }

    public TimeSpan PollInterval { get; }

    public void EnsureRunning()
    {
        if (_currentProcessId is int pid && _launcher.IsRunning(pid))
        {
            return;
        }

        _currentProcessId = _launcher.TryLaunch(out var newPid) ? newPid : null;
    }
}
