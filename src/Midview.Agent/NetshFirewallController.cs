using System.Diagnostics;

namespace Midview.Agent;

public sealed class NetshFirewallController : IFirewallController
{
    private readonly int _agentPort;
    private readonly int _sessionPort;

    public NetshFirewallController(int agentPort, int sessionPort)
    {
        _agentPort = agentPort;
        _sessionPort = sessionPort;
    }

    public bool ApplyBlock()
    {
        // "No matching rule" is the normal case here, so exit codes are ignored.
        foreach (var args in FirewallCommands.BuildRemoveRuleCommands())
        {
            RunNetsh(args);
        }

        foreach (var args in FirewallCommands.BuildBlockCommands(_agentPort, _sessionPort))
        {
            if (RunNetsh(args) != 0)
            {
                return false;
            }
        }
        return true;
    }

    public void ApplyUnblock()
    {
        // Sent unconditionally, including when no block was ever applied --
        // "no matching rule" is the expected common case here, not a
        // failure, so every command is attempted regardless of the
        // previous one's exit code (mirrors Treezeview's own FirewallController
        // reasoning for the equivalent unblock call).
        foreach (var args in FirewallCommands.BuildUnblockCommands())
        {
            RunNetsh(args);
        }
    }

    private static int RunNetsh(string[] args)
    {
        var startInfo = new ProcessStartInfo("netsh.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }
        using var process = Process.Start(startInfo);
        process!.WaitForExit();
        return process.ExitCode;
    }
}
