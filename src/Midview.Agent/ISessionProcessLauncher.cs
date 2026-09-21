namespace Midview.Agent;

public interface ISessionProcessLauncher
{
    bool TryLaunch(out int processId);
    bool IsRunning(int processId);
}
