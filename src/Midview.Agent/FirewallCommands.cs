namespace Midview.Agent;

public static class FirewallCommands
{
    private const string RuleNamePrefix = "MidviewSiteBlock";

    public static IReadOnlyList<string[]> BuildBlockCommands(int agentPort, int sessionPort)
    {
        return new[]
        {
            new[] { "advfirewall", "firewall", "add", "rule",
                $"name={RuleNamePrefix}_AllowLocalNetwork", "dir=out", "action=allow",
                "localip=192.168.0.0/16", "remoteip=192.168.0.0/16" },
            new[] { "advfirewall", "firewall", "add", "rule",
                $"name={RuleNamePrefix}_AllowAgentPort", "dir=out", "action=allow",
                "protocol=TCP", $"remoteport={agentPort}" },
            new[] { "advfirewall", "firewall", "add", "rule",
                $"name={RuleNamePrefix}_AllowSessionPort", "dir=out", "action=allow",
                "protocol=TCP", $"remoteport={sessionPort}" },
            new[] { "advfirewall", "firewall", "add", "rule",
                $"name={RuleNamePrefix}_BlockAllOutbound", "dir=out", "action=block" },
        };
    }

    public static IReadOnlyList<string[]> BuildUnblockCommands()
    {
        return new[]
        {
            new[] { "advfirewall", "firewall", "delete", "rule", $"name={RuleNamePrefix}_AllowLocalNetwork" },
            new[] { "advfirewall", "firewall", "delete", "rule", $"name={RuleNamePrefix}_AllowAgentPort" },
            new[] { "advfirewall", "firewall", "delete", "rule", $"name={RuleNamePrefix}_AllowSessionPort" },
            new[] { "advfirewall", "firewall", "delete", "rule", $"name={RuleNamePrefix}_BlockAllOutbound" },
        };
    }
}
