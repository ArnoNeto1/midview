using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Midview.Protocol;

namespace Midview.Master;

// The lab-configuration panel ("Painel do laboratório"): the browser and
// Windows lockdowns applied to one machine or the whole class through each
// machine's Agent (SetLabConfig / ResetLabConfig / ClearChromeProfiles on
// port 28900), plus the distraction-site block through the site-blocking the
// Agent already has. Each lockdown button reads the machine's current
// configuration, changes the one function, and sends it back, so the other
// functions are left as they were.
public partial class LabConfigWindow : Window
{
    private readonly IReadOnlyList<KnownMachine> _targets;
    private readonly X509Certificate2 _masterCertificate;

    public LabConfigWindow(IReadOnlyList<KnownMachine> targets, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        TargetsTextBlock.Text = targets.Count == 1
            ? $"Em: {targets[0].Name}"
            : $"Na turma toda ({targets.Count} máquinas)";
    }

    private async void OnActivateClicked(object sender, RoutedEventArgs e) => await ToggleAsync(FlagOf(sender), on: true);

    private async void OnDeactivateClicked(object sender, RoutedEventArgs e) => await ToggleAsync(FlagOf(sender), on: false);

    private static string FlagOf(object sender) => (string)((Button)sender).Tag;

    // Read the machine's current configuration, change the one function, send
    // it back. A fresh SavedAt (always newer than what the machine has) so the
    // change always wins.
    private async Task ToggleAsync(string flag, bool on)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await RunAsync(machine =>
        {
            var status = LabConfigClient.GetStatus(machine, _masterCertificate);
            if (status.OldAgent)
            {
                return (false, "Midview antigo nesta máquina: atualize-a para usar o painel");
            }
            var updated = LabConfigChange.With(status.Config!, flag, on)
                with { SavedAt = LabConfigChange.NextSavedAt(now, status.Config!.SavedAt) };
            return LabConfigClient.Apply(machine, _masterCertificate, updated);
        });
    }

    private async void OnResetClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(machine => LabConfigClient.Reset(machine, _masterCertificate));

    private async void OnClearChromeProfilesClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(machine => LabConfigClient.ClearChromeProfiles(machine, _masterCertificate));

    private async void OnClearDownloadsTodayClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(machine => LabConfigClient.ClearDownloads(machine, _masterCertificate, DownloadsScope.Today));

    private async void OnClearDownloadsAllClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(machine => LabConfigClient.ClearDownloads(machine, _masterCertificate, DownloadsScope.All));

    private async void OnBlockDistractionClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(machine => LabConfigClient.SetDistractionSites(machine, _masterCertificate, block: true));

    private async void OnUnblockDistractionClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(machine => LabConfigClient.SetDistractionSites(machine, _masterCertificate, block: false));

    private async void OnShowAppliedClicked(object sender, RoutedEventArgs e)
    {
        IsEnabled = false;
        ResultTextBlock.Text = string.Empty;
        ProgressTextBlock.Text = "Lendo o que está aplicado...";

        var lines = await Task.WhenAll(_targets.Select(machine => Task.Run(() =>
        {
            if (string.IsNullOrEmpty(machine.AgentThumbprint))
            {
                return $"• {machine.Name}: pareie-a de novo em \"Máquinas da sala\"";
            }
            try
            {
                var status = LabConfigClient.GetStatus(machine, _masterCertificate);
                return status.OldAgent
                    ? $"• {machine.Name}: Midview antigo (atualize-a)"
                    : $"• {machine.Name}: {Describe(status.Config!)}";
            }
            catch (Exception ex)
            {
                return $"• {machine.Name}: {FailureText.DescribeAgent(ex)}";
            }
        })));

        ProgressTextBlock.Text = "O que está aplicado agora:";
        ResultTextBlock.Text = string.Join("\n", lines);
        IsEnabled = true;
    }

    // Runs one command on every target at once and shows a per-machine result.
    private async Task RunAsync(Func<KnownMachine, (bool Succeeded, string Detail)> command)
    {
        IsEnabled = false;
        ResultTextBlock.Text = string.Empty;
        ProgressTextBlock.Text = "Enviando...";

        var results = await Task.WhenAll(_targets.Select(machine => Task.Run(() => RunOne(machine, command))));

        var failures = results.Count(result => !result.Succeeded);
        ProgressTextBlock.Text = failures == 0
            ? results.Length == 1 ? "Feito." : $"Feito nas {results.Length} máquinas."
            : $"Feito em {results.Length - failures} de {results.Length}.";
        ResultTextBlock.Text = string.Join("\n", results.Select(result =>
            $"{(result.Succeeded ? "✓" : "✗")} {result.Name}: {result.Detail}"));
        IsEnabled = true;
    }

    private (string Name, bool Succeeded, string Detail) RunOne(
        KnownMachine machine, Func<KnownMachine, (bool Succeeded, string Detail)> command)
    {
        if (string.IsNullOrEmpty(machine.AgentThumbprint))
        {
            return (machine.Name, false,
                "a lista da sala não tem o certificado do Agent desta máquina: pareie-a de novo em \"Máquinas da sala\"");
        }
        try
        {
            var (succeeded, detail) = command(machine);
            // An Agent from before this feature doesn't know ResetLabConfig /
            // ClearChromeProfiles and answers a bare CommandAck, which decodes
            // to (false, ""); say so plainly instead of an empty line.
            if (!succeeded && string.IsNullOrEmpty(detail))
            {
                return (machine.Name, false, "Midview antigo nesta máquina: atualize-a para usar o painel");
            }
            return (machine.Name, succeeded, detail);
        }
        catch (Exception ex)
        {
            return (machine.Name, false, FailureText.DescribeAgent(ex));
        }
    }

    private static string Describe(LabConfig config)
    {
        if (!config.IsAnyOn)
        {
            return "nada aplicado";
        }

        var on = new List<string>();
        if (config.CleanOnExit) on.Add("limpeza ao fechar");
        if (config.Lockdown) on.Add("blindagem");
        if (config.BlockDownloads) on.Add("bloquear downloads");
        if (config.ForceGuest) on.Add("modo visitante");
        if (config.Adblock) on.Add("bloqueador de anúncios");
        if (config.BlockEdge) on.Add("bloquear Edge");
        if (config.LockTaskbar) on.Add("barra de tarefas travada");
        return string.Join(", ", on);
    }
}
