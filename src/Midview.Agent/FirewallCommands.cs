namespace Midview.Agent;

public static class FirewallCommands
{
    private const string RuleNamePrefix = "MidviewSiteBlock";

    public static IReadOnlyList<string[]> BuildBlockCommands(int agentPort, int sessionPort)
    {
        return new[]
        {
            // remoteip=LocalSubnet alone covers this machine regardless of which
            // private range it's actually on -- an earlier localip=192.168.0.0/16
            // constraint here made the rule silently match nothing at all on a
            // 10.x or 172.16-31.x lab network.
            new[] { "advfirewall", "firewall", "add", "rule",
                $"name={RuleNamePrefix}_AllowLocalNetwork", "dir=out", "action=allow",
                "remoteip=LocalSubnet" },
            // Scoped to the local subnet too: without this, a student could still
            // reach an internet server listening on either port while "blocked".
            new[] { "advfirewall", "firewall", "add", "rule",
                $"name={RuleNamePrefix}_AllowAgentPort", "dir=out", "action=allow",
                "protocol=TCP", $"remoteport={agentPort}", "remoteip=LocalSubnet" },
            new[] { "advfirewall", "firewall", "add", "rule",
                $"name={RuleNamePrefix}_AllowSessionPort", "dir=out", "action=allow",
                "protocol=TCP", $"remoteport={sessionPort}", "remoteip=LocalSubnet" },
            // Windows Firewall evaluates block rules before allow rules regardless of
            // add order, so an explicit block-all rule here would silently override the
            // three allow rules above and cut off the LAN and Midview's own ports too.
            // Setting the profile-wide default outbound policy to "block" is what the
            // allow rules above actually take precedence over.
            new[] { "advfirewall", "set", "allprofiles", "firewallpolicy", "blockinbound,blockoutbound" },
        };
    }

    public static IReadOnlyList<string[]> BuildUnblockCommands()
    {
        return new[]
        {
            // Restore Windows' own documented default profile policy -- matches the
            // existing "don't try to be clever, just get to the known-good state"
            // philosophy already used elsewhere in this codebase (see HostsFileManager).
            new[] { "advfirewall", "set", "allprofiles", "firewallpolicy", "blockinbound,allowoutbound" },
            new[] { "advfirewall", "firewall", "delete", "rule", $"name={RuleNamePrefix}_AllowLocalNetwork" },
            new[] { "advfirewall", "firewall", "delete", "rule", $"name={RuleNamePrefix}_AllowAgentPort" },
            new[] { "advfirewall", "firewall", "delete", "rule", $"name={RuleNamePrefix}_AllowSessionPort" },
        };
    }
}
