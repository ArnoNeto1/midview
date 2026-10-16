using System.Text.Json;
using Midview.Protocol;

namespace Midview.Agent;

// The two things the installer scripts (installer/) need from the Agent
// itself. Run from an administrator console with the service stopped, never
// by the service.
public static class InstallCommands
{
    public const string PrepareInstallArgument = "--prepare-install";
    public const string UninstallCleanupArgument = "--uninstall-cleanup";

    // Locks the data directory down and creates both identities, exactly as
    // the service's first start would, so the installer then writes
    // trusted-masters.json into an already-protected directory and records
    // the machine's thumbprints for the teacher. Prints them as JSON.
    public static int RunPrepareInstall()
    {
        try
        {
            AgentDataDirectory.EnsureSecure();
            string agentThumbprint;
            using (var certificate = AgentIdentity.LoadOrCreate(AgentIdentity.DefaultCertificatePath))
            {
                agentThumbprint = certificate.Thumbprint;
            }
            AgentDataDirectory.RestrictToAdministrators(AgentIdentity.DefaultCertificatePath);
            var sessionThumbprint = SessionIdentity.EnsureExists(SessionIdentity.DefaultCertificatePath);

            Console.WriteLine(IdentityJson(agentThumbprint, sessionThumbprint));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Não foi possível preparar a máquina: {ErrorText.Describe(ex)}");
            return 1;
        }
    }

    public static string IdentityJson(string agentThumbprint, string sessionThumbprint) =>
        JsonSerializer.Serialize(new { agentThumbprint, sessionThumbprint });

    public static int RunUninstallCleanup()
    {
        var failures = UninstallCleanup.Run(
            AgentServerWorker.SiteBlockStatePath, AgentServerWorker.HostsFilePath,
            new NetshFirewallController(AgentServerWorker.AgentPort, AgentServerWorker.SessionPort),
            new WindowsInputDeviceController(), args => AgentServerWorker.RunNetsh(args),
            AppBlockCommandHandler.DefaultStatePath, ProtectedStudentFolders.AllStudentDesktops(),
            PendingUninstalls.DefaultPath);
        foreach (var failure in failures)
        {
            Console.Error.WriteLine(failure);
        }
        return failures.Count == 0 ? 0 : 1;
    }
}
