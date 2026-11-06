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

    // A student account's Policies keys are read-only to the student (Windows
    // sets them so): opening them to write throws SecurityException. The lab's
    // Session called Unblock as it started and crashed on it, again and again,
    // so no screen ever reached the teacher. Neither may ever throw.
    [Fact]
    public void Unblock_OnPolicyKeysTheStudentCanOnlyRead_DoesNotThrow()
    {
        ReadOnlyPolicyKeys(withValues: true);

        Policy().Unblock();

        Assert.Equal(1, Read(SystemPath, "DisableTaskMgr")); // left as it was, not deleted
    }

    [Fact]
    public void Block_OnPolicyKeysTheStudentCanOnlyRead_DoesNotThrow()
    {
        ReadOnlyPolicyKeys(withValues: false);

        Policy().Block();

        Assert.Null(Read(SystemPath, "DisableTaskMgr"));
    }

    // Both policy keys, with the current user denied every write to them (but
    // not their deletion, so Dispose still cleans up), as Windows leaves them
    // for a standard account.
    private void ReadOnlyPolicyKeys(bool withValues)
    {
        foreach (var (path, name) in new[] { (SystemPath, "DisableTaskMgr"), (ExplorerPath, "NoLogoff") })
        {
            using var key = _root.CreateSubKey(path, writable: true);
            if (withValues)
            {
                key.SetValue(name, 1, RegistryValueKind.DWord);
            }
            var security = key.GetAccessControl();
            security.AddAccessRule(new System.Security.AccessControl.RegistryAccessRule(
                System.Security.Principal.WindowsIdentity.GetCurrent().User!,
                System.Security.AccessControl.RegistryRights.SetValue | System.Security.AccessControl.RegistryRights.CreateSubKey,
                System.Security.AccessControl.AccessControlType.Deny));
            key.SetAccessControl(security);
        }
    }
}
