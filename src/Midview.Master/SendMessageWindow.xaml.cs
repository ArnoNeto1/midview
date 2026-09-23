using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Midview.Protocol;

namespace Midview.Master;

// Sends one text to one machine or to the whole class; each student sees it
// in a window on their screen (the Session's WinFormsMessageDisplay).
public partial class SendMessageWindow : Window
{
    private readonly IReadOnlyList<KnownMachine> _targets;
    private readonly X509Certificate2 _masterCertificate;

    public SendMessageWindow(IReadOnlyList<KnownMachine> targets, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        MessageTextBox.MaxLength = TeacherMessage.MaxLength;
        TargetsTextBlock.Text = targets.Count == 1
            ? $"Para: {targets[0].Name}"
            : $"Para a turma toda ({targets.Count} máquinas)";
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e) => MessageTextBox.Focus();

    private void OnMessageChanged(object sender, TextChangedEventArgs e) =>
        SendButton.IsEnabled = MessageTextBox.Text.Trim().Length > 0;

    private async void OnSendClicked(object sender, RoutedEventArgs e)
    {
        var payload = TeacherMessage.Encode(MessageTextBox.Text.Trim());
        SendButton.IsEnabled = false;
        ResultTextBlock.Text = "Enviando...";

        // All machines at once: an offline one must not hold up the rest.
        var results = await Task.WhenAll(_targets.Select(machine => Task.Run(() => SendTo(machine, payload))));

        var failures = results.Where(result => result.Failure is not null).ToList();
        var delivered = results.Length - failures.Count;
        ResultTextBlock.Text = failures.Count == 0
            ? results.Length == 1 ? "Mensagem enviada." : $"Mensagem enviada para as {delivered} máquinas."
            : $"Enviada para {delivered} de {results.Length}. Não chegou em:\n"
              + string.Join("\n", failures.Select(failure => $"• {failure.Name}: {failure.Failure}"));
        SendButton.IsEnabled = MessageTextBox.Text.Trim().Length > 0;
    }

    private (string Name, string? Failure) SendTo(KnownMachine machine, byte[] payload)
    {
        try
        {
            var shown = CommandClient.SendCommand(
                machine.HostAddress, ConnectionClient.SessionPort, machine.SessionThumbprint, _masterCertificate,
                MessageType.ShowMessage, payload);
            return (machine.Name, shown ? null : "a máquina não conseguiu mostrar a mensagem");
        }
        catch (Exception ex)
        {
            return (machine.Name, FailureText.Describe(ex));
        }
    }
}
