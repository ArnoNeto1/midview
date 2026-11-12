using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// Shuts down or restarts one machine or the whole class, or calls that off
// while it's still counting down. It's the Agent's command (it works with
// nobody logged in), so it needs each machine's Agent thumbprint. The
// toolbar's "Desligar" and "Reiniciar" both open it, each already set to
// its own action. A phone takes the same command on its agent: it restarts,
// or for "Desligar" turns its screen off and locks (PowerTargets); a line
// says so, and for phones alone the Windows updates are not offered.
public partial class PowerWindow : MidviewWindow
{
    private readonly IReadOnlyList<KnownMachine> _targets;
    private readonly X509Certificate2 _masterCertificate;

    // wholeClass: targets is every machine in the classroom list (TargetsText).
    public PowerWindow(IReadOnlyList<KnownMachine> targets, bool wholeClass, X509Certificate2 masterCertificate,
        PowerAction initialAction = PowerAction.Shutdown)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        TargetsTextBlock.Text = PowerTargets.Describe(targets, wholeClass);
        DelayHintTextBlock.Text = PowerTargets.DelayHint(targets);
        if (PowerTargets.Note(targets) is { } note)
        {
            PhoneNoteTextBlock.Text = note;
            PhoneNoteTextBlock.Visibility = Visibility.Visible;
        }
        if (PowerTargets.OnlyPhones(targets))
        {
            // Never ticked, so no phone is ever sent it; the window is shorter without it.
            UpdatesCard.Visibility = Visibility.Collapsed;
            MinHeight = 500;
            Height = 600;
        }
        if (initialAction == PowerAction.Restart)
        {
            RestartRadioButton.IsChecked = true;
        }
        OnActionChanged(this, new RoutedEventArgs());
    }

    private bool InstallUpdates => InstallUpdatesCheckBox.IsChecked == true;

    private PowerAction SelectedAction => RestartRadioButton.IsChecked == true ? PowerAction.Restart : PowerAction.Shutdown;

    private int SelectedDelaySeconds =>
        NowRadioButton.IsChecked == true ? 0 : FiveMinutesRadioButton.IsChecked == true ? 5 * 60 : 60;

    private void OnActionChanged(object sender, RoutedEventArgs e)
    {
        // The XAML's first Checked comes before the buttons exist.
        if (RunButton is null || CancelShutdownButton is null || InstallUpdatesCheckBox is null)
        {
            return;
        }
        var restart = SelectedAction == PowerAction.Restart;
        Title = restart ? "Reiniciar" : "Desligar";
        Header.Heading = Title;
        Header.Icon = restart ? "" : "";
        RunButton.Content = PowerText.RunButton(restart, InstallUpdates);
        CancelShutdownButton.Content = restart ? "Cancelar reinício" : "Cancelar desligamento";
        UpdatesHintTextBlock.Text = PowerText.UpdatesHint(restart);
    }

    // No "are you sure": the window itself says what happens, and a
    // countdown can still be called off with "Cancelar".
    private async void OnRunClicked(object sender, RoutedEventArgs e) =>
        await SendToAll(SelectedAction, SelectedDelaySeconds, InstallUpdates);

    private async void OnCancelShutdownClicked(object sender, RoutedEventArgs e) =>
        await SendToAll(PowerAction.Cancel, 0, installUpdates: false);

    // Every machine at once: an offline one must not hold up the rest.
    private async Task SendToAll(PowerAction action, int delaySeconds, bool installUpdates)
    {
        RunButton.IsEnabled = CancelShutdownButton.IsEnabled = false;
        ResultTextBlock.Text = string.Empty;
        ProgressTextBlock.Text = action == PowerAction.Cancel ? "Cancelando..." : "Enviando...";

        var results = await Task.WhenAll(_targets.Select(machine => Task.Run(() =>
        {
            var (succeeded, detail) = PowerTargets.Send(machine, action, delaySeconds, installUpdates, _masterCertificate);
            return (machine.Name, Succeeded: succeeded, Detail: detail);
        })));

        ProgressTextBlock.Text = PowerTargets.Done(_targets, results.Count(result => !result.Succeeded));
        ResultTextBlock.Text = string.Join("\n", results.Select(result =>
            $"{(result.Succeeded ? "✓" : "✗")} {result.Name}: {result.Detail}"));
        RunButton.IsEnabled = CancelShutdownButton.IsEnabled = true;
    }
}
