using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Midview.Protocol;

namespace Midview.Agent;

// Keeps the student from deleting or renaming "Arquivos do professor" and
// "Entregar ao professor" on their Desktop (asked for during the first live
// run). Done by the Agent, as SYSTEM, every time it starts Session for a
// student: anything Session did, running as the student, the student could
// undo.
//
// - Each folder is created if missing, owned by SYSTEM, with its own
//   permissions (not the Desktop's): the student may list it and create,
//   change and delete what's inside, but not delete, rename or hide the
//   folder itself, nor change its permissions.
// - The student's own Desktop gets one Deny for "delete subfolders and
//   files": that right on a parent lets anyone delete or rename what's in it
//   whatever its own permissions say. Their other Desktop items still go,
//   through the delete right each one has itself.
//
// The service runs as SYSTEM on folders the student controls, so it never
// follows a link: each folder is opened as itself (a junction or symbolic
// link is refused, not followed) and its permissions are set through that
// open handle, so what was checked is what gets changed. Inside the folders
// nothing is touched. An administrator, which a student account shouldn't
// be, can always undo all of it.
public static class ProtectedStudentFolders
{
    // The rights on the folder itself: everything to use it, nothing to
    // delete, rename, hide it or change its permissions.
    private const FileSystemRights UseTheFolder =
        FileSystemRights.ListDirectory | FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories
        | FileSystemRights.ReadExtendedAttributes | FileSystemRights.Traverse
        | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ReadAttributes
        | FileSystemRights.ReadPermissions | FileSystemRights.Synchronize;

