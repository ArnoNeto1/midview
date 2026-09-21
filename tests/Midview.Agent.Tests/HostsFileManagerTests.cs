using Midview.Agent;
using Xunit;

public class HostsFileManagerTests
{
    [Fact]
    public void Reconcile_NoDomains_RemovesExistingBlockAndAddsNothing()
    {
        var existing = "127.0.0.1 localhost\n# BEGIN MIDVIEW SITE BLOCK\n127.0.0.1 old.com\n# END MIDVIEW SITE BLOCK\n";

        var result = HostsFileManager.Reconcile(existing, Array.Empty<string>());

        Assert.Equal("127.0.0.1 localhost\n", result);
    }

    [Fact]
    public void Reconcile_OneDomain_ExpandsToBareAndWww()
    {
        var result = HostsFileManager.Reconcile("127.0.0.1 localhost\n", new[] { "youtube.com" });

        Assert.Contains("127.0.0.1 youtube.com", result);
        Assert.Contains("127.0.0.1 www.youtube.com", result);
    }

    [Fact]
    public void Reconcile_WwwDomainGiven_AlsoBlocksBareForm()
    {
        var result = HostsFileManager.Reconcile("127.0.0.1 localhost\n", new[] { "www.youtube.com" });

        Assert.Contains("127.0.0.1 youtube.com", result);
        Assert.Contains("127.0.0.1 www.youtube.com", result);
    }

    [Fact]
    public void Reconcile_PreservesLinesOutsideManagedBlock()
    {
        var existing = "127.0.0.1 localhost\n192.168.1.1 router.local\n";

        var result = HostsFileManager.Reconcile(existing, new[] { "youtube.com" });

        Assert.Contains("127.0.0.1 localhost", result);
        Assert.Contains("192.168.1.1 router.local", result);
    }

    [Fact]
    public void Reconcile_CalledTwiceWithDifferentSets_ReplacesOldBlockEntirely()
    {
        var afterFirst = HostsFileManager.Reconcile("127.0.0.1 localhost\n", new[] { "old.com" });
        var afterSecond = HostsFileManager.Reconcile(afterFirst, new[] { "new.com" });

        Assert.DoesNotContain("old.com", afterSecond);
        Assert.Contains("new.com", afterSecond);
    }
}
