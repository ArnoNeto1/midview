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
// functions are left as they were. Phones among the targets take only the
// functions that mean something on them (LabConfigPhones): each row says
// what it does on a phone, and the others reach the computers alone.
public partial class LabConfigWindow : MidviewWindow
{
    private readonly IReadOnlyList<KnownMachine> _targets;
    private readonly X509Certificate2 _masterCertificate;

    // Every row of the window, by its function name (LabConfigPhones).
    private static readonly string[] Functions =
    {
        "adblock", "clean", "lockdown", "edge", "guest", "taskbar", "desktop", "downloads",
        LabConfigPhones.Distraction, LabConfigPhones.ClearProfiles, LabConfigPhones.ClearDownloads,
        LabConfigPhones.Reset, LabConfigPhones.ShowApplied,
    };

    // wholeClass: targets is every machine in the classroom list (TargetsText).
    public LabConfigWindow(IReadOnlyList<KnownMachine> targets, bool wholeClass, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        TargetsTextBlock.Text = TargetsText.Describe(TargetsPreposition.In, targets, wholeClass);
        ShowPhoneNotes();
    }

    // With phones among the targets, each row says what it does on them; a
    // computers-only row is greyed when there is no computer to reach.
    private void ShowPhoneNotes()
    {
        if (!_targets.Any(DeviceSupport.IsAndroid))
        {
            return;
        }
        PhonesHint.Text = LabConfigPhones.PhonesHint;
        PhonesHint.Visibility = Visibility.Visible;
        foreach (var function in Functions)
        {
            if (LabConfigPhones.Note(function) is { } note && FindName("Note_" + function) is TextBlock noteBlock)
            {
                noteBlock.Text = note;
                noteBlock.Visibility = Visibility.Visible;
            }
            if (LabConfigPhones.Reach(function, _targets).Count == 0 && FindName("Buttons_" + function) is UIElement buttons)
            {
                buttons.IsEnabled = false;
            }
        }
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
        await RunAsync(flag, machine =>
        {
            var phone = DeviceSupport.IsAndroid(machine);
            var status = LabConfigClient.GetStatus(machine, _masterCertificate);
            if (status.OldAgent)
            {
                return (false, phone ? LabConfigPhones.OldAgent : "Midview antigo nesta máquina: atualize-a para usar o painel");
            }
            var updated = LabConfigChange.With(status.Config!, flag, on)
                with { SavedAt = LabConfigChange.NextSavedAt(now, status.Config!.SavedAt) };
            var (succeeded, detail) = LabConfigClient.Apply(
                machine, _masterCertificate, updated, timeout: phone ? LabConfigPhones.CommandTimeout : null);
            // An Agent from before a function refuses the flag it doesn't
            // know as an invalid request (LabConfig.TryDecode).
            return !succeeded && detail == "pedido inválido"
                ? (false, "Midview antigo nesta máquina: atualize-a para usar esta função")
                : (succeeded, detail);
        });
    }

    private async void OnResetClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(LabConfigPhones.Reset, machine => LabConfigClient.Reset(
            machine, _masterCertificate, timeout: DeviceSupport.IsAndroid(machine) ? LabConfigPhones.CommandTimeout : null));

    private async void OnClearChromeProfilesClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(LabConfigPhones.ClearProfiles, machine => LabConfigClient.ClearChromeProfiles(machine, _masterCertificate));

    private async void OnClearDownloadsTodayClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(LabConfigPhones.ClearDownloads, machine => LabConfigClient.ClearDownloads(machine, _masterCertificate, DownloadsScope.Today));

    private async void OnClearDownloadsAllClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(LabConfigPhones.ClearDownloads, machine => LabConfigClient.ClearDownloads(machine, _masterCertificate, DownloadsScope.All));

    private async void OnBlockDistractionClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(LabConfigPhones.Distraction, machine => LabConfigClient.SetDistractionSites(machine, _masterCertificate, block: true));

    private async void OnUnblockDistractionClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(LabConfigPhones.Distraction, machine => LabConfigClient.SetDistractionSites(machine, _masterCertificate, block: false));

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
                return (status.OldAgent, DeviceSupport.IsAndroid(machine)) switch
                {
                    (true, true) => $"• {machine.Name}: {LabConfigPhones.OldAgent}",
                    (true, false) => $"• {machine.Name}: Midview antigo (atualize-a)",
                    (false, true) => $"• {machine.Name}: {LabConfigPhones.Describe(status.Config!)}",
                    _ => $"• {machine.Name}: {Describe(status.Config!)}",
                };
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

    // Runs one command on every target the function reaches (the computers
    // alone for one phones don't take: LabConfigPhones) at once and shows a
    // per-machine result.
    private async Task RunAsync(string function, Func<KnownMachine, (bool Succeeded, string Detail)> command)
    {
        var reach = LabConfigPhones.Reach(function, _targets);
        if (reach.Count == 0)
        {
            return;
        }
        IsEnabled = false;
        ResultTextBlock.Text = string.Empty;
        ProgressTextBlock.Text = "Enviando...";

        var results = await Task.WhenAll(reach.Select(machine => Task.Run(() => RunOne(machine, command))));

        var failures = results.Count(result => !result.Succeeded);
        var done = failures == 0
            ? results.Length == 1 ? "Feito." : $"Feito nas {results.Length} máquinas."
            : $"Feito em {results.Length - failures} de {results.Length}.";
        ProgressTextBlock.Text = LabConfigPhones.LeftOut(_targets.Count - reach.Count) is { } leftOut ? $"{done} {leftOut}" : done;
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
            // A phone's agent from before the panel came to phones answers
            // "Ainda não funciona em celular.".
            if (DeviceSupport.IsAndroid(machine) && PhoneAgentCommand.IsOldAgentAnswer(succeeded, detail))
            {
                return (machine.Name, false, LabConfigPhones.OldAgent);
            }
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
            return (machine.Name, false,
                DeviceSupport.IsAndroid(machine) ? FailureText.DescribePhoneAgent(ex) : FailureText.DescribeAgent(ex));
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
        if (config.BlockDownloads) on.Add("travar downloads");
        if (config.ForceGuest) on.Add("modo visitante");
        if (config.Adblock) on.Add("bloqueador de anúncios");
        if (config.BlockEdge) on.Add("bloquear Edge");
        if (config.LockTaskbar) on.Add("barra de tarefas travada");
        if (config.LockDesktop) on.Add("Área de Trabalho travada");
        return string.Join(", ", on);
    }
}
