using Midview.Agent;
using Xunit;

// The real launch places the Desk helper in the console session with a
// duplicated SYSTEM token, which needs the Agent running as the SYSTEM service
// and someone at the console; it is verified live. The one piece that can be
// checked here is the gate: with nobody logged on at the console, the launcher
// starts nothing. The logged-on check is injected so this needs no real session.
public sealed class ConsoleSystemProcessLauncherTests
{
    [Fact]
    public void Launch_is_refused_when_nobody_is_logged_on()
    {
        var launcher = new ConsoleSystemProcessLauncher(
            @"C:\nonexistent\Midview.Desk.exe", someoneLoggedOn: () => false);

        var started = launcher.TryLaunch(out var processId);

        Assert.False(started);
        Assert.Equal(0, processId);
    }
}
