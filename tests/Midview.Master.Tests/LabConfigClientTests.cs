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

    // runUninstaller: what the Agent's uninstaller does with each command
    // (succeeds at once, by default).
    private (KnownMachine Machine, int Port) StartAgent(bool withLabConfig, Func<string, int?>? runUninstaller = null)
    {
        LabConfigCommandHandler? labConfig = null;
        ProgramUninstaller? uninstaller = null;
        if (withLabConfig)
        {
            var rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
            var writer = new LabPolicyWriter(Registry.CurrentUser.CreateSubKey(rootPath, writable: true));
            var statePath = Path.Combine(Path.GetTempPath(), $"midview-labcfg-{Guid.NewGuid():N}.json");
            var usersRoot = Path.Combine(Path.GetTempPath(), $"midview-users-{Guid.NewGuid():N}");
            labConfig = new LabConfigCommandHandler(statePath, writer, new AppBlockEnforcer(new EmptyInspector()), usersRoot);
            using (var app = Registry.CurrentUser.CreateSubKey(
                rootPath + @"\Microsoft\Windows\CurrentVersion\Uninstall\AppQuiet", writable: true))
            {
                app.SetValue("DisplayName", "App Quiet");
                app.SetValue("QuietUninstallString", "un.exe /S");
            }
            uninstaller = new ProgramUninstaller(Registry.CurrentUser.CreateSubKey(rootPath, writable: true), runUninstaller ?? (_ => 0));
            _cleanup.Add(() => Registry.CurrentUser.DeleteSubKeyTree(rootPath, throwOnMissingSubKey: false));
            _cleanup.Add(() => File.Delete(statePath));
        }

        var siteBlock = new SiteBlockCommandHandler(new SiteBlockState(), "unused", "unused-hosts", new FakeFirewall());
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new AgentServer(listener, _agentCert, new TrustStore(new[] { _masterCert.Thumbprint }), siteBlock,
            labConfig: labConfig, uninstaller: uninstaller);
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

    [Fact]
    public async Task GetInstalledPrograms_ListsThem()
    {
        var (machine, port) = StartAgent(withLabConfig: true);

        var result = await Task.Run(() => LabConfigClient.GetInstalledPrograms(machine, _masterCert, port))
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(result.OldAgent);
        Assert.Contains("App Quiet", result.Programs!);
    }

    [Fact]
    public async Task UninstallPrograms_RunsThem()
    {
        var (machine, port) = StartAgent(withLabConfig: true);

        var (ok, detail) = await Task.Run(() => LabConfigClient.UninstallPrograms(machine, _masterCert, new[] { "App Quiet" }, port))
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(ok);
        Assert.Contains("desinstalado", detail);
    }

    [Fact]
    public async Task UninstallPrograms_ASlowUninstall_IsWaitedFor()
    {
        // Longer than the 20 s every other command gets: a big program takes
        // minutes, and the teacher used to see "Sem resposta" for a machine
        // that went on to remove it.
        var (machine, port) = StartAgent(withLabConfig: true, runUninstaller: _ =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(25));
            return 0;
        });

        var (ok, detail) = await Task.Run(() => LabConfigClient.UninstallPrograms(machine, _masterCert, new[] { "App Quiet" }, port))
            .WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(ok);
        Assert.Contains("1 desinstalado", detail);
    }

    [Fact]
    public void UninstallTimeout_GrowsWithThePrograms()
    {
        Assert.Equal(TimeSpan.FromSeconds(20) + TimeSpan.FromMinutes(10), LabConfigClient.UninstallTimeout(0));
        Assert.Equal(TimeSpan.FromSeconds(20) + TimeSpan.FromMinutes(10), LabConfigClient.UninstallTimeout(1));
        Assert.Equal(TimeSpan.FromSeconds(20) + TimeSpan.FromMinutes(30), LabConfigClient.UninstallTimeout(3));
    }

    [Fact]
    public async Task ClearDownloads_IsAccepted()
    {
        var (machine, port) = StartAgent(withLabConfig: true);

        var (ok, _) = await Task.Run(() => LabConfigClient.ClearDownloads(machine, _masterCert, DownloadsScope.Today, port))
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(ok);
    }

    [Fact]
    public async Task GetInstalledPrograms_AgainstAnOlderAgent_ReportsOldAgent()
    {
        var (machine, port) = StartAgent(withLabConfig: false);

        var result = await Task.Run(() => LabConfigClient.GetInstalledPrograms(machine, _masterCert, port))
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(result.OldAgent);
        Assert.Null(result.Programs);
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
