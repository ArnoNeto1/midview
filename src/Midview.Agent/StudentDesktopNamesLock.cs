using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using Midview.Protocol;

namespace Midview.Agent;

// The names of Windows' own Desktop icons (Lixeira, Este Computador, Rede, the
// student's folder) while "Travar a Área de Trabalho" is on. They are not files,
// so StudentDesktopLock's permissions don't reach them: a new name for one is
// the default value of its key in the student's own registry,
// Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{...} (checked on a
// Windows 11 PC: renaming the Recycle Bin writes exactly that). The Agent, as
// SYSTEM, denies the student writing values on that key or deleting it, so a
// rename fails with "access denied". Only the key itself: its DefaultIcon
// subkey, where Explorer switches the Recycle Bin's full and empty icons,
// stays the student's. Changing the key's permissions back is denied too, with
// the OWNER RIGHTS entry where the student owns the key, as on the Desktop.
//
// The student's Software key is handed in (their hive, through HKEY_USERS),
// so tests use a throwaway subtree.
public static class StudentDesktopNamesLock
{
    private const string ClsidPath = @"Microsoft\Windows\CurrentVersion\Explorer\CLSID\";

    // Recycle Bin, This PC, Network, the user's own folder.
    internal static readonly string[] Icons =
    {
        "{645FF040-5081-101B-9F08-00AA002F954E}",
        "{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
        "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}",
        "{59031A47-3F72-44A7-89C5-5595FE6B30EE}",
    };

    private const RegistryRights NoRenaming = RegistryRights.SetValue | RegistryRights.Delete;
    private const RegistryRights NoPermissionChanges = RegistryRights.ChangePermissions | RegistryRights.TakeOwnership;
    private const RegistryRights Opened =
        RegistryRights.ReadKey | RegistryRights.ChangePermissions;

    private static readonly SecurityIdentifier OwnerRights = new("S-1-3-4");

    internal static string KeyPath(string icon) => ClsidPath + icon;

    // guardPermissions: also keep the student from changing the permissions
    // back, as the service always does; tests, run as the account they lock,
    // leave it off so they can clean up.
    public static StudentDesktopLock.Result Lock(RegistryKey software, SecurityIdentifier student, bool guardPermissions = true)
    {
        var problems = new List<string>();
        var changed = 0;
        foreach (var icon in Icons)
        {
            try
            {
                software.CreateSubKey(KeyPath(icon), writable: false)?.Dispose();
                using var key = software.OpenSubKey(KeyPath(icon), RegistryKeyPermissionCheck.ReadWriteSubTree, Opened);
                if (key is null)
                {
                    continue;
                }
                var security = key.GetAccessControl();
                var needed = NoRenaming | (guardPermissions ? NoPermissionChanges : 0);
                var ownedByStudent = student.Equals(security.GetOwner(typeof(SecurityIdentifier)));
                if ((DeniedTo(security, student) & needed) == needed
                    && (!guardPermissions || !ownedByStudent || HasOwnerRights(security)))
                {
                    continue;
                }
                security.AddAccessRule(Deny(student, needed));
                if (guardPermissions && ownedByStudent && !HasOwnerRights(security))
                {
                    security.AddAccessRule(OwnerReadsOnly());
                }
                key.SetAccessControl(security);
                changed++;
            }
            catch (Exception ex) when (IsRegistryError(ex))
            {
                problems.Add($"{icon}: {ErrorText.Describe(ex)}");
            }
        }
        return new StudentDesktopLock.Result(changed, problems);
    }

    // Takes back what Lock put; keys without it are only read.
    public static StudentDesktopLock.Result Unlock(RegistryKey software, SecurityIdentifier student)
    {
        var problems = new List<string>();
        var changed = 0;
        foreach (var icon in Icons)
        {
            try
            {
                using var key = software.OpenSubKey(KeyPath(icon), RegistryKeyPermissionCheck.ReadWriteSubTree, Opened);
                if (key is null)
                {
                    continue;
                }
                var security = key.GetAccessControl();
                if ((DeniedTo(security, student) & NoRenaming) == 0 && !HasOwnerRights(security))
                {
                    continue;
                }
                security.RemoveAccessRule(Deny(student, NoRenaming | NoPermissionChanges));
                security.RemoveAccessRule(OwnerReadsOnly());
                key.SetAccessControl(security);
                changed++;
            }
            catch (Exception ex) when (IsRegistryError(ex))
            {
                problems.Add($"{icon}: {ErrorText.Describe(ex)}");
            }
        }
        return new StudentDesktopLock.Result(changed, problems);
    }

    // Whether any of the keys has Lock's Deny; false when none can be read.
    public static bool IsAnyLocked(RegistryKey software, SecurityIdentifier student)
    {
        foreach (var icon in Icons)
        {
            try
            {
                using var key = software.OpenSubKey(KeyPath(icon), RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadPermissions);
                if (key is not null && (DeniedTo(key.GetAccessControl(), student) & NoRenaming) != 0)
                {
                    return true;
                }
            }
            catch (Exception ex) when (IsRegistryError(ex))
            {
                // Unreadable: counts as not locked; Unlock reports it if it matters.
            }
        }
        return false;
    }

    private static RegistryRights DeniedTo(RegistrySecurity security, SecurityIdentifier student) =>
        security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<RegistryAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Deny && rule.IdentityReference.Equals(student)
                && rule.InheritanceFlags == InheritanceFlags.None)
            .Aggregate((RegistryRights)0, (rights, rule) => rights | rule.RegistryRights);

    private static bool HasOwnerRights(RegistrySecurity security) =>
        security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<RegistryAccessRule>()
            .Any(rule => rule.AccessControlType == AccessControlType.Allow && rule.IdentityReference.Equals(OwnerRights)
                && rule.InheritanceFlags == InheritanceFlags.None);

    private static RegistryAccessRule Deny(SecurityIdentifier student, RegistryRights rights) =>
        new(student, rights, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);

    private static RegistryAccessRule OwnerReadsOnly() =>
        new(OwnerRights, RegistryRights.ReadPermissions, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow);

    private static bool IsRegistryError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException;
}
