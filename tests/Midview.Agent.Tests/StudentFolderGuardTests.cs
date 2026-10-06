using System.Security.AccessControl;
using System.Security.Principal;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

// Against real folders under %TEMP%, like ProtectedStudentFoldersTests:
// whoever runs the tests is both the student and the owner.
public class StudentFolderGuardTests : IDisposable
{
    private static readonly SecurityIdentifier Me = WindowsIdentity.GetCurrent().User!;

    private readonly string _desktop = Path.Combine(Path.GetTempPath(), $"midview-desktop-{Guid.NewGuid():N}");
    private readonly List<string> _log = new();

    public StudentFolderGuardTests() => Directory.CreateDirectory(_desktop);

    public void Dispose()
    {
        MakeDeletable();
        Directory.Delete(_desktop, recursive: true);
    }

    // Undoes the protection, as an administrator deleting the folders would get past it.
    private void MakeDeletable()
    {
        foreach (var directory in Directory.EnumerateDirectories(_desktop).Append(_desktop))
        {
            var security = new DirectorySecurity();
            security.AddAccessRule(new FileSystemAccessRule(Me, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(security);
        }
    }

    private StudentFolderGuard Guard(string desktop) => new(() => (desktop, Me), Me, _log.Add);

    [Fact]
    public void Check_WithNobodyLoggedOn_DoesNothing()
    {
        new StudentFolderGuard(() => null, Me, _log.Add).Check();

        Assert.Empty(Directory.EnumerateFileSystemEntries(_desktop));
        Assert.Empty(_log);
    }

    [Fact]
    public void Check_PutsTheFoldersBack_Protected()
    {
        Guard(_desktop).Check();

        foreach (var name in StudentFolderNames.All)
        {
            Assert.True(new DirectoryInfo(Path.Combine(_desktop, name)).GetAccessControl().AreAccessRulesProtected);
        }
        Assert.False(ProtectedStudentFolders.NeedsProtecting(_desktop, Me));
        Assert.Contains(_desktop, Assert.Single(_log));
    }

    [Fact]
    public void Check_WithNothingToDo_StaysQuiet()
    {
        var guard = Guard(_desktop);

        guard.Check();
        guard.Check();
        guard.Check();

        Assert.Single(_log);
    }

    [Fact]
    public void Check_PutsAFolderBackEachTimeItGoes()
    {
        var guard = Guard(_desktop);
        guard.Check();
        guard.Check(); // a poll with nothing to do, before someone deletes it again
        var inbox = Path.Combine(_desktop, StudentFolderNames.Inbox);
        MakeDeletable();
        Directory.Delete(inbox);

        guard.Check();

        Assert.True(Directory.Exists(inbox));
        Assert.Equal(2, _log.Count);
    }

    [Fact]
    public void Check_AProblemThatLasts_IsLoggedOnce()
    {
        var guard = Guard(_desktop + "-nao-existe");

        guard.Check();
        guard.Check();

        Assert.Contains("couldn't", Assert.Single(_log));
    }

    [Fact]
    public void Check_NeverThrows()
    {
        var guard = new StudentFolderGuard(() => throw new InvalidOperationException("sem token"), Me, _log.Add);

        guard.Check();
        guard.Check();

        Assert.Contains("sem token", Assert.Single(_log));
    }
}
