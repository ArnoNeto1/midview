using System.Security.AccessControl;
using System.Security.Principal;

namespace Midview.Agent;

public static class AgentDataDirectory
{
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Midview");

    // Applied on every Agent start, not only when the directory is first
    // created, so a directory that already existed (made by hand during
    // setup, or by an older version) still ends up locked down.
    //
    // Users get read-only access: Session runs as the logged-in student and
    // must read trusted-masters.json and session-identity.pfx from here --
    // with SYSTEM/Administrators-only access it couldn't, and crashed on
    // every launch. Only writing is restricted, since tampering with the
    // trust list is the actual risk (thumbprints aren't secret).
    public static string EnsureSecure()
    {
        var directoryInfo = Directory.CreateDirectory(DirectoryPath);

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(InheritedRule(WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl));
        security.AddAccessRule(InheritedRule(WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl));
        security.AddAccessRule(InheritedRule(WellKnownSidType.BuiltinUsersSid, FileSystemRights.ReadAndExecute));
        directoryInfo.SetAccessControl(security);

        return DirectoryPath;
    }

    // For files students must not read even though the directory lets them,
    // i.e. the Agent's own private key.
    public static void RestrictToAdministrators(string filePath)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(filePath).SetAccessControl(security);
    }

    private static FileSystemAccessRule InheritedRule(WellKnownSidType sid, FileSystemRights rights) => new(
        new SecurityIdentifier(sid, null),
        rights,
        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
        PropagationFlags.None,
        AccessControlType.Allow);
}
