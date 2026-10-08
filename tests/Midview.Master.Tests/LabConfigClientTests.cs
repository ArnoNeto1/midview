using System.Net;
using System.Net.Sockets;
using Microsoft.Win32;
using Midview.Agent;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// Drives LabConfigClient against a real AgentServer on loopback (never a lab
// machine). An AgentServer built WITHOUT a lab-config handler stands in for
// an Agent from before this feature (it answers CommandAck).
public sealed class LabConfigClientTests : IDisposable
{
    private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _agentCert =
        SelfSignedCertificate.Create("agent-under-test");
    private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _masterCert =
        SelfSignedCertificate.Create("master-under-test");
    private readonly List<Action> _cleanup = new();

    public void Dispose()
    {
        foreach (var step in _cleanup)
        {
            try { step(); } catch { }
        }
        _agentCert.Dispose();
        _masterCert.Dispose();
    }

    private (KnownMachine Machine, int Port) StartAgent(bool withLabConfig)
    {
        LabConfigCommandHandler? labConfig = null;
        if (withLabConfig)
        {
            var rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
            var writer = new LabPolicyWriter(Registry.CurrentUser.CreateSubKey(rootPath, writable: true));
            var statePath = Path.Combine(Path.GetTempPath(), $"midview-labcfg-{Guid.NewGuid():N}.json");
            var usersRoot = Path.Combine(Path.GetTempPath(), $"midview-users-{Guid.NewGuid():N}");
            labConfig = new LabConfigCommandHandler(statePath, writer, new AppBlockEnforcer(new EmptyInspector()), usersRoot);
            _cleanup.Add(() => Registry.CurrentUser.DeleteSubKeyTree(rootPath, throwOnMissingSubKey: false));
            _cleanup.Add(() => File.Delete(statePath));
        }

        var siteBlock = new SiteBlockCommandHandler(new SiteBlockState(), "unused", "unused-hosts", new FakeFirewall());
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new AgentServer(listener, _agentCert, new TrustStore(new[] { _masterCert.Thumbprint }), siteBlock, labConfig: labConfig);
        var stop = new CancellationTokenSource();
        var served = Task.Run(() => server.Run(stop.Token));
        _cleanup.Add(() => { stop.Cancel(); try { served.Wait(TimeSpan.FromSeconds(5)); } catch { } });

        return (new KnownMachine("Aluno-Teste", "127.0.0.1", "unused-session-thumb", _agentCert.Thumbprint), port);
    }

    [Fact]
    public async Task ApplyThenGetStatus_RoundTripsTheConfig()
    {
        var (machine, port) = StartAgent(withLabConfig: true);

        var config = LabConfig.Off(1000) with { Lockdown = true, BlockEdge = true };
        var (applied, _) = await Task.Run(() => LabConfigClient.Apply(machine, _masterCert, config, port))
            .WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(applied);

        var status = await Task.Run(() => LabConfigClient.GetStatus(machine, _masterCert, port)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(status.OldAgent);
        Assert.NotNull(status.Config);
        Assert.True(status.Config!.Lockdown);
        Assert.True(status.Config.BlockEdge);
    }

    [Fact]
    public async Task GetStatus_AgainstAnOlderAgent_ReportsOldAgent()
    {
        var (machine, port) = StartAgent(withLabConfig: false);

        var status = await Task.Run(() => LabConfigClient.GetStatus(machine, _masterCert, port)).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(status.OldAgent);
        Assert.Null(status.Config);
    }

    private sealed class EmptyInspector : IProcessInspector
    {
        public IReadOnlyList<RunningProcess> ListInteractive() => Array.Empty<RunningProcess>();
        public ProcessFacts? Inspect(RunningProcess process) => null;
        public bool End(RunningProcess process) => true;
        public void Notify(int sessionId, string message) { }
    }

    private sealed class FakeFirewall : IFirewallController
    {
        public bool ApplyBlock() => true;
        public void ApplyUnblock() { }
    }
}
