using Midview.Agent;
using Xunit;

// What KillAll ends is a list of process names, checked here. The kills need
// real processes in other logon sessions and are verified live. The names are
// literals on purpose: they are what Task Manager shows and what the
// installer's Stop-Midview looks for.
public class SessionProcessesTests
{
    [Fact]
    public void Names_include_the_session_and_the_desk_helper()
    {
        Assert.Contains("Midview.Session", SessionProcesses.Names);
        Assert.Contains("Midview.Desk", SessionProcesses.Names);
    }

    // The applier that calls KillAll is a Midview.Agent too: ending it would
    // end the update halfway.
    [Fact]
    public void Names_do_not_include_the_agent()
    {
        Assert.DoesNotContain("Midview.Agent", SessionProcesses.Names);
    }
}
