using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Midview.Protocol;

namespace Midview.Agent;

public sealed class AgentServerWorker : BackgroundService
{
    public const int AgentPort = 5900;
    public const int SessionPort = 5901;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var programDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Midview");
        Directory.CreateDirectory(programDataDir);

        var certificate = AgentIdentity.LoadOrCreate(AgentIdentity.DefaultCertificatePath);
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

        return Task.Run(() => server.Run(stoppingToken), stoppingToken);
    }
}
