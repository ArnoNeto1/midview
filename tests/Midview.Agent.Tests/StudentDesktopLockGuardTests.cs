using System.Security.AccessControl;
using System.Security.Principal;
using Midview.Agent;
using Xunit;

// Against a real folder under %TEMP%, like StudentDesktopLockTests: whoever
// runs the tests is the student, and the guard locks without guarding the
// permissions, so the test can clean up after.
public class StudentDesktopLockGuardTests : IDisposable
{
    private static readonly SecurityIdentifier Me = WindowsIdentity.GetCurrent().User!;

    private readonly string _desktop = Path.Combine(Path.GetTempPath(), $"midview-desktop-{Guid.NewGuid():N}");
    private readonly List<string> _log = new();

    public StudentDesktopLockGuardTests()
    {
        Directory.CreateDirectory(_desktop);
        File.WriteAllText(Path.Combine(_desktop, "trabalho.txt"), "x");
    }

    public void Dispose()
    {
        StudentDesktopLock.Unlock(_desktop, Me);
        Directory.Delete(_desktop, recursive: true);
    }

    private StudentDesktopLockGuard Guard(string desktop) => new(() => (desktop, Me), _log.Add, guardPermissions: false);

    [Fact]
    public void Check_WithNobodyLoggedOn_DoesNothing()
    {
        var problems = new StudentDesktopLockGuard(() => null, _log.Add, guardPermissions: false).Check(locked: true);

        Assert.Empty(problems);
        Assert.Empty(_log);
    }

    [Fact]
    public void Check_WithARefusedDesktop_DoesNothing()
    {
        var problems = new StudentDesktopLockGuard(() => (null, Me), _log.Add, guardPermissions: false).Check(locked: true);

        Assert.Empty(problems);
        Assert.Empty(_log);
    }

    [Fact]
    public void Check_Locked_LocksTheDesktop_AndSaysSoOnce()
    {
        var guard = Guard(_desktop);

        Assert.Empty(guard.Check(locked: true));
        guard.Check(locked: true);
        guard.Check(locked: true);

        Assert.True(StudentDesktopLock.IsAnyLocked(_desktop, Me));
        Assert.Contains(_desktop, Assert.Single(_log));
    }

    [Fact]
    public void Check_Unlocked_GivesTheDesktopBack()
    {
        var guard = Guard(_desktop);
        guard.Check(locked: true);

        Assert.Empty(guard.Check(locked: false));

        Assert.False(StudentDesktopLock.IsAnyLocked(_desktop, Me));
        Assert.Equal(2, _log.Count);
        File.Delete(Path.Combine(_desktop, "trabalho.txt"));
    }

    // The common case, every poll on every machine: nothing locked, nothing to
    // do, and the Desktop isn't touched.
    [Fact]
    public void Check_Unlocked_OnADesktopNeverLocked_TouchesNothing()
    {
        var before = new DirectoryInfo(_desktop).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);

        Guard(_desktop).Check(locked: false);

        Assert.Equal(before, new DirectoryInfo(_desktop).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));
        Assert.Empty(_log);
    }

    [Fact]
    public void Check_AProblemThatLasts_IsLoggedOnce()
    {
        var guard = Guard(_desktop + "-nao-existe");

        Assert.NotEmpty(guard.Check(locked: true));
        guard.Check(locked: true);

        Assert.Contains("couldn't", Assert.Single(_log));
    }

    [Fact]
    public void Check_NeverThrows()
    {
        var guard = new StudentDesktopLockGuard(() => throw new InvalidOperationException("sem token"), _log.Add);

        Assert.NotEmpty(guard.Check(locked: true));
        guard.Check(locked: true);

        Assert.Contains("sem token", Assert.Single(_log));
    }
}
