using System.Security.Principal;
using Midview.Protocol;

namespace Midview.Agent;

// What the uninstaller runs (Midview.Agent.exe --uninstall-cleanup, with the
// service already stopped) so that removing Midview never leaves a machine
// the way a lesson left it: internet blocked, sites blocked, keyboard and
// mouse disabled, or Midview's firewall openings behind. Each step runs even
// when an earlier one failed; every failure is reported.
public static class UninstallCleanup
{
    public static IReadOnlyList<string> Run(
        string statePath, string hostsFilePath, IFirewallController firewall, IInputDeviceController devices,
        Action<string[]> runNetsh, string? appBlockStatePath = null,
        IEnumerable<(string Desktop, SecurityIdentifier Student)>? studentDesktops = null,
        string? pendingUninstallsPath = null)
    {
        var failures = new List<string>();

        // The students' "Arquivos do professor" and "Entregar ao professor"
        // can be deleted and renamed again (ProtectedStudentFolders).
        foreach (var (desktop, student) in studentDesktops ?? Array.Empty<(string, SecurityIdentifier)>())
        {
            foreach (var problem in ProtectedStudentFolders.Release(desktop, student))
            {
                failures.Add($"Não foi possível devolver a pasta ao aluno em {desktop}: {problem}");
            }
        }

        Step(failures, "reativar teclado e mouse", () =>
        {
            if (!devices.EnableInputDevices())
            {
                throw new InvalidOperationException("algum dispositivo não pôde ser reativado");
            }
        });
        Step(failures, "desbloquear os sites bloqueados", () => HostsFileManager.ApplyToFile(hostsFilePath, Array.Empty<string>()));
        Step(failures, "desbloquear a internet", firewall.ApplyUnblock);
        Step(failures, "remover as regras de firewall do Midview", () =>
        {
            foreach (var ruleName in AgentServerWorker.InboundRuleNames)
            {
                runNetsh(new[] { "advfirewall", "firewall", "delete", "rule", $"name={ruleName}" });
            }
        });

        // A later reinstall must not bring back a block from before.
        Step(failures, "limpar o estado de bloqueio salvo", () => new SiteBlockState().Save(statePath));
        if (appBlockStatePath is not null)
        {
            Step(failures, "desfazer o bloqueio de programas", () =>
            {
                File.Delete(appBlockStatePath);
                File.Delete(appBlockStatePath + ".tmp");
            });
        }
        // Nor the programs a teacher once asked to remove, from the students
        // who hadn't logged on since (PendingUninstalls).
        if (pendingUninstallsPath is not null)
        {
            Step(failures, "esquecer os programas a desinstalar", () =>
            {
                File.Delete(pendingUninstallsPath);
                File.Delete(pendingUninstallsPath + ".tmp");
            });
        }

        return failures;
    }

    private static void Step(List<string> failures, string description, Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            failures.Add($"Não foi possível {description}: {ErrorText.Describe(ex)}");
        }
    }
}
