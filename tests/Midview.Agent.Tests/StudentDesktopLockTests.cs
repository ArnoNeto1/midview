using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Midview.Agent;
using Xunit;

// Against real folders under %TEMP%, like ProtectedStudentFoldersTests:
// whoever runs the tests is the student. Most tests lock without
// guardPermissions, which would keep this same account from cleaning up
// after itself; the one test that checks it holds handles opened before the
// lock and puts the permissions back through them.
public class StudentDesktopLockTests : IDisposable
{
    private static readonly SecurityIdentifier Me = WindowsIdentity.GetCurrent().User!;

    private readonly string _desktop = Path.Combine(Path.GetTempPath(), $"midview-desktop-{Guid.NewGuid():N}");
    private readonly string _file;
    private readonly string _folder;

    public StudentDesktopLockTests()
    {
        Directory.CreateDirectory(_desktop);
        _file = Path.Combine(_desktop, "trabalho.txt");
        File.WriteAllText(_file, "x");
        _folder = Path.Combine(_desktop, "Minha pasta");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        StudentDesktopLock.Unlock(_desktop, Me);
        foreach (var directory in Directory.EnumerateDirectories(_desktop).Append(_desktop))
        {
            var security = new DirectorySecurity();
            security.AddAccessRule(new FileSystemAccessRule(Me, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(security);
        }
        Directory.Delete(_desktop, recursive: true);
    }

    private static bool Refused(Action action) => Record.Exception(action) is IOException or UnauthorizedAccessException;

    private static IEnumerable<FileSystemAccessRule> DeniesFor(FileSystemSecurity security, SecurityIdentifier sid) =>
        security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Where(rule => rule.IdentityReference.Equals(sid) && rule.AccessControlType == AccessControlType.Deny);

    private static void MakeLink(string switchName, string link, string target) =>
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink {switchName} \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!.WaitForExit();

    [Fact]
    public void Lock_NothingNewLandsOnTheDesktop()
    {
        var result = StudentDesktopLock.Lock(_desktop, Me, guardPermissions: false);

        Assert.Empty(result.Problems);
        Assert.True(Refused(() => File.WriteAllText(Path.Combine(_desktop, "novo.txt"), "x")));
        Assert.True(Refused(() => Directory.CreateDirectory(Path.Combine(_desktop, "Nova pasta"))));
        Assert.Equal(2, Directory.EnumerateFileSystemEntries(_desktop).Count());
    }

    [Fact]
    public void Lock_WhatIsThere_CantBeDeletedOrRenamed()
    {
        StudentDesktopLock.Lock(_desktop, Me, guardPermissions: false);

        Assert.True(Refused(() => File.Delete(_file)));
        Assert.True(Refused(() => File.Move(_file, Path.Combine(_desktop, "outro nome.txt"))));
        Assert.True(Refused(() => Directory.Delete(_folder)));
        Assert.True(Refused(() => Directory.Move(_folder, Path.Combine(_desktop, "Outra pasta"))));
        Assert.True(Refused(() => Directory.Move(_folder, Path.Combine(Path.GetTempPath(), $"midview-fora-{Guid.NewGuid():N}"))));
        Assert.True(File.Exists(_file));
        Assert.True(Directory.Exists(_folder));
    }

    [Fact]
    public void Lock_InsideTheFolders_EverythingStaysTheStudents()
    {
        StudentDesktopLock.Lock(_desktop, Me, guardPermissions: false);

        var work = Path.Combine(_folder, "trabalho.txt");
        File.WriteAllText(work, "feito");
        File.Move(work, Path.Combine(_folder, "trabalho final.txt"));
        Directory.CreateDirectory(Path.Combine(_folder, "Fotos"));
        Directory.Move(Path.Combine(_folder, "Fotos"), Path.Combine(_folder, "Imagens"));
        Directory.Delete(Path.Combine(_folder, "Imagens"));
        File.Delete(Path.Combine(_folder, "trabalho final.txt"));
    }

    [Fact]
    public void Lock_TheFilesStillOpenAndSaveInPlace()
    {
        StudentDesktopLock.Lock(_desktop, Me, guardPermissions: false);

        File.AppendAllText(_file, "y");

        Assert.Equal("xy", File.ReadAllText(_file));
    }

    // A lesson's class folder is Midview's to remove when the lesson is over
    // (the student's Session moves its files aside and recycles it), so it
    // gets no Deny of its own; it still can't be renamed, as nothing on the
    // Desktop can get a new name there.
    [Fact]
    public void Lock_LeavesAClassFolderForMidviewToRemove()
    {
        var classFolder = Path.Combine(_desktop, "Pasta da turma – 7A");
        Directory.CreateDirectory(classFolder);

        StudentDesktopLock.Lock(_desktop, Me, guardPermissions: false);

        Assert.True(Refused(() => Directory.Move(classFolder, Path.Combine(_desktop, "Pasta da turma – 7B"))));
        Directory.Delete(classFolder);
    }

    [Fact]
    public void Lock_Twice_ChangesNothingTheSecondTime()
    {
        var first = StudentDesktopLock.Lock(_desktop, Me, guardPermissions: false);
        var second = StudentDesktopLock.Lock(_desktop, Me, guardPermissions: false);

        Assert.Equal(3, first.Changed); // the Desktop and its two items
        Assert.Equal(0, second.Changed);
        Assert.Empty(second.Problems);
        Assert.Single(DeniesFor(new FileInfo(_file).GetAccessControl(), Me));
    }

    // Something that appears on the Desktop after the lock (one of Midview's
    // own folders, or what an administrator put there) is locked by the next
    // one, and only it.
    [Fact]
    public void Lock_Again_LocksWhatAppearedSince()
    {
        StudentDesktopLock.Lock(_desktop, Me, guardPermissions: false);
        var later = Path.Combine(_desktop, "depois.txt");
        var security = new DirectoryInfo(_desktop).GetAccessControl();
        security.RemoveAccessRule(new FileSystemAccessRule(Me, FileSystemRights.CreateFiles, AccessControlType.Deny));
        new DirectoryInfo(_desktop).SetAccessControl(security);
        File.WriteAllText(later, "x");

        var result = StudentDesktopLock.Lock(_desktop, Me, guardPermissions: false);

        Assert.Equal(2, result.Changed); // the Desktop's Deny back, and the new file
        Assert.True(Refused(() => File.Delete(later)));
    }

    [Fact]
    public void Unlock_GivesEverythingBack()
    {
        StudentDesktopLock.Lock(_desktop, Me, guardPermissions: false);

        var result = StudentDesktopLock.Unlock(_desktop, Me);

        Assert.Empty(result.Problems);
        Assert.Equal(3, result.Changed);
        Assert.False(StudentDesktopLock.IsAnyLocked(_desktop, Me));
        File.Move(_file, Path.Combine(_desktop, "renomeado.txt"));
        File.Delete(Path.Combine(_desktop, "renomeado.txt"));
        Directory.Move(_folder, Path.Combine(_desktop, "Outra pasta"));
        Directory.Delete(Path.Combine(_desktop, "Outra pasta"));
        File.WriteAllText(Path.Combine(_desktop, "novo.txt"), "x");
        Directory.CreateDirectory(Path.Combine(_desktop, "Nova pasta"));
    }

    // "Arquivos do professor" and "Entregar ao professor" rely on the Desktop's
    // Deny of "delete subfolders and files" (ProtectedStudentFolders): unlocking
    // the Desktop leaves it there.
    [Fact]
    public void Unlock_KeepsWhatProtectsMidviewsFolders()
    {
        ProtectedStudentFolders.Protect(_desktop, Me, Me);
        StudentDesktopLock.Lock(_desktop, Me, guardPermissions: false);
        foreach (var name in Midview.Protocol.StudentFolderNames.All)
        {
            // Still not inheriting the Desktop's permissions.
            Assert.True(new DirectoryInfo(Path.Combine(_desktop, name)).GetAccessControl().AreAccessRulesProtected);
        }

        StudentDesktopLock.Unlock(_desktop, Me);

        var deny = Assert.Single(DeniesFor(new DirectoryInfo(_desktop).GetAccessControl(), Me));
        Assert.Equal(FileSystemRights.DeleteSubdirectoriesAndFiles, deny.FileSystemRights);
        Assert.Empty(ProtectedStudentFolders.Release(_desktop, Me));
    }

    [Fact]
    public void Unlock_OfADesktopNeverLocked_ChangesNothing()
    {
        var result = StudentDesktopLock.Unlock(_desktop, Me);

        Assert.Equal(0, result.Changed);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void IsAnyLocked_FollowsLockAndUnlock()
    {
        Assert.False(StudentDesktopLock.IsAnyLocked(_desktop, Me));

        StudentDesktopLock.Lock(_desktop, Me, guardPermissions: false);
        Assert.True(StudentDesktopLock.IsAnyLocked(_desktop, Me));

        StudentDesktopLock.Unlock(_desktop, Me);
        Assert.False(StudentDesktopLock.IsAnyLocked(_desktop, Me));
    }

    // The service runs as SYSTEM on a Desktop the student controls: a link
    // there gets the Deny on the link itself, never on what it points at.
    [Fact]
    public void Lock_ALinkOnTheDesktop_IsLockedItself_NotWhatItPointsAt()
    {
        var elsewhere = _desktop + "-alvo";
        Directory.CreateDirectory(elsewhere);
        try
        {
            var link = Path.Combine(_desktop, "Atalho");
            MakeLink("/J", link, elsewhere);
            Assert.True(Directory.Exists(link));

            var result = StudentDesktopLock.Lock(_desktop, Me, guardPermissions: false);

            Assert.Empty(result.Problems);
            Assert.Empty(DeniesFor(new DirectoryInfo(elsewhere).GetAccessControl(), Me));
            Assert.True(Refused(() => Directory.Delete(link)));
            StudentDesktopLock.Unlock(_desktop, Me);
            Directory.Delete(link);
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    // A file with another name somewhere else shares its permissions with that
    // other place: changing them would change that file too.
    [Fact]
    public void Lock_AFileWithAnotherNameElsewhere_IsLeftAsItIs()
    {
        var elsewhere = _desktop + "-original.txt";
        File.WriteAllText(elsewhere, "x");
        try
        {
            var link = Path.Combine(_desktop, "vinculo.txt");
            MakeLink("/H", link, elsewhere);
            Assert.True(File.Exists(link));

            var result = StudentDesktopLock.Lock(_desktop, Me, guardPermissions: false);

            Assert.Contains(result.Problems, problem => problem.Contains("vinculo.txt"));
            Assert.Empty(DeniesFor(new FileInfo(elsewhere).GetAccessControl(), Me));
            StudentDesktopLock.Unlock(_desktop, Me);
            File.Delete(link);
        }
        finally
        {
            File.Delete(elsewhere);
        }
    }

    [Fact]
    public void Lock_ADesktopThatIsntThere_IsAProblem()
    {
        var result = StudentDesktopLock.Lock(_desktop + "-nao-existe", Me, guardPermissions: false);

        Assert.Equal(0, result.Changed);
        Assert.Contains("Área de Trabalho", Assert.Single(result.Problems));
    }

    // As the service locks it: the student, owner of the Desktop and of what
    // they put on it, can't take the Denies off through the Security tab.
    // Not as an administrator, whose own rights aren't what is checked here.
    [NotAdministratorFact]
    public void Lock_TheStudentCantChangeThePermissionsBack()
    {
        // Opened before the lock, with the right to change permissions: an
        // open handle keeps the rights it was opened with, so the test can
        // put everything back through them.
        var paths = new[] { _desktop, _file, _folder };
        var handles = paths.Select(path => ProtectedStudentFolders.OpenEntryAsItself(
            path, ProtectedStudentFolders.ReadControl | ProtectedStudentFolders.WriteDac, out _, out _)).ToList();
        try
        {
            var result = StudentDesktopLock.Lock(_desktop, Me);

            Assert.Empty(result.Problems);
            Assert.True(StudentDesktopLock.IsAnyLocked(_desktop, Me));
            // What the Security tab would write: full control, no Denies.
            var fullControl = new FileSystemAccessRule(Me, FileSystemRights.FullControl, AccessControlType.Allow);
            var directorySecurity = new DirectorySecurity();
            directorySecurity.AddAccessRule(fullControl);
            var fileSecurity = new FileSecurity();
            fileSecurity.AddAccessRule(fullControl);
            Assert.True(Refused(() => new DirectoryInfo(_desktop).SetAccessControl(directorySecurity)));
            Assert.True(Refused(() => new FileInfo(_file).SetAccessControl(fileSecurity)));
            Assert.True(Refused(() => new DirectoryInfo(_folder).SetAccessControl(directorySecurity)));
            Assert.True(StudentDesktopLock.IsAnyLocked(_desktop, Me));
            Assert.True(Refused(() => File.Delete(_file)));
            // Reading them still works.
            new DirectoryInfo(_desktop).GetAccessControl();
            new FileInfo(_file).GetAccessControl();
        }
        finally
        {
            foreach (var handle in handles)
            {
                using (handle)
                {
                    PutBack(handle);
                }
            }
        }
    }

    // Full control for the test's account, inherited as before.
    private static void PutBack(SafeFileHandle handle)
    {
        var security = new DirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(Me, FileSystemRights.FullControl, AccessControlType.Allow));
        ProtectedStudentFolders.WriteSecurity(handle, security,
            ProtectedStudentFolders.DaclSecurityInformation | ProtectedStudentFolders.UnprotectedDaclSecurityInformation);
    }
}
