using System.Linq;
using Midview.Agent;
using Xunit;

public class FirewallCommandsTests
{
    [Fact]
    public void BuildBlockCommands_IncludesLocalNetworkAllowRule()
    {
        var commands = FirewallCommands.BuildBlockCommands(agentPort: 5900, sessionPort: 5901);

        Assert.Contains(commands, c => c.Contains("localip=192.168.0.0/16"));
    }

    [Fact]
    public void BuildBlockCommands_AllowsBothAgentAndSessionPorts()
    {
        var commands = FirewallCommands.BuildBlockCommands(agentPort: 5900, sessionPort: 5901);

        Assert.Contains(commands, c => c.Contains("remoteport=5900"));
        Assert.Contains(commands, c => c.Contains("remoteport=5901"));
    }

    [Fact]
    public void BuildBlockCommands_EndsWithBlockAllOutboundRule()
    {
        var commands = FirewallCommands.BuildBlockCommands(agentPort: 5900, sessionPort: 5901);

        Assert.Contains(commands, c => c.Contains("action=block"));
    }

    [Fact]
    public void BuildUnblockCommands_DeletesEveryRuleBuildBlockCommandsCreates()
    {
        var blockCommands = FirewallCommands.BuildBlockCommands(agentPort: 5900, sessionPort: 5901);
        var unblockCommands = FirewallCommands.BuildUnblockCommands();

        var blockedRuleNames = blockCommands
            .Select(c => c.First(arg => arg.StartsWith("name=")))
            .OrderBy(n => n)
            .ToList();
        var unblockedRuleNames = unblockCommands
            .Select(c => c.First(arg => arg.StartsWith("name=")))
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(blockedRuleNames, unblockedRuleNames);
    }
}
