namespace Midview.Agent;

// Keeps the Desk helper running the way SessionSupervisor keeps Session running:
// it is a SessionSupervisor over another launcher (ConsoleSystemProcessLauncher).
// A type of its own, so the helper's supervisor and Session's can be told apart
// when they are injected.
public sealed class HelperSupervisor
{
    private readonly SessionSupervisor _supervisor;

    public HelperSupervisor(SessionSupervisor supervisor)
    {
        _supervisor = supervisor;
    }

    public void EnsureRunning() => _supervisor.EnsureRunning();
}
