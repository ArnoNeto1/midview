using System.Linq;
using Midview.Agent;
using Xunit;

public class FirewallCommandsTests
{
    [Fact]
    public void BuildBlockCommands_IncludesLocalNetworkAllowRule()
    {
        var commands = FirewallCommands.BuildBlockCommands(agentPort: 5900, sessionPort: 5901);

        Assert.Contains(commands, c => c.Contains("remoteip=LocalSubnet") && !c.Any(a => a.StartsWith("remoteport=")));
    }

    [Fact]
    public void BuildBlockCommands_NeverHardcodesA192168LocalIpRange()
    {
        var commands = FirewallCommands.BuildBlockCommands(agentPort: 5900, sessionPort: 5901);

        Assert.DoesNotContain(commands, c => c.Any(a => a.StartsWith("localip=")));
    }

    [Fact]
    public void BuildBlockCommands_AllowsBothAgentAndSessionPortsScopedToLocalSubnet()
    {
        var commands = FirewallCommands.BuildBlockCommands(agentPort: 5900, sessionPort: 5901);

        Assert.Contains(commands, c => c.Contains("remoteport=5900") && c.Contains("remoteip=LocalSubnet"));
        Assert.Contains(commands, c => c.Contains("remoteport=5901") && c.Contains("remoteip=LocalSubnet"));
    }

    [Fact]
    public void BuildBlockCommands_SetsBlockOutboundDefaultPolicy()
    {
        var commands = FirewallCommands.BuildBlockCommands(agentPort: 5900, sessionPort: 5901);

        Assert.Contains(commands, c => c.Contains("firewallpolicy") && c.Contains("blockinbound,blockoutbound"));
    }

    [Fact]
    public void BuildUnblockCommands_RestoresAllowOutboundDefaultPolicy()
    {
        var commands = FirewallCommands.BuildUnblockCommands();

        Assert.Contains(commands, c => c.Contains("firewallpolicy") && c.Contains("blockinbound,allowoutbound"));
    }

    [Fact]
    public void BuildUnblockCommands_DeletesEveryRuleBuildBlockCommandsCreates()
    {
        var blockCommands = FirewallCommands.BuildBlockCommands(agentPort: 5900, sessionPort: 5901);
        var unblockCommands = FirewallCommands.BuildUnblockCommands();

        // Policy-set commands (advfirewall set ...) carry no "name=" argument since
        // they don't create a named rule -- only compare the named rules.
        var blockedRuleNames = blockCommands
            .Where(c => c.Any(arg => arg.StartsWith("name=")))
            .Select(c => c.First(arg => arg.StartsWith("name=")))
            .OrderBy(n => n)
            .ToList();
        var unblockedRuleNames = unblockCommands
            .Where(c => c.Any(arg => arg.StartsWith("name=")))
            .Select(c => c.First(arg => arg.StartsWith("name=")))
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(blockedRuleNames, unblockedRuleNames);
    }

    [Fact]
    public void BuildRemoveRuleCommands_DeletesEveryRuleBuildBlockCommandsCreates()
    {
        var blockCommands = FirewallCommands.BuildBlockCommands(agentPort: 5900, sessionPort: 5901);
        var removeCommands = FirewallCommands.BuildRemoveRuleCommands();

        var blockedRuleNames = blockCommands
            .Where(c => c.Any(arg => arg.StartsWith("name=")))
            .Select(c => c.First(arg => arg.StartsWith("name=")))
            .OrderBy(n => n)
            .ToList();
        var removedRuleNames = removeCommands
            .Select(c => c.First(arg => arg.StartsWith("name=")))
            .OrderBy(n => n)
            .ToList();

        Assert.All(removeCommands, c => Assert.Equal(new[] { "advfirewall", "firewall", "delete", "rule" }, c.Take(4)));
        Assert.Equal(blockedRuleNames, removedRuleNames);
    }
}
