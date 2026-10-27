namespace Midview.Agent;

// Keeps the Desk helper running the way SessionSupervisor keeps Session running:
// it is a SessionSupervisor over another launcher (ConsoleSystemProcessLauncher).
// A type of its own, so the helper's supervisor and Session's can be told apart
// when they are injected.
public sealed class HelperSupervisor
{
    private readonly SessionSupervisor _supervisor;
    private readonly Action<string> _log;
    private string? _lastFailure;

    public HelperSupervisor(SessionSupervisor supervisor)
        : this(supervisor, AgentLog.Warn)
    {
    }

    // The log is injectable so the guard below can be tested without writing the
    // real agent.log.
    internal HelperSupervisor(SessionSupervisor supervisor, Action<string> log)
    {
        _supervisor = supervisor;
        _log = log;
    }

    // Never throws. The helper is a SYSTEM process in another logon session, so even
    // checking whether it still runs can fail (Process.HasExited raises a
    // Win32Exception when access is denied), and an exception out of here would end
    // MidviewAgentWorker's loop and take the whole Agent down. The failure is logged
    // -- once, not on every poll -- and the next pass tries again.
    public void EnsureRunning()
    {
        try
        {
            _supervisor.EnsureRunning();
            _lastFailure = null;
        }
        catch (Exception ex)
        {
            var failure = $"Couldn't keep the Desk helper running ({ex.GetType().Name}): {ex.Message}";
            if (failure != _lastFailure)
            {
                _lastFailure = failure;
                _log(failure);
            }
        }
    }
}
