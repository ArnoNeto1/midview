using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// Shuts down or restarts one machine or the whole class, or calls that off
// while it's still counting down. It's the Agent's command (it works with
// nobody logged in), so it needs each machine's Agent thumbprint.
public partial class PowerWindow : Window
{
    private readonly IReadOnlyList<KnownMachine> _targets;
    private readonly X509Certificate2 _masterCertificate;

    public PowerWindow(IReadOnlyList<KnownMachine> targets, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        TargetsTextBlock.Text = targets.Count == 1
            ? $"Em: {targets[0].Name}"
            : $"Na turma toda ({targets.Count} máquinas)";
    }

    private PowerAction SelectedAction => RestartRadioButton.IsChecked == true ? PowerAction.Restart : PowerAction.Shutdown;

    private int SelectedDelaySeconds =>
        NowRadioButton.IsChecked == true ? 0 : FiveMinutesRadioButton.IsChecked == true ? 5 * 60 : 60;

    private void OnActionChanged(object sender, RoutedEventArgs e)
    {
        if (RunButton is not null)
        {
            RunButton.Content = SelectedAction == PowerAction.Restart ? "Reiniciar" : "Desligar";
        }
    }

    private async void OnRunClicked(object sender, RoutedEventArgs e)
    {
        var action = SelectedAction;
        var delaySeconds = SelectedDelaySeconds;
        var verb = action == PowerAction.Restart ? "Reiniciar" : "Desligar";
        var where = _targets.Count == 1 ? _targets[0].Name : $"as {_targets.Count} máquinas da turma";
        var when = delaySeconds == 0 ? "agora" : delaySeconds == 60 ? "em 1 minuto" : $"em {delaySeconds / 60} minutos";
        var answer = MessageBox.Show(this,
            $"{verb} {where} {when}?\n\nO trabalho não salvo nos programas abertos é perdido.",
            "Midview", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
        {
            await SendToAll(action, delaySeconds);
        }
    }

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
