using Midview.Agent;
using Xunit;

// The inbound firewall openings the Agent adds at every start and the uninstaller
// takes away again (UninstallCleanup runs through InboundRuleNames).
public class AgentServerWorkerTests
{
    // The Master's control through the Desk helper reaches it inbound on its own
    // port, which the Agent opens; the rule must be on the list the uninstaller
    // removes, or uninstalling Midview would leave the opening behind.
    [Fact]
    public void InboundRuleNames_includes_the_desk_port_rule()
    {
        Assert.Equal(5902, AgentServerWorker.DeskPort);
        Assert.Contains("MidviewDeskInbound", AgentServerWorker.InboundRuleNames);
    }

    // A student can't approve Windows' "allow access?" prompt for Session, and
    // cancelling it leaves inbound block rules for its exe that beat the port
    // rule: the teacher saw the machine "Offline". A network update runs
    // Session from a new folder, so Windows asks again after each one and an
    // old block can be for any of them. Windows' rules (named after the exe,
    // "Midview.Session", whatever the folder) and any rule for this exe go,
    // then this exe is allowed, which also keeps the prompt from coming back.
    [Fact]
    public void SessionProgramRule_ClearsTheExesBlockRules_ThenAllowsTheExe()
    {
        const string exe = @"C:\Program Files\Midview.update\abc123\Midview.Session.exe";

        var commands = AgentServerWorker.SessionProgramRuleCommands(exe);

        Assert.Equal(3, commands.Count);
        Assert.Equal(
            new[] { "advfirewall", "firewall", "delete", "rule", "name=Midview.Session", "dir=in" },
            commands[0]);
        Assert.Equal(
            new[] { "advfirewall", "firewall", "delete", "rule", "name=all", "dir=in", $"program={exe}" },
            commands[1]);
        Assert.Equal(
            new[]
            {
                "advfirewall", "firewall", "add", "rule", "name=MidviewSessionProgram", "dir=in", "action=allow",
                $"program={exe}", "enable=yes", "profile=any",
            },
            commands[2]);
        Assert.Contains("MidviewSessionProgram", AgentServerWorker.InboundRuleNames);
    }
}
