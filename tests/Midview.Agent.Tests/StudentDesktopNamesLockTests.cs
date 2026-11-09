using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using Midview.Agent;
using Xunit;

// The student's Software key is a throwaway HKCU subtree, and whoever runs the
// tests is the student: locked without guardPermissions, so this same account
// can still clean up after itself.
public sealed class StudentDesktopNamesLockTests : IDisposable
{
    private const string RecycleBin = "{645FF040-5081-101B-9F08-00AA002F954E}";
    private static readonly SecurityIdentifier Me = WindowsIdentity.GetCurrent().User!;

    private readonly string _rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
    private readonly RegistryKey _software;

    public StudentDesktopNamesLockTests()
    {
        _software = Registry.CurrentUser.CreateSubKey(_rootPath, writable: true);
    }

    public void Dispose()
    {
        StudentDesktopNamesLock.Unlock(_software, Me);
        _software.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false);
    }

    private string KeyPath(string icon) => $@"{_rootPath}\{StudentDesktopNamesLock.KeyPath(icon)}";

    // What Explorer does to rename the Recycle Bin: the key's default value.
    private bool CanRename(string icon)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath(icon), writable: true);
            key!.SetValue("", "Lixo");
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    [Fact]
    public void Lock_TheRecycleBinAndThisPc_CantBeRenamed()
    {
        var result = StudentDesktopNamesLock.Lock(_software, Me, guardPermissions: false);

        Assert.Empty(result.Problems);
        Assert.All(StudentDesktopNamesLock.Icons, icon => Assert.False(CanRename(icon)));
    }

    // Explorer switches the Recycle Bin's full and empty icons there.
    [Fact]
    public void Lock_TheRecycleBinIconStillChanges()
    {
        using (var icon = Registry.CurrentUser.CreateSubKey($@"{KeyPath(RecycleBin)}\DefaultIcon", writable: true))
        {
            icon.SetValue("empty", "imageres.dll,-55");
        }
        StudentDesktopNamesLock.Lock(_software, Me, guardPermissions: false);

        using var defaultIcon = Registry.CurrentUser.OpenSubKey($@"{KeyPath(RecycleBin)}\DefaultIcon", writable: true);
        defaultIcon!.SetValue("", "imageres.dll,-54");

        Assert.Equal("imageres.dll,-54", defaultIcon.GetValue(""));
    }

    // A name the Recycle Bin already had stays as it is.
    [Fact]
    public void Lock_KeepsANameThatWasThere()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath(RecycleBin), writable: true))
        {
            key.SetValue("", "Lixeira da escola");
        }

        StudentDesktopNamesLock.Lock(_software, Me, guardPermissions: false);

        using var locked = Registry.CurrentUser.OpenSubKey(KeyPath(RecycleBin));
        Assert.Equal("Lixeira da escola", locked!.GetValue(""));
    }

    [Fact]
    public void Unlock_GivesRenamingBack()
    {
        StudentDesktopNamesLock.Lock(_software, Me, guardPermissions: false);

        var result = StudentDesktopNamesLock.Unlock(_software, Me);

        Assert.Empty(result.Problems);
        Assert.Equal(StudentDesktopNamesLock.Icons.Length, result.Changed);
        Assert.All(StudentDesktopNamesLock.Icons, icon => Assert.True(CanRename(icon)));
    }

    [Fact]
    public void Lock_Twice_ChangesNothingTheSecondTime()
    {
        StudentDesktopNamesLock.Lock(_software, Me, guardPermissions: false);

        Assert.Equal(0, StudentDesktopNamesLock.Lock(_software, Me, guardPermissions: false).Changed);
    }

    [Fact]
    public void IsAnyLocked_FollowsLockAndUnlock()
    {
        Assert.False(StudentDesktopNamesLock.IsAnyLocked(_software, Me));

        StudentDesktopNamesLock.Lock(_software, Me, guardPermissions: false);
        Assert.True(StudentDesktopNamesLock.IsAnyLocked(_software, Me));

        StudentDesktopNamesLock.Unlock(_software, Me);
        Assert.False(StudentDesktopNamesLock.IsAnyLocked(_software, Me));
    }

    [Fact]
    public void Unlock_WhereNothingWasLocked_ChangesNothing()
    {
        Assert.Equal(0, StudentDesktopNamesLock.Unlock(_software, Me).Changed);
    }

    // Through the guard, as the Agent's poll runs it: the names lock follows
    // "Travar a Área de Trabalho" even with no Desktop the Agent may work on.
    [Fact]
    public void Guard_LocksAndUnlocksTheNames()
    {
        var guard = new StudentDesktopLockGuard(
            () => (null, Me), _ => { }, guardPermissions: false,
            openSoftware: _ => Registry.CurrentUser.OpenSubKey(_rootPath, writable: true));

        Assert.Empty(guard.Check(locked: true));
        Assert.False(CanRename(RecycleBin));

        Assert.Empty(guard.Check(locked: false));
        Assert.True(CanRename(RecycleBin));
    }

    // Denies only on the key itself: nothing inherited down to DefaultIcon.
    [Fact]
    public void Lock_DeniesNothingBelowTheKey()
    {
        StudentDesktopNamesLock.Lock(_software, Me, guardPermissions: false);

        using var key = Registry.CurrentUser.OpenSubKey(KeyPath(RecycleBin));
        var inherited = key!.GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<RegistryAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Deny && rule.InheritanceFlags != InheritanceFlags.None);
        Assert.Empty(inherited);
    }
}
