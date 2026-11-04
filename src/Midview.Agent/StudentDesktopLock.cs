using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Midview.Protocol;

namespace Midview.Agent;

// "Travar a Área de Trabalho" (lab panel, Bloqueios extras): the student can't
// create, delete or rename anything on their own Desktop, nor change its
// permissions to undo that. Done by the Agent, as SYSTEM, with NTFS Denies
// for the student alone:
//
// - On the Desktop itself, "create files" and "create folders": nothing new
//   lands there (new, paste, drag, save, nor the new name of a rename), next
//   to ProtectedStudentFolders' "delete subfolders and files", made sure of
//   here and left in place by Unlock.
// - On each item directly on it, "delete": not deleted, not sent to the
//   Recycle Bin, not renamed nor moved away.
// - On both, "change permissions" and "take ownership", and where the student
//   is the owner an OWNER RIGHTS entry that leaves them only reading the
//   permissions: an owner can otherwise always change them, Denies or not.
//
// Inside the folders nothing changes: what is in them stays the student's to
// add, rename and delete, and the files on the Desktop still open and save in
// place. Each item gets its own Denies, none inherited from the Desktop, so
// Unlock takes back exactly what Lock put, item by item. What appears later
// (Midview's own folders, something an administrator put there) is locked by
// the next Lock. A lesson's class folder is left out: Midview protects it
// during the lesson and the student's Session removes it once it is over.
//
// As in ProtectedStudentFolders, the service never follows a link: the
// Desktop and each item are opened as themselves (a link on the Desktop gets
// the Denies on the link, never on what it points at), checked to be where
// they should be, and changed through that handle. A file with another name
// elsewhere (a hard link) shares its permissions with that other place, so it
// is left as it is. An administrator, which a student account shouldn't be,
// can always undo all of it.
public static class StudentDesktopLock
{
    // How many objects Lock or Unlock wrote the permissions of (the Desktop
    // and its items), and what it couldn't do, for the Agent's log and the
    // teacher's panel.
    public sealed record Result(int Changed, IReadOnlyList<string> Problems);

    private const FileSystemRights NothingNew = FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories;
    private const FileSystemRights NoPermissionChanges = FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    // OWNER RIGHTS: where the permissions name it, the owner gets what it is
    // given there instead of the right to read and change the permissions
    // that an owner otherwise always has.
    private static readonly SecurityIdentifier OwnerRights = new("S-1-3-4");

    // guardPermissions: also keep the student from changing the permissions,
    // as the service always does. Without it (tests, run as the account they
    // lock, which must clean up after) only the Denies on creating, deleting
    // and renaming are put.
    public static Result Lock(string desktopPath, SecurityIdentifier student, bool guardPermissions = true)
    {
        var problems = new List<string>();
        var changed = 0;
        string desktopFinal;
        try
        {
            using var desktop = ProtectedStudentFolders.OpenDesktop(
                desktopPath, ProtectedStudentFolders.ReadControl | ProtectedStudentFolders.WriteDac, out desktopFinal);
            var security = (DirectorySecurity)Read(desktop, isDirectory: true);
            if (!IsDesktopLocked(security, student, guardPermissions))
            {
                ProtectedStudentFolders.DenyDeleteChild(security, student);
                security.AddAccessRule(Deny(student, NothingNew | (guardPermissions ? NoPermissionChanges : 0)));
                RestrictTheOwner(security, student, guardPermissions);
                Write(desktop, security);
                changed++;
            }
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            // Without it, nothing on the Desktop is locked: each item's Deny
            // alone leaves it free to rename through the Desktop.
            problems.Add($"Área de Trabalho ({desktopPath}): {ErrorText.Describe(ex)}");
            return new Result(changed, problems);
        }

        foreach (var name in ItemNames(desktopFinal, problems))
        {
            var path = Path.Combine(desktopFinal, name);
            try
            {
                using var item = ProtectedStudentFolders.OpenEntryAsItself(
                    path, ProtectedStudentFolders.ReadControl | ProtectedStudentFolders.WriteDac, out var isDirectory, out var links);
                ProtectedStudentFolders.CheckIsIn(item, path);
                if (isDirectory && IsClassFolder(name))
                {
                    continue;
                }
                if (!isDirectory && links > 1)
                {
                    problems.Add($"{name}: tem outro nome em outro lugar do disco (vínculo); deixado como está");
                    continue;
                }

                var security = Read(item, isDirectory);
                if (!IsItemLocked(security, student, guardPermissions))
                {
                    security.AddAccessRule(Deny(student, FileSystemRights.Delete | (guardPermissions ? NoPermissionChanges : 0)));
                    RestrictTheOwner(security, student, guardPermissions);
                    Write(item, security);
                    changed++;
                }
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                problems.Add($"{name}: {ErrorText.Describe(ex)}");
            }
        }
        return new Result(changed, problems);
    }

