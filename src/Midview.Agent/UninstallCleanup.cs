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
        Action<string[]> runNetsh)
    {
        var failures = new List<string>();

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
            failures.Add($"Não foi possível {description}: {ex.Message}");
        }
    }
}
