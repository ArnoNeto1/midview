using System.Security.AccessControl;
using System.Security.Principal;

namespace Midview.Agent;

public static class AgentDataDirectory
{
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Midview");

    // Applied on every Agent start, not only when the directory is first
    // created: machines that ran an older Agent still have it locked to
    // SYSTEM/Administrators only, which stops MidviewSession -- running as
    // the logged-in student -- from reading trusted-masters.json and
    // session-identity.pfx. Users get read-only access: only tampering
    // with the trust list is a concern here, not anyone reading it. The one
    // real secret, the Agent's own key, gets RestrictToAdministrators.
    public static string EnsureSecure()
    {
        var directoryInfo = Directory.CreateDirectory(DirectoryPath);

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(InheritableRule(WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl));
        security.AddAccessRule(InheritableRule(WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl));
        security.AddAccessRule(InheritableRule(WellKnownSidType.BuiltinUsersSid, FileSystemRights.ReadAndExecute));
        directoryInfo.SetAccessControl(security);

        return DirectoryPath;
    }

    // Cuts one file off from the directory's inherited Users read access.
    public static void RestrictToAdministrators(string filePath)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(filePath).SetAccessControl(security);
    }

    private static FileSystemAccessRule InheritableRule(WellKnownSidType sidType, FileSystemRights rights) =>
        new(new SecurityIdentifier(sidType, null), rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow);
}
