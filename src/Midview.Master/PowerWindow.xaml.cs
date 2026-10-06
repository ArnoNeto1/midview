using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// Shuts down or restarts one machine or the whole class, or calls that off
// while it's still counting down. It's the Agent's command (it works with
// nobody logged in), so it needs each machine's Agent thumbprint. The
// toolbar's "Desligar" and "Reiniciar" both open it, each already set to
// its own action.
public partial class PowerWindow : Window
{
    private readonly IReadOnlyList<KnownMachine> _targets;
    private readonly X509Certificate2 _masterCertificate;

    public PowerWindow(IReadOnlyList<KnownMachine> targets, X509Certificate2 masterCertificate,
        PowerAction initialAction = PowerAction.Shutdown)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        TargetsTextBlock.Text = targets.Count == 1
            ? $"Em: {targets[0].Name}"
            : $"Na turma toda ({targets.Count} máquinas)";
        if (initialAction == PowerAction.Restart)
        {
            RestartRadioButton.IsChecked = true;
        }
    }

    private PowerAction SelectedAction => RestartRadioButton.IsChecked == true ? PowerAction.Restart : PowerAction.Shutdown;

    private int SelectedDelaySeconds =>
        NowRadioButton.IsChecked == true ? 0 : FiveMinutesRadioButton.IsChecked == true ? 5 * 60 : 60;

    private void OnActionChanged(object sender, RoutedEventArgs e)
    {
        // The XAML's first Checked comes before the buttons exist.
        if (RunButton is null || CancelShutdownButton is null)
        {
            return;
        }
        var restart = SelectedAction == PowerAction.Restart;
        Title = restart ? "Reiniciar" : "Desligar";
        RunButton.Content = restart ? "Reiniciar" : "Desligar";
        CancelShutdownButton.Content = restart ? "Cancelar reinício" : "Cancelar desligamento";
    }

    // No "are you sure": the window itself says what happens, and a
    // countdown can still be called off with "Cancelar".
    private async void OnRunClicked(object sender, RoutedEventArgs e) =>
        await SendToAll(SelectedAction, SelectedDelaySeconds);

    private async void OnCancelShutdownClicked(object sender, RoutedEventArgs e) =>
        await SendToAll(PowerAction.Cancel, 0);

    // Every machine at once: an offline one must not hold up the rest.
    private async Task SendToAll(PowerAction action, int delaySeconds)
    {
        RunButton.IsEnabled = CancelShutdownButton.IsEnabled = false;
        ResultTextBlock.Text = string.Empty;
        ProgressTextBlock.Text = action == PowerAction.Cancel ? "Cancelando..." : "Enviando...";

        var payload = PowerCommand.Encode(action, delaySeconds);
        var results = await Task.WhenAll(_targets.Select(machine => Task.Run(() => SendTo(machine, payload))));

        var failures = results.Count(result => !result.Succeeded);
        ProgressTextBlock.Text = failures == 0
            ? results.Length == 1 ? "Concluído." : $"Concluído nas {results.Length} máquinas."
            : $"Concluído em {results.Length - failures} de {results.Length}.";
        ResultTextBlock.Text = string.Join("\n", results.Select(result =>
            $"{(result.Succeeded ? "✓" : "✗")} {result.Name}: {result.Detail}"));
        RunButton.IsEnabled = CancelShutdownButton.IsEnabled = true;
    }

    private (string Name, bool Succeeded, string Detail) SendTo(KnownMachine machine, byte[] payload)
    {
        if (machine.AgentThumbprint is not { Length: > 0 } agentThumbprint)
        {
            return (machine.Name, false,
                "falta o thumbprint do Agent (reinstale com o pacote de instalação, ou preencha em \"Testar / bloquear sites...\")");
        }

        try
        {
            var (succeeded, detail) = CommandClient.SendCommandForResult(
                machine.HostAddress, ConnectionClient.AgentPort, agentThumbprint, _masterCertificate,
                MessageType.Power, payload);
            return (machine.Name, succeeded, detail);
        }
        catch (Exception ex)
        {
            return (machine.Name, false, FailureText.DescribeAgent(ex));
        }
    }
}
