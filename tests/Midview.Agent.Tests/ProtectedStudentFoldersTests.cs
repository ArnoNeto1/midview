using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

// Against real folders under %TEMP%. The service makes SYSTEM the owner;
// here whoever runs the tests is both the owner, which is what lets each
// test put the permissions back and clean up, and the student.
public class ProtectedStudentFoldersTests : IDisposable
{
    private static readonly SecurityIdentifier Me = WindowsIdentity.GetCurrent().User!;
    private static readonly SecurityIdentifier Student = Me;

    private readonly string _desktop = Path.Combine(Path.GetTempPath(), $"midview-desktop-{Guid.NewGuid():N}");

    public ProtectedStudentFoldersTests() => Directory.CreateDirectory(_desktop);

    public void Dispose()
    {
        foreach (var directory in Directory.EnumerateDirectories(_desktop).Append(_desktop))
        {
            var security = new DirectorySecurity();
            security.AddAccessRule(new FileSystemAccessRule(Me, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(security);
        }
        Directory.Delete(_desktop, recursive: true);
    }

    private static IEnumerable<FileSystemAccessRule> RulesFor(DirectorySecurity security, SecurityIdentifier sid, bool inherited = false) =>
        security.GetAccessRules(true, inherited, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Where(rule => rule.IdentityReference.Equals(sid));

    [Fact]
    public void TheStudent_UsesTheFolder_ButCantDeleteRenameOrHideIt()
    {
        var security = ProtectedStudentFolders.FolderSecurity(Student, Me);

        Assert.True(security.AreAccessRulesProtected);
        var onTheFolder = RulesFor(security, Student).Single(rule => rule.PropagationFlags != PropagationFlags.InheritOnly);
        Assert.Equal(AccessControlType.Allow, onTheFolder.AccessControlType);
        Assert.True(onTheFolder.FileSystemRights.HasFlag(FileSystemRights.CreateFiles));
        Assert.True(onTheFolder.FileSystemRights.HasFlag(FileSystemRights.CreateDirectories));
        Assert.True(onTheFolder.FileSystemRights.HasFlag(FileSystemRights.ListDirectory));
        Assert.False(onTheFolder.FileSystemRights.HasFlag(FileSystemRights.Delete));
        Assert.False(onTheFolder.FileSystemRights.HasFlag(FileSystemRights.WriteAttributes));
        Assert.False(onTheFolder.FileSystemRights.HasFlag(FileSystemRights.ChangePermissions));

        // What they put inside stays theirs to change and delete.
        var inside = RulesFor(security, Student).Single(rule => rule.PropagationFlags == PropagationFlags.InheritOnly);
        Assert.True(inside.FileSystemRights.HasFlag(FileSystemRights.Modify));
    }

    [Fact]
    public void TheDesktop_KeepsTheStudentFromDeletingThroughIt_Once()
    {
        var desktop = new DirectorySecurity();

        ProtectedStudentFolders.DenyDeleteChild(desktop, Student);
        ProtectedStudentFolders.DenyDeleteChild(desktop, Student);

        var deny = RulesFor(desktop, Student).Single();
        Assert.Equal(AccessControlType.Deny, deny.AccessControlType);
        Assert.Equal(FileSystemRights.DeleteSubdirectoriesAndFiles, deny.FileSystemRights);
        Assert.Equal(InheritanceFlags.None, deny.InheritanceFlags);
    }

    [Fact]
    public void Protect_CreatesBothFolders_AndLocksThem()
    {
        var problems = ProtectedStudentFolders.Protect(_desktop, Student, Me);

        Assert.Empty(problems);
        foreach (var name in StudentFolderNames.All)
        {
            var folder = new DirectoryInfo(Path.Combine(_desktop, name));
            Assert.True(folder.Exists);
            var security = folder.GetAccessControl();
            Assert.True(security.AreAccessRulesProtected);
            Assert.DoesNotContain(RulesFor(security, Student), rule => rule.FileSystemRights.HasFlag(FileSystemRights.Delete)
                && rule.PropagationFlags != PropagationFlags.InheritOnly);
        }
        Assert.Contains(RulesFor(new DirectoryInfo(_desktop).GetAccessControl(), Student),
            rule => rule.AccessControlType == AccessControlType.Deny);
    }

    // What the student actually runs into. Not as an administrator, who can
    // always do it (the Administrators group has full control).
    [NotAdministratorFact]
    public void TheStudent_CantDeleteOrRenameTheFolders_ButUsesThem()
    {
        ProtectedStudentFolders.Protect(_desktop, Student, Me);
        var delivery = Path.Combine(_desktop, StudentFolderNames.Delivery);

        // Windows words it as either, depending on the call.
        Assert.True(Record.Exception(() => Directory.Delete(delivery)) is IOException or UnauthorizedAccessException);
        Assert.True(Record.Exception(() => Directory.Move(delivery, Path.Combine(_desktop, "outro nome")))
            is IOException or UnauthorizedAccessException);
        Assert.True(Directory.Exists(delivery));

        // Inside, it's the student's as before.
        var work = Path.Combine(delivery, "trabalho.txt");
        File.WriteAllText(work, "feito");
        File.Move(work, Path.Combine(delivery, "trabalho final.txt"));
        File.Delete(Path.Combine(delivery, "trabalho final.txt"));

        // And the rest of the Desktop still goes.
        var other = Path.Combine(_desktop, "rascunho.txt");
        File.WriteAllText(other, "x");
        File.Delete(other);
    }

    // A student who replaced a folder with a link to somewhere else must not
    // get the service, which runs as SYSTEM, to change permissions there.
    [Fact]
    public void Protect_LeavesALinkedFolderAlone()
    {
        var elsewhere = _desktop + "-alvo";
        Directory.CreateDirectory(elsewhere);
        try
        {
            var link = Path.Combine(_desktop, StudentFolderNames.Delivery);
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{elsewhere}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            })!.WaitForExit();
            Assert.True(Directory.Exists(link));

            var problems = ProtectedStudentFolders.Protect(_desktop, Student, Me);

            Assert.Contains(problems, problem => problem.Contains(StudentFolderNames.Delivery));
            Assert.False(new DirectoryInfo(elsewhere).GetAccessControl().AreAccessRulesProtected);
            Directory.Delete(link);
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    [Fact]
    public void NeedsProtecting_WhenAFolderIsMissing()
    {
        Assert.True(ProtectedStudentFolders.NeedsProtecting(_desktop, Me));

        ProtectedStudentFolders.Protect(_desktop, Student, Me);

        Assert.False(ProtectedStudentFolders.NeedsProtecting(_desktop, Me));
    }

    // A folder the student's own programs made again (the Session, sending a
    // file into one that was gone) is theirs, not the service's.
    [Fact]
    public void NeedsProtecting_WhenAFolderIsntTheServicesOwn()
    {
        foreach (var name in StudentFolderNames.All)
        {
            Directory.CreateDirectory(Path.Combine(_desktop, name));
        }

        Assert.True(ProtectedStudentFolders.NeedsProtecting(_desktop, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)));
        Assert.False(ProtectedStudentFolders.NeedsProtecting(_desktop, OwnerOf(Path.Combine(_desktop, StudentFolderNames.Inbox))));
    }

    // Whoever made a folder owns it: the user, or the Administrators group
    // when the tests run elevated (as in CI).
    private static SecurityIdentifier OwnerOf(string path) =>
        (SecurityIdentifier)new DirectoryInfo(path).GetAccessControl().GetOwner(typeof(SecurityIdentifier))!;

    // Protect leaves a link alone, so there's nothing for it to do there.
    [Fact]
    public void NeedsProtecting_NotForALinkedFolder()
    {
        Directory.CreateDirectory(Path.Combine(_desktop, StudentFolderNames.Inbox));
        var elsewhere = _desktop + "-alvo";
        Directory.CreateDirectory(elsewhere);
        var link = Path.Combine(_desktop, StudentFolderNames.Delivery);
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{elsewhere}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            })!.WaitForExit();
            Assert.True(Directory.Exists(link));

            Assert.False(ProtectedStudentFolders.NeedsProtecting(_desktop, OwnerOf(Path.Combine(_desktop, StudentFolderNames.Inbox))));
        }
        finally
        {
            Directory.Delete(link);
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    // Uninstalling gives the folders back: the student can delete and rename
    // them again, and the Desktop is as it was.
    [Fact]
    public void Release_UndoesTheProtection()
    {
        ProtectedStudentFolders.Protect(_desktop, Student, Me);

        var problems = ProtectedStudentFolders.Release(_desktop, Student);

        Assert.Empty(problems);
        Assert.DoesNotContain(RulesFor(new DirectoryInfo(_desktop).GetAccessControl(), Student),
            rule => rule.AccessControlType == AccessControlType.Deny);
        foreach (var name in StudentFolderNames.All)
        {
            var security = new DirectoryInfo(Path.Combine(_desktop, name)).GetAccessControl();
            Assert.False(security.AreAccessRulesProtected); // the Desktop's permissions apply again
            Assert.Contains(RulesFor(security, Student, inherited: true),
                rule => rule.AccessControlType == AccessControlType.Allow && rule.FileSystemRights.HasFlag(FileSystemRights.FullControl));
        }
    }

    // The lesson's own folder ("Pasta da turma – <turma>", U+2013), protected
    // like the other two while the lesson is on.
    private const string ClassFolder = "Pasta da turma – Turma A";

    private static IReadOnlyList<string> AllAndTheClassFolder => StudentFolderNames.All.Append(ClassFolder).ToList();

    private static bool IsDenied(string desktop, SecurityIdentifier student) =>
        RulesFor(new DirectoryInfo(desktop).GetAccessControl(), student).Any(rule => rule.AccessControlType == AccessControlType.Deny);

    [NotAdministratorFact]
    public void Protect_WithExtraName_CreatesAndProtectsIt()
    {
        var problems = ProtectedStudentFolders.Protect(_desktop, Student, Me, AllAndTheClassFolder);

        Assert.Empty(problems);
        var folder = Path.Combine(_desktop, ClassFolder);
        Assert.True(Directory.Exists(folder));
        Assert.True(new DirectoryInfo(folder).GetAccessControl().AreAccessRulesProtected);
        Assert.True(Directory.Exists(Path.Combine(_desktop, StudentFolderNames.Inbox)));
        Assert.True(Directory.Exists(Path.Combine(_desktop, StudentFolderNames.Delivery)));

        // Like the others: the student can't delete or rename it...
        Assert.True(Record.Exception(() => Directory.Delete(folder)) is IOException or UnauthorizedAccessException);
        Assert.True(Record.Exception(() => Directory.Move(folder, Path.Combine(_desktop, "outro nome")))
            is IOException or UnauthorizedAccessException);
        Assert.True(Directory.Exists(folder));

        // ...but what a browser saves into it is theirs.
        var download = Path.Combine(folder, "baixado.pdf");
        File.WriteAllText(download, "x");
        File.Delete(download);
    }

    // Explicit names are the whole list, not an addition to the default two.
    [Fact]
    public void Protect_WithOnlyTheExtraName_LeavesTheDefaultFoldersOut()
    {
        var problems = ProtectedStudentFolders.Protect(_desktop, Student, Me, new[] { ClassFolder });

        Assert.Empty(problems);
        Assert.True(Directory.Exists(Path.Combine(_desktop, ClassFolder)));
        Assert.False(Directory.Exists(Path.Combine(_desktop, StudentFolderNames.Inbox)));
        Assert.False(Directory.Exists(Path.Combine(_desktop, StudentFolderNames.Delivery)));
        Assert.True(IsDenied(_desktop, Student));
    }

    // The service joins a name onto the Desktop as SYSTEM: a name that is a
    // path must not reach, or create, anything outside it.
    [Theory]
    [InlineData(@"..\midview-escape")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("")]
    public void Protect_RefusesANameThatIsAPath(string name)
    {
        var target = Path.GetFullPath(Path.Combine(_desktop, name));
        var existed = Directory.Exists(target);
        try
        {
            var problems = ProtectedStudentFolders.Protect(_desktop, Student, Me, new[] { name });

            Assert.Contains(problems, problem => problem.StartsWith(name + ":", StringComparison.Ordinal));
            Assert.Equal(existed, Directory.Exists(target));
            Assert.Empty(Directory.EnumerateDirectories(_desktop));
            Assert.False(ProtectedStudentFolders.NeedsProtecting(_desktop, Me, new[] { name }));
        }
        finally
        {
            if (!existed && Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
        }
    }

    [Fact]
    public void NeedsProtecting_WithExtraName_WhenItIsMissing()
    {
        ProtectedStudentFolders.Protect(_desktop, Student, Me);
        Assert.False(ProtectedStudentFolders.NeedsProtecting(_desktop, Me));

        Assert.True(ProtectedStudentFolders.NeedsProtecting(_desktop, Me, AllAndTheClassFolder));

        ProtectedStudentFolders.Protect(_desktop, Student, Me, AllAndTheClassFolder);

        Assert.False(ProtectedStudentFolders.NeedsProtecting(_desktop, Me, AllAndTheClassFolder));
    }

    // When the lesson ends only its folder is given back: the other two still
    // need the Desktop's Deny.
    [Fact]
    public void Release_WithOneName_KeepsTheDesktopDeny()
    {
        ProtectedStudentFolders.Protect(_desktop, Student, Me, AllAndTheClassFolder);

        var problems = ProtectedStudentFolders.Release(_desktop, Student, new[] { ClassFolder });

        Assert.Empty(problems);
        Assert.True(IsDenied(_desktop, Student));
        var released = new DirectoryInfo(Path.Combine(_desktop, ClassFolder)).GetAccessControl();
        Assert.False(released.AreAccessRulesProtected);
        foreach (var name in StudentFolderNames.All)
        {
            Assert.True(new DirectoryInfo(Path.Combine(_desktop, name)).GetAccessControl().AreAccessRulesProtected);
        }
    }
}
