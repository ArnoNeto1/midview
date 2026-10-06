using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// Closes the student's programs on one machine or on the whole class (the
// Session's WindowsAppCloser), after warning the student for the time the
// teacher picks. Forcing goes ahead without asking the teacher: the choice
// itself says that unsaved work is lost.
public partial class CloseAppsWindow : Window
{
    private readonly IReadOnlyList<KnownMachine> _targets;
    private readonly X509Certificate2 _masterCertificate;

    public CloseAppsWindow(IReadOnlyList<KnownMachine> targets, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        TargetsTextBlock.Text = targets.Count == 1
            ? $"Em: {targets[0].Name}"
            : $"Na turma toda ({targets.Count} máquinas)";
    }

    private async void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        var mode = ForceRadioButton.IsChecked == true ? CloseAppsMode.Force : CloseAppsMode.Ask;
        var delaySeconds = OneMinuteRadioButton.IsChecked == true ? 60
            : ThirtySecondsRadioButton.IsChecked == true ? 30
            : 0;
        CloseButton.IsEnabled = false;
        ResultTextBlock.Text = string.Empty;
        ProgressTextBlock.Text = delaySeconds == 0 ? "Fechando..." : "Avisando os alunos...";

        // All machines at once: an offline one must not hold up the rest.
        var payload = CloseAppsCommand.Encode(mode, delaySeconds);
        var results = await Task.WhenAll(_targets.Select(machine => Task.Run(() => CloseOn(machine, payload))));

        var failures = results.Count(result => !result.Succeeded);
        ProgressTextBlock.Text = failures == 0
            ? results.Length == 1 ? "Concluído." : $"Concluído nas {results.Length} máquinas."
            : $"Concluído em {results.Length - failures} de {results.Length}.";
        ResultTextBlock.Text = string.Join("\n", results.Select(result =>
            $"{(result.Succeeded ? "✓" : "✗")} {result.Name}: {result.Detail}"));
        CloseButton.IsEnabled = true;
    }

    private (string Name, bool Succeeded, string Detail) CloseOn(KnownMachine machine, byte[] payload)
    {
        try
        {
            var (succeeded, detail) = CommandClient.SendCommandForResult(
                machine.HostAddress, ConnectionClient.SessionPort, machine.SessionThumbprint, _masterCertificate,
                MessageType.CloseApps, payload);
            return (machine.Name, succeeded, detail);
        }
        catch (Exception ex)
        {
            return (machine.Name, false, FailureText.Describe(ex));
        }
    }
}
