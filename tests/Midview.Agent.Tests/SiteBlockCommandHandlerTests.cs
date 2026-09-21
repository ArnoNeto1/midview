// tests/Midview.Agent.Tests/SiteBlockCommandHandlerTests.cs
using System.Text;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

public class FakeFirewallController : IFirewallController
{
    public int ApplyBlockCalls { get; private set; }
    public int ApplyUnblockCalls { get; private set; }
    public bool BlockShouldSucceed { get; set; } = true;

    public bool ApplyBlock()
    {
        ApplyBlockCalls++;
        return BlockShouldSucceed;
    }

    public void ApplyUnblock() => ApplyUnblockCalls++;
}

public class SiteBlockCommandHandlerTests
{
    private static (SiteBlockCommandHandler handler, string statePath, string hostsPath, FakeFirewallController firewall) CreateHandler()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"midview-state-{Guid.NewGuid()}.json");
        var hostsPath = Path.Combine(Path.GetTempPath(), $"midview-hosts-{Guid.NewGuid()}.txt");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var firewall = new FakeFirewallController();
        var handler = new SiteBlockCommandHandler(new SiteBlockState(), statePath, hostsPath, firewall);
        return (handler, statePath, hostsPath, firewall);
    }

    [Fact]
    public void Handle_BlockInternet_AppliesFirewallBlockAndPersistsState()
    {
        var (handler, statePath, _, firewall) = CreateHandler();

        var result = handler.Handle(MessageType.BlockInternet, Array.Empty<byte>());

        Assert.True(result);
        Assert.Equal(1, firewall.ApplyBlockCalls);
        Assert.True(SiteBlockState.Load(statePath).InternetBlocked);
    }

    [Fact]
    public void Handle_BlockInternet_FirewallFails_DoesNotPersistBlockedState()
    {
        var (handler, statePath, _, firewall) = CreateHandler();
        firewall.BlockShouldSucceed = false;

        var result = handler.Handle(MessageType.BlockInternet, Array.Empty<byte>());

        Assert.False(result);
        Assert.False(SiteBlockState.Load(statePath).InternetBlocked);
    }

    [Fact]
    public void Handle_UnblockInternet_AppliesFirewallUnblockAndPersistsState()
    {
        var (handler, statePath, _, firewall) = CreateHandler();
        handler.Handle(MessageType.BlockInternet, Array.Empty<byte>());

        var result = handler.Handle(MessageType.UnblockInternet, Array.Empty<byte>());

        Assert.True(result);
        Assert.Equal(1, firewall.ApplyUnblockCalls);
        Assert.False(SiteBlockState.Load(statePath).InternetBlocked);
    }

    [Fact]
    public void Handle_BlockDomain_AddsDomainAndRewritesHostsFile()
    {
        var (handler, statePath, hostsPath, _) = CreateHandler();

        var result = handler.Handle(MessageType.BlockDomain, Encoding.UTF8.GetBytes("youtube.com"));

        Assert.True(result);
        Assert.Contains("youtube.com", SiteBlockState.Load(statePath).BlockedDomains);
        Assert.Contains("127.0.0.1 youtube.com", File.ReadAllText(hostsPath));
    }

    [Fact]
    public void Handle_UnblockDomain_RemovesDomainAndRewritesHostsFile()
    {
        var (handler, statePath, hostsPath, _) = CreateHandler();
        handler.Handle(MessageType.BlockDomain, Encoding.UTF8.GetBytes("youtube.com"));

        var result = handler.Handle(MessageType.UnblockDomain, Encoding.UTF8.GetBytes("youtube.com"));

        Assert.True(result);
        Assert.DoesNotContain("youtube.com", SiteBlockState.Load(statePath).BlockedDomains);
        Assert.DoesNotContain("youtube.com", File.ReadAllText(hostsPath));
    }

    [Fact]
    public void ReconcileAtStartup_PersistedDomainsAndBlockState_ReappliesBoth()
    {
        var (handler, statePath, hostsPath, _) = CreateHandler();
        handler.Handle(MessageType.BlockDomain, Encoding.UTF8.GetBytes("youtube.com"));
        handler.Handle(MessageType.BlockInternet, Array.Empty<byte>());
        // Simulate a fresh Agent process: reload persisted state into a new handler.
        var reloadedState = SiteBlockState.Load(statePath);
        var freshFirewall = new FakeFirewallController();
        var freshHandler = new SiteBlockCommandHandler(reloadedState, statePath, hostsPath, freshFirewall);

        var result = freshHandler.ReconcileAtStartup();

        Assert.True(result);
        Assert.Equal(1, freshFirewall.ApplyBlockCalls);
        Assert.Contains("127.0.0.1 youtube.com", File.ReadAllText(hostsPath));
    }

    [Fact]
    public void ReconcileAtStartup_FirewallReapplyFails_ReturnsFalse()
    {
        var (handler, statePath, hostsPath, _) = CreateHandler();
        handler.Handle(MessageType.BlockInternet, Array.Empty<byte>());
        // Simulate a fresh Agent process with firewall that will fail.
        var reloadedState = SiteBlockState.Load(statePath);
        var freshFirewall = new FakeFirewallController { BlockShouldSucceed = false };
        var freshHandler = new SiteBlockCommandHandler(reloadedState, statePath, hostsPath, freshFirewall);

        var result = freshHandler.ReconcileAtStartup();

        Assert.False(result);
    }

    [Fact]
    public void Handle_BlockDomain_HostsFileApplyThrows_RollsBackInMemoryDomainSet()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"midview-state-{Guid.NewGuid()}.json");
        var invalidHostsPath = Path.Combine(Path.GetTempPath(), $"midview-nonexistent-dir-{Guid.NewGuid()}", "hosts.txt");
        var state = new SiteBlockState();
        var handler = new SiteBlockCommandHandler(state, statePath, invalidHostsPath, new FakeFirewallController());

        Assert.ThrowsAny<Exception>(() => handler.Handle(MessageType.BlockDomain, Encoding.UTF8.GetBytes("example.com")));

        Assert.DoesNotContain("example.com", state.BlockedDomains);
    }

    [Fact]
    public void Handle_UnblockDomain_HostsFileApplyThrows_RollsBackInMemoryDomainSet()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"midview-state-{Guid.NewGuid()}.json");
        var hostsPath = Path.Combine(Path.GetTempPath(), $"midview-hosts-{Guid.NewGuid()}.txt");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var state = new SiteBlockState();
        state.BlockedDomains.Add("example.com");
        var handler = new SiteBlockCommandHandler(state, statePath, hostsPath, new FakeFirewallController());

        // Make the hosts path a directory instead of a file, so WriteAllText throws
        File.Delete(hostsPath);
        Directory.CreateDirectory(hostsPath);

        Assert.ThrowsAny<Exception>(() => handler.Handle(MessageType.UnblockDomain, Encoding.UTF8.GetBytes("example.com")));

        Assert.Contains("example.com", state.BlockedDomains);
    }
}