    public static DirectorySecurity FolderSecurity(SecurityIdentifier student, SecurityIdentifier owner)
    {
        var security = new DirectorySecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var everywhere = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid, null), FileSystemRights.FullControl, everywhere, PropagationFlags.None, AccessControlType.Allow));
        }
        security.AddAccessRule(new FileSystemAccessRule(
            student, UseTheFolder, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            student, FileSystemRights.Modify, everywhere, PropagationFlags.InheritOnly, AccessControlType.Allow));
        return security;
    }

    public static void DenyDeleteChild(DirectorySecurity desktop, SecurityIdentifier student)
    {
        if (!HasDenyDeleteChild(desktop, student))
        {
            desktop.AddAccessRule(DenyDeleteChildRule(student));
        }
    }

    // Returns what couldn't be protected, for the Agent's log. names are the
    // folders to protect (default: the two Midview always keeps); a lesson's
    // class folder is protected the same way by naming it too. desktopPath is
    // where the Desktop really is (StudentDesktopCheck.Verify): the Desktop and
    // each folder are checked to be there still before anything is changed.
    public static IReadOnlyList<string> Protect(
        string desktopPath, SecurityIdentifier student, SecurityIdentifier owner, IReadOnlyList<string>? names = null)
    {
        var problems = new List<string>();
        string desktopFinal;
        try
        {
            using var desktop = OpenDesktop(desktopPath, ReadControl | WriteDac, out desktopFinal);
            var security = ReadSecurity(desktop);
            if (!HasDenyDeleteChild(security, student))
            {
                DenyDeleteChild(security, student);
                WriteSecurity(desktop, security, DaclSecurityInformation);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Without it, the folders could still be deleted through the Desktop.
            problems.Add($"Área de Trabalho ({desktopPath}): {ErrorText.Describe(ex)}");
            return problems;
        }

        foreach (var name in names ?? StudentFolderNames.All)
        {
            if (!IsPlainName(name))
            {
                problems.Add($"{name}: nome inválido, precisa ser só o nome de uma pasta");
                continue;
            }
            var path = Path.Combine(desktopFinal, name);
            try
            {
                if (!Directory.Exists(path) && !File.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                var folderSecurity = FolderSecurity(student, owner);
                SafeFileHandle folder;
                var information = OwnerSecurityInformation | DaclSecurityInformation | ProtectedDaclSecurityInformation;
                try
                {
                    folder = OpenAsItself(path, ReadControl | WriteDac | WriteOwner);
                }
                catch (UnauthorizedAccessException)
                {
                    // Already owned by someone else (SYSTEM, from an earlier
                    // start): the permissions are what still needs setting.
                    folder = OpenAsItself(path, ReadControl | WriteDac);
                    information &= ~OwnerSecurityInformation;
                }
                using (folder)
                {
                    CheckIsIn(folder, path);
                    WriteSecurity(folder, folderSecurity, information);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                problems.Add($"{name}: {ErrorText.Describe(ex)}");
            }
        }
        return problems;
    }

    // Whether Protect has something to do: a folder missing, or one not owned
    // by the owner Protect gives them (one the student's own programs made
    // again). A link, or a file by that name, is left alone, as Protect
    // leaves it, and so is a name Protect refuses. Only reads.
    public static bool NeedsProtecting(string desktopPath, SecurityIdentifier owner, IReadOnlyList<string>? names = null)
    {
        foreach (var name in names ?? StudentFolderNames.All)
        {
            if (!IsPlainName(name))
            {
                continue;
            }
            var path = Path.Combine(desktopPath, name);
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                return true;
            }
            try
            {
                using var folder = OpenAsItself(path, ReadControl);
                if (!owner.Equals(ReadSecurity(folder).GetOwner(typeof(SecurityIdentifier))))
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
            }
        }
        return false;
    }

    // For uninstalling: the Desktop's Deny goes, and the folders become
    // ordinary ones the student can delete or rename again (their content
    // is the student's, so nothing is deleted). With names given, only those
    // folders are given back (a lesson's class folder, when the lesson ends)
    // and the Desktop's Deny stays: the other folders still need it. As with
    // Protect, nothing is given back through a Desktop that isn't (or is no
    // longer) where desktopPath says.
    public static IReadOnlyList<string> Release(
        string desktopPath, SecurityIdentifier student, IReadOnlyList<string>? names = null)
    {
        var problems = new List<string>();
        string desktopFinal;
        try
        {
            using var desktop = OpenDesktop(desktopPath, names is null ? ReadControl | WriteDac : ReadControl, out desktopFinal);
            if (names is null)
            {
                var security = ReadSecurity(desktop);
                if (HasDenyDeleteChild(security, student))
                {
                    security.RemoveAccessRuleSpecific(DenyDeleteChildRule(student));
                    WriteSecurity(desktop, security, DaclSecurityInformation);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            problems.Add($"Área de Trabalho ({desktopPath}): {ErrorText.Describe(ex)}");
            return problems;
        }

        foreach (var name in names ?? StudentFolderNames.All)
        {
            if (!IsPlainName(name))
            {
                continue;
            }
            var path = Path.Combine(desktopFinal, name);
            if (!Directory.Exists(path))
            {
                continue;
            }
            try
            {
                var security = new DirectorySecurity();
                var everywhere = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                foreach (var sid in new[] { student, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                             new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
                {
                    security.AddAccessRule(new FileSystemAccessRule(
                        sid, FileSystemRights.FullControl, everywhere, PropagationFlags.None, AccessControlType.Allow));
                }
                using var folder = OpenAsItself(path, ReadControl | WriteDac);
                CheckIsIn(folder, path);
                WriteSecurity(folder, security, DaclSecurityInformation | UnprotectedDaclSecurityInformation);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                problems.Add($"{name}: {ErrorText.Describe(ex)}");
            }
        }
        return problems;
    }

    // Every user account's Desktop on this machine, for the uninstaller,
    // which runs with no student logged in: each profile in the registry
    // (real accounts only, S-1-5-21-...) and its Desktop folder, where it
    // really is, if that is in the profile (StudentDesktopCheck). A Desktop
    // moved elsewhere (OneDrive, say) isn't found this way.
    public static IEnumerable<(string Desktop, SecurityIdentifier Student)> AllStudentDesktops()
    {
        using var profiles = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        foreach (var sid in profiles?.GetSubKeyNames() ?? Array.Empty<string>())
        {
            if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal))
            {
                continue;
            }
            using var profile = profiles!.OpenSubKey(sid);
            if (profile?.GetValue("ProfileImagePath") is string profilePath)
            {
                var desktop = Path.Combine(Environment.ExpandEnvironmentVariables(profilePath), "Desktop");
                var student = new SecurityIdentifier(sid);
                if (Directory.Exists(desktop)
                    && StudentDesktopCheck.Verify(desktop, student, StudentDesktopCheck.ProfileImagePathOf, out _) is { } verified)
                {
                    yield return (verified, student);
                }
            }
        }
    }

    // True only when path is a real directory and not a reparse point
    // (junction, symbolic link, mount point): opened as itself, never
    // following a link. For a privileged action that walks a student-writable
    // path -- so a link the student planted can't redirect it elsewhere.
    // Never throws (a missing path, a link, or access denied all give false).
    public static bool IsRealDirectory(string path)
    {
        try
        {
            using var handle = OpenAsItself(path, ReadControl);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    // A folder name is one path segment, never a path: it is joined onto the
    // student's Desktop by a service running as SYSTEM, so a name carrying
    // "..\" or a drive must not reach anywhere else. Windows drops trailing
    // dots and spaces from a path's last segment, so "...", ". " and ".. "
    // would come out as the Desktop itself or its parent: a name must not
    // end in either (nor be blank).
    private static bool IsPlainName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name == name.TrimEnd('.', ' ')
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static FileSystemAccessRule DenyDeleteChildRule(SecurityIdentifier student) => new(
        student, FileSystemRights.DeleteSubdirectoriesAndFiles, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);

    private static bool HasDenyDeleteChild(DirectorySecurity security, SecurityIdentifier student) =>
        security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Any(rule =>
            rule.AccessControlType == AccessControlType.Deny && rule.IdentityReference.Equals(student)
            && rule.InheritanceFlags == InheritanceFlags.None
            && rule.FileSystemRights.HasFlag(FileSystemRights.DeleteSubdirectoriesAndFiles));

    // The Desktop as itself (OpenAsItself), and only where desktopPath says it is:
    // a link put on the way since the path was checked leads somewhere else, and
    // is refused. finalPath: where it is, for its folders to be checked against.
    private static SafeFileHandle OpenDesktop(string desktopPath, uint access, out string finalPath)
    {
        var handle = OpenAsItself(desktopPath, access);
        try
        {
            finalPath = StudentDesktopCheck.FinalPath(handle);
            if (!StudentDesktopCheck.SamePath(finalPath, desktopPath))
            {
                throw new InvalidOperationException(NotWhereItWas);
            }
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    // An open folder is the one at path (in the Desktop OpenDesktop checked), not one a
    // link swapped in on the way since.
    private static void CheckIsIn(SafeFileHandle folder, string path)
    {
        if (!StudentDesktopCheck.SamePath(StudentDesktopCheck.FinalPath(folder), path))
        {
            throw new InvalidOperationException(NotWhereItWas);
        }
    }

    private const string NotWhereItWas = "não está onde deveria (há um atalho ou vínculo no caminho); deixada como está";

    // The folder itself, never what a link points at: a reparse point
    // (junction, symbolic link, mount point) is refused.
    internal static SafeFileHandle OpenAsItself(string path, uint access)
    {
        var handle = CreateFile(path, access, FileShareAll, IntPtr.Zero, OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw error == 5
                ? new UnauthorizedAccessException($"acesso negado a {path}")
                : new IOException($"{path} não pôde ser aberta (erro {error})", unchecked((int)0x80070000) | error);
        }
        if (!GetFileInformationByHandle(handle, out var information)
            || (information.FileAttributes & FileAttributeDirectory) == 0
            || (information.FileAttributes & FileAttributeReparsePoint) != 0)
        {
            handle.Dispose();
            throw new InvalidOperationException("é um atalho ou vínculo para outro lugar, não uma pasta; deixada como está");
        }
        return handle;
    }

    // The owner of an open folder (opened with ReadControl).
    internal static SecurityIdentifier? OwnerOf(SafeFileHandle handle) =>
        ReadSecurity(handle).GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;

    private static DirectorySecurity ReadSecurity(SafeFileHandle handle)
    {
        GetKernelObjectSecurity(handle, OwnerSecurityInformation | DaclSecurityInformation, null, 0, out var needed);
        var descriptor = new byte[needed];
        if (!GetKernelObjectSecurity(handle, OwnerSecurityInformation | DaclSecurityInformation, descriptor, needed, out _))
        {
            throw new IOException($"as permissões não puderam ser lidas (erro {Marshal.GetLastWin32Error()})");
        }
        var security = new DirectorySecurity();
        security.SetSecurityDescriptorBinaryForm(descriptor);
        return security;
    }

    private static void WriteSecurity(SafeFileHandle handle, DirectorySecurity security, uint information)
    {
        if (!SetKernelObjectSecurity(handle, information, security.GetSecurityDescriptorBinaryForm()))
        {
            var error = Marshal.GetLastWin32Error();
            throw error == 5
                ? new UnauthorizedAccessException("acesso negado ao mudar as permissões")
                : new IOException($"as permissões não puderam ser gravadas (erro {error})", unchecked((int)0x80070000) | error);
        }
    }

    internal const uint ReadControl = 0x00020000;
    private const uint WriteDac = 0x00040000;
    private const uint WriteOwner = 0x00080000;
    private const uint FileShareAll = 0x7;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeReparsePoint = 0x400;
    private const uint OwnerSecurityInformation = 0x1;
    private const uint DaclSecurityInformation = 0x4;
    private const uint ProtectedDaclSecurityInformation = 0x80000000;
    private const uint UnprotectedDaclSecurityInformation = 0x20000000;

    [StructLayout(LayoutKind.Sequential, Pack = 4)] // FILETIMEs are two DWORDs, 4-byte aligned
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint access, uint share, IntPtr securityAttributes, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetKernelObjectSecurity(
        SafeFileHandle handle, uint requested, byte[]? descriptor, uint length, out uint needed);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetKernelObjectSecurity(SafeFileHandle handle, uint information, byte[] descriptor);
}
