using Microsoft.Win32;
using Midview.Session;
using Xunit;

// Runs against a throwaway subtree under HKCU, never the user's real policy
// keys. The policy's paths are relative to the injected base key.
public sealed class RegistryScreenLockPolicyTests : IDisposable
{
    private const string SystemPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string ExplorerPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    private readonly string _rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
    private readonly RegistryKey _root;

    public RegistryScreenLockPolicyTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_rootPath, writable: true);
    }

    public void Dispose()
    {
        _root.Dispose();
        try { Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false); } catch { }
    }

    private RegistryScreenLockPolicy Policy() => new(Registry.CurrentUser.CreateSubKey(_rootPath, writable: true));

    private object? Read(string subPath, string name)
    {
        using var key = _root.OpenSubKey(subPath);
        return key?.GetValue(name);
    }

    [Fact]
    public void Block_DisablesTaskManagerAndSignOut()
    {
        Policy().Block();

        Assert.Equal(1, Read(SystemPath, "DisableTaskMgr"));
        Assert.Equal(1, Read(ExplorerPath, "NoLogoff"));
    }

    [Fact]
    public void Unblock_RemovesWhatBlockSet()
    {
        var policy = Policy();
        policy.Block();
        policy.Unblock();

        Assert.Null(Read(SystemPath, "DisableTaskMgr"));
        Assert.Null(Read(ExplorerPath, "NoLogoff"));
    }

    [Fact]
    public void Unblock_WithoutBlock_DoesNotThrow()
    {
        Policy().Unblock();
    }
}
