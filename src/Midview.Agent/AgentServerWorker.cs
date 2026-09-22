using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Midview.Protocol;

namespace Midview.Agent;

public sealed class AgentServerWorker : BackgroundService
{
    public const int AgentPort = 28900;
    public const int SessionPort = 5901;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var programDataDir = AgentDataDirectory.EnsureSecure();

            var certificate = AgentIdentity.LoadOrCreate(AgentIdentity.DefaultCertificatePath);
            AgentLog.Warn($"Agent certificate thumbprint: {certificate.Thumbprint}");

            EnsureInboundFirewallRuleExists(AgentPort);

            var trustedMasters = TrustedMastersFile.Load(Path.Combine(programDataDir, "trusted-masters.json"));
            var statePath = Path.Combine(programDataDir, "site-block-state.json");
            var state = SiteBlockState.Load(statePath);
            var hostsFilePath = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\drivers\etc\hosts");
            var firewallController = new NetshFirewallController(AgentPort, SessionPort);
            var commandHandler = new SiteBlockCommandHandler(state, statePath, hostsFilePath, firewallController);

            if (!commandHandler.ReconcileAtStartup())
            {
                AgentLog.Warn("Failed to reapply the full-internet-block firewall rules at startup — the persisted state says it should be on, but the firewall reconcile failed.");
            }

            var server = new AgentServer(
                new TcpListener(IPAddress.Any, AgentPort), certificate, new TrustStore(trustedMasters), commandHandler);

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

    private static void EnsureInboundFirewallRuleExists(int port)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("netsh.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[]
        {
            "advfirewall", "firewall", "add", "rule",
            "name=MidviewAgentInbound", "dir=in", "action=allow",
            "protocol=TCP", $"localport={port}",
        })
        {
            startInfo.ArgumentList.Add(arg);
        }
        using var process = System.Diagnostics.Process.Start(startInfo);
        process?.WaitForExit();
    }
}
