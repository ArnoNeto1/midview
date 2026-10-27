using System.ComponentModel;
using Midview.Agent;
using Xunit;

// The helper is a SYSTEM process in another logon session, so even checking
// whether it still runs can fail (Process.HasExited raises a Win32Exception when
// access is denied). The Agent has to outlive that: an exception out of
// EnsureRunning would end MidviewAgentWorker's loop and, with it, the service.
public class HelperSupervisorTests
{
    [Fact]
    public void ACheckThatFails_DoesNotThrow_AndIsLogged()
    {
        var launcher = new CheckableLauncher();
        var log = new List<string>();
        var supervisor = Supervisor(launcher, log);
        supervisor.EnsureRunning(); // launches it
        launcher.Fail = new Win32Exception(5, "Access is denied");

        var thrown = Record.Exception(supervisor.EnsureRunning);

        Assert.Null(thrown);
        Assert.Contains("Access is denied", Assert.Single(log));
    }

    // The poll comes every few seconds: the same failure on each one would fill
    // agent.log and push out everything else in it.
    [Fact]
    public void ARepeatedFailure_IsLoggedOnce_AndAgainWhenItComesBackLater()
    {
        var launcher = new CheckableLauncher();
        var log = new List<string>();
        var supervisor = Supervisor(launcher, log);
        supervisor.EnsureRunning();
        launcher.Fail = new Win32Exception(5, "Access is denied");

        supervisor.EnsureRunning();
        supervisor.EnsureRunning();
        supervisor.EnsureRunning();
        Assert.Single(log);

        launcher.Fail = null;
        supervisor.EnsureRunning();
        launcher.Fail = new Win32Exception(5, "Access is denied");
        supervisor.EnsureRunning();

        Assert.Equal(2, log.Count);
    }

    private static HelperSupervisor Supervisor(CheckableLauncher launcher, List<string> log) =>
        new(new SessionSupervisor(launcher, TimeSpan.FromSeconds(5)), log.Add);

    // Starts fine; checking on what it started fails while Fail is set.
    private sealed class CheckableLauncher : ISessionProcessLauncher
    {
        public Exception? Fail { get; set; }

        public bool TryLaunch(out int processId)
        {
            processId = 42;
            return true;
        }

        public bool IsRunning(int processId) => Fail is null ? true : throw Fail;
    }
}
