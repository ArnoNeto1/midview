namespace Midview.Agent;

// The two network-update flags (see UpdateApplier). Part of the contract
// between versions: the build running them is the one being replaced, so
// they must keep working exactly like this in every later build.
public static class UpdateCommands
{
    public const string ApplyArgument = "--apply-update";
    public const string RecoverArgument = "--recover-update";

    public static int RunApply() => Run(applier => applier.Apply(), "apply");

    public static int RunRecover() => Run(applier => applier.Recover(), "recover");

    private static int Run(Func<UpdateApplier, int> step, string name)
    {
        try
        {
            try
            {
                UpdatePaths.Default.EnsureStateDirectory();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AgentLog.Warn($"Update {name}: couldn't protect the update folder: {ex.Message}");
            }
            return step(new UpdateApplier(
                UpdatePaths.Default, new WindowsServiceControl(), new SchtasksRecoveryTask(), SessionProcesses.KillAll,
                SystemUpdateClock.Instance));
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Update {name} crashed: {ex}");
            return 1;
        }
    }
}
