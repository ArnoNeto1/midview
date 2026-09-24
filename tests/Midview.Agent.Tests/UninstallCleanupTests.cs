using System.Text.Json;
using Midview.Agent;
using Xunit;

public class UninstallCleanupTests : IDisposable
{
    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"midview-state-{Guid.NewGuid()}.json");
    private readonly string _hostsPath = Path.Combine(Path.GetTempPath(), $"midview-hosts-{Guid.NewGuid()}.txt");
    private readonly List<string[]> _netshCalls = new();

    public void Dispose()
    {
        File.Delete(_statePath);
        File.Delete(_hostsPath);
    }

    [Fact]
    public void UndoesEverythingALessonCanLeaveBehind()
    {
        File.WriteAllText(_hostsPath, HostsFileManager.Reconcile("127.0.0.1 localhost\n", new[] { "youtube.com" }));
        new SiteBlockState { InternetBlocked = true, BlockedDomains = { "youtube.com" } }.Save(_statePath);
        var firewall = new FakeFirewallController();
        var devices = new FakeInputDeviceController();

        var failures = UninstallCleanup.Run(_statePath, _hostsPath, firewall, devices, _netshCalls.Add);

        Assert.Empty(failures);
        Assert.Equal(1, devices.EnableCalls);
        Assert.Equal("127.0.0.1 localhost\n", File.ReadAllText(_hostsPath));
        Assert.Equal(1, firewall.ApplyUnblockCalls);
        Assert.Equal(0, firewall.ApplyBlockCalls);
        Assert.Equal(
            new[] { "name=MidviewAgentInbound", "name=MidviewSessionInbound" },
            _netshCalls.Select(call => call[^1]));
        Assert.All(_netshCalls, call => Assert.Equal(new[] { "advfirewall", "firewall", "delete", "rule" }, call[..4]));

        var state = SiteBlockState.Load(_statePath);
        Assert.False(state.InternetBlocked);
        Assert.Empty(state.BlockedDomains);
    }

    [Fact]
    public void AFailingStep_DoesNotStopTheOthers_AndIsReported()
    {
        File.WriteAllText(_hostsPath, "127.0.0.1 localhost\n");
        var firewall = new FakeFirewallController();

        var failures = UninstallCleanup.Run(
            _statePath, _hostsPath, firewall, new FailingDevices(),
            _ => throw new InvalidOperationException("netsh não encontrado"));

        Assert.Equal(2, failures.Count);
        Assert.Contains(failures, failure => failure.Contains("teclado e mouse"));
        Assert.Contains(failures, failure => failure.Contains("netsh não encontrado"));
        Assert.Equal(1, firewall.ApplyUnblockCalls);
        Assert.False(SiteBlockState.Load(_statePath).InternetBlocked);
    }

    [Fact]
    public void IdentityJson_HasBothThumbprints()
    {
        using var json = JsonDocument.Parse(InstallCommands.IdentityJson("AAAA", "BBBB"));

        Assert.Equal("AAAA", json.RootElement.GetProperty("agentThumbprint").GetString());
        Assert.Equal("BBBB", json.RootElement.GetProperty("sessionThumbprint").GetString());
    }

    private sealed class FailingDevices : IInputDeviceController
    {
        public bool DisableInputDevices() => false;

        public bool EnableInputDevices() => false;
    }
}
