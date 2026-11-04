using Microsoft.Win32;
using Midview.Agent;
using Xunit;

// Runs against a throwaway subtree under HKCU, never the machine's real policy
// keys. The path is relative to the injected base key (HKLM\SOFTWARE in
// production).
public sealed class RegistrySwitchUserPolicyTests : IDisposable
{
    private const string SystemPath = @"Microsoft\Windows\CurrentVersion\Policies\System";

    private readonly string _rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
    private readonly RegistryKey _root;

    public RegistrySwitchUserPolicyTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_rootPath, writable: true);
    }

    public void Dispose()
    {
        _root.Dispose();
        try { Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false); } catch { }
    }

    private RegistrySwitchUserPolicy Policy() => new(Registry.CurrentUser.CreateSubKey(_rootPath, writable: true));

    private object? Read(string name)
    {
        using var key = _root.OpenSubKey(SystemPath);
        return key?.GetValue(name);
    }

    [Fact]
    public void Block_HidesSwitchUser()
    {
        Policy().Block();

        Assert.Equal(1, Read("HideFastUserSwitching"));
    }

    [Fact]
    public void Unblock_RemovesWhatBlockSet()
    {
        var policy = Policy();
        policy.Block();
        policy.Unblock();

        Assert.Null(Read("HideFastUserSwitching"));
    }

    [Fact]
    public void Unblock_WithoutBlock_DoesNotThrow()
    {
        Policy().Unblock();
    }
}