    // Takes back what Lock put, items first: the Desktop's "delete subfolders
    // and files" stays, as ProtectedStudentFolders' folders still need it.
    // Writes only what has something of Lock's.
    public static Result Unlock(string desktopPath, SecurityIdentifier student)
    {
        var problems = new List<string>();
        var changed = 0;
        try
        {
            using var desktop = ProtectedStudentFolders.OpenDesktop(
                desktopPath, ProtectedStudentFolders.ReadControl | ProtectedStudentFolders.WriteDac, out var desktopFinal);

            foreach (var name in ItemNames(desktopFinal, problems))
            {
                var path = Path.Combine(desktopFinal, name);
                try
                {
                    using var item = ProtectedStudentFolders.OpenEntryAsItself(
                        path, ProtectedStudentFolders.ReadControl | ProtectedStudentFolders.WriteDac, out var isDirectory, out var links);
                    ProtectedStudentFolders.CheckIsIn(item, path);
                    if (!isDirectory && links > 1)
                    {
                        continue; // never locked
                    }
                    var security = Read(item, isDirectory);
                    if (TakeBack(security, student, FileSystemRights.Delete | NoPermissionChanges))
                    {
                        Write(item, security);
                        changed++;
                    }
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    problems.Add($"{name}: {ErrorText.Describe(ex)}");
                }
            }

            var desktopSecurity = Read(desktop, isDirectory: true);
            if (TakeBack(desktopSecurity, student, NothingNew | NoPermissionChanges))
            {
                Write(desktop, desktopSecurity);
                changed++;
            }
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            problems.Add($"Área de Trabalho ({desktopPath}): {ErrorText.Describe(ex)}");
        }
        return new Result(changed, problems);
    }

    // Whether the Desktop has Lock's Deny on creating: only its own
    // permissions are read, none of its items'. False when it can't be read.
    public static bool IsAnyLocked(string desktopPath, SecurityIdentifier student)
    {
        try
        {
            using var desktop = ProtectedStudentFolders.OpenDesktop(desktopPath, ProtectedStudentFolders.ReadControl, out _);
            return (DeniedTo(Read(desktop, isDirectory: true), student) & NothingNew) != 0;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return false;
        }
    }

    private static bool IsDesktopLocked(FileSystemSecurity security, SecurityIdentifier student, bool guardPermissions) =>
        HasAll(security, student, NothingNew | FileSystemRights.DeleteSubdirectoriesAndFiles, guardPermissions);

    private static bool IsItemLocked(FileSystemSecurity security, SecurityIdentifier student, bool guardPermissions) =>
        HasAll(security, student, FileSystemRights.Delete, guardPermissions);

    private static bool HasAll(FileSystemSecurity security, SecurityIdentifier student, FileSystemRights denied, bool guardPermissions)
    {
        var needed = denied | (guardPermissions ? NoPermissionChanges : 0);
        return (DeniedTo(security, student) & needed) == needed
            && (!guardPermissions || !IsOwnedBy(security, student) || HasOwnerRights(security));
    }

    // Lock's Denies (rights) and OWNER RIGHTS entry off security; false when
    // it had none of them.
    private static bool TakeBack(FileSystemSecurity security, SecurityIdentifier student, FileSystemRights rights)
    {
        if ((DeniedTo(security, student) & rights) == 0 && !HasOwnerRights(security))
        {
            return false;
        }
        security.RemoveAccessRule(Deny(student, rights));
        security.RemoveAccessRule(OwnerReadsOnly());
        return true;
    }

    private static void RestrictTheOwner(FileSystemSecurity security, SecurityIdentifier student, bool guardPermissions)
    {
        // Only where the student is the owner: for anyone else (SYSTEM, an
        // administrator) it would take away their own way of changing them.
        if (guardPermissions && IsOwnedBy(security, student) && !HasOwnerRights(security))
        {
            security.AddAccessRule(OwnerReadsOnly());
        }
    }

    // The rights the student is denied on the object itself (explicit, not
    // inherited, not passed down to what is inside).
    private static FileSystemRights DeniedTo(FileSystemSecurity security, SecurityIdentifier student) =>
        security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Deny && rule.IdentityReference.Equals(student)
                && rule.InheritanceFlags == InheritanceFlags.None)
            .Aggregate((FileSystemRights)0, (rights, rule) => rights | rule.FileSystemRights);

    private static bool HasOwnerRights(FileSystemSecurity security) =>
        security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Any(rule => rule.AccessControlType == AccessControlType.Allow && rule.IdentityReference.Equals(OwnerRights)
                && rule.InheritanceFlags == InheritanceFlags.None
                && rule.FileSystemRights.HasFlag(FileSystemRights.ReadPermissions));

    private static bool IsOwnedBy(FileSystemSecurity security, SecurityIdentifier student) =>
        student.Equals(security.GetOwner(typeof(SecurityIdentifier)));

    private static FileSystemAccessRule Deny(SecurityIdentifier student, FileSystemRights rights) =>
        new(student, rights, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);

    private static FileSystemAccessRule OwnerReadsOnly() =>
        new(OwnerRights, FileSystemRights.ReadPermissions, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow);

    private static bool IsClassFolder(string name) =>
        name.StartsWith(ClassLessonHandler.FolderPrefix, StringComparison.OrdinalIgnoreCase);

    // The names directly on the Desktop, hidden ones included; none when it
    // can't be listed (a problem).
    private static List<string> ItemNames(string desktop, List<string> problems)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(desktop).Select(entry => Path.GetFileName(entry)).ToList();
        }
        catch (Exception ex) when (IsFileError(ex) || ex is System.Security.SecurityException)
        {
            problems.Add($"Área de Trabalho ({desktop}): {ErrorText.Describe(ex)}");
            return new List<string>();
        }
    }

    private static FileSystemSecurity Read(SafeFileHandle handle, bool isDirectory)
    {
        FileSystemSecurity security = isDirectory ? new DirectorySecurity() : new FileSecurity();
        ProtectedStudentFolders.ReadSecurityInto(handle, security);
        return security;
    }

    // The permissions only (not the owner), protected from inheriting as they
    // were.
    private static void Write(SafeFileHandle handle, FileSystemSecurity security) =>
        ProtectedStudentFolders.WriteSecurity(handle, security, ProtectedStudentFolders.DaclSecurityInformation
            | (security.AreAccessRulesProtected ? ProtectedStudentFolders.ProtectedDaclSecurityInformation : 0));

    private static bool IsFileError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidOperationException;
}
