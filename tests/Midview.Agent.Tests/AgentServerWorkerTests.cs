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
}
