using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Midview.Protocol;

namespace Midview.Agent;

public sealed class AgentServerWorker : BackgroundService
{
    public const int AgentPort = 28900;
    public const int SessionPort = 5901;

    private const string AgentInboundRuleName = "MidviewAgentInbound";
    private const string SessionInboundRuleName = "MidviewSessionInbound";

    // The inbound openings added on every start; the uninstaller removes them.
    public static readonly IReadOnlyList<string> InboundRuleNames = new[] { AgentInboundRuleName, SessionInboundRuleName };

    public static string SiteBlockStatePath => Path.Combine(AgentDataDirectory.DirectoryPath, "site-block-state.json");

    public static string HostsFilePath => Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\drivers\etc\hosts");

    private readonly ScheduledShutdown _scheduledShutdown;

    public AgentServerWorker(ScheduledShutdown scheduledShutdown)
    {
        _scheduledShutdown = scheduledShutdown;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var programDataDir = AgentDataDirectory.DirectoryPath;

            var certificate = AgentIdentity.LoadOrCreate(AgentIdentity.DefaultCertificatePath);

            EnsureInboundFirewallRuleExists(AgentInboundRuleName, AgentPort);
            // Session can't add its own rule: it runs as a standard user, and
            // Windows' "allow access?" prompt needs an administrator.
            EnsureInboundFirewallRuleExists(SessionInboundRuleName, SessionPort);

            var trustedMasters = TrustedMastersFile.Load(Path.Combine(programDataDir, "trusted-masters.json"));
            var statePath = SiteBlockStatePath;
            var state = LoadStateOrStartEmpty(statePath);
            var hostsFilePath = HostsFilePath;
            var firewallController = new NetshFirewallController(AgentPort, SessionPort);
            var commandHandler = new SiteBlockCommandHandler(state, statePath, hostsFilePath, firewallController);

            // Isolated from the listener startup below: a hosts-file error here
            // (e.g. a locked file, antivirus scanning it at boot) must not stop
            // the Agent from listening at all -- that would lock the teacher out
            // of sending even an unblock command until the next restart.
            try
            {
                if (!commandHandler.ReconcileAtStartup())
                {
                    AgentLog.Warn("Failed to reapply the full-internet-block firewall rules at startup — the persisted state says it should be on, but the firewall reconcile failed.");
                }
            }
            catch (Exception ex)
            {
                AgentLog.Warn($"Failed to reconcile site-block state at startup: {ex.Message}");
            }

            var server = new AgentServer(
                new TcpListener(IPAddress.Any, AgentPort), certificate, new TrustStore(trustedMasters), commandHandler,
                new PowerCommandHandler(PowerCommandHandler.RunShutdownExe), scheduledShutdown: _scheduledShutdown);

            return Task.Run(() =>
            {
                try
                {
                    server.Run(stoppingToken);
                }
                catch (Exception ex)
                {
                    AgentLog.Warn($"AgentServer.Run failed: {ex.Message}");
                }
            }, stoppingToken);
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Failed to start the site-blocking listener: {ex.Message}");
            return Task.CompletedTask;
        }
    }

    // Same reasoning as the reconcile isolation above: state that can't be
    // read (a corrupt file is already handled inside Load; this covers I/O
    // errors like a locked file) must not keep the listener from starting.
    // Nothing blocked is the direction the teacher can always fix remotely.
    private static SiteBlockState LoadStateOrStartEmpty(string statePath)
    {
        try
        {
            return SiteBlockState.Load(statePath);
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Failed to load site-block state, starting with nothing blocked: {ex.Message}");
            return new SiteBlockState();
        }
    }

    private static void EnsureInboundFirewallRuleExists(string ruleName, int port)
    {
        // "add rule" never replaces a rule with the same name, so it's deleted
        // first -- otherwise every Agent start would add one more copy.
        RunNetsh("advfirewall", "firewall", "delete", "rule", $"name={ruleName}");
        RunNetsh("advfirewall", "firewall", "add", "rule",
            $"name={ruleName}", "dir=in", "action=allow",
            "protocol=TCP", $"localport={port}");
    }

    internal static void RunNetsh(params string[] args)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("netsh.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }
        using var process = System.Diagnostics.Process.Start(startInfo);
        process?.WaitForExit();
    }
}
