using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Midview.Protocol;

namespace Midview.Master;

// Sends one text to one machine or to the whole class; each student sees it
// in a window on their screen (the Session's WinFormsMessageDisplay), or over
// the app on a phone (MessageTargets).
public partial class SendMessageWindow : MidviewWindow
{
    private readonly IReadOnlyList<KnownMachine> _targets;
    private readonly X509Certificate2 _masterCertificate;

    // wholeClass: targets is every machine in the classroom list (TargetsText).
    public SendMessageWindow(IReadOnlyList<KnownMachine> targets, bool wholeClass, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        MessageTextBox.MaxLength = TextMessage.MaxLength;
        TargetsTextBlock.Text = TargetsText.Describe(TargetsPreposition.To, targets, wholeClass);
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e) => MessageTextBox.Focus();

    private void OnMessageChanged(object sender, TextChangedEventArgs e) =>
        SendButton.IsEnabled = MessageTextBox.Text.Trim().Length > 0;

    private async void OnSendClicked(object sender, RoutedEventArgs e)
    {
        // Text that can't be encoded isn't sent: the text stays, with the
        // character selected, for the teacher to fix.
        var invalid = TypedText.IndexOfInvalidCharacter(MessageTextBox.Text);
        if (invalid >= 0)
        {
            ResultTextBlock.Text = "Não enviada: o texto tem um caractere inválido (talvez metade de um emoji colado). "
                + "Ele ficou selecionado: apague-o e envie de novo.";
            MessageTextBox.Focus();
            MessageTextBox.Select(invalid, 1);
            return;
        }

        var payload = TextMessage.Encode(MessageTextBox.Text.Trim());
        SendButton.IsEnabled = false;
        ResultTextBlock.Text = "Enviando...";

        // All machines at once: an offline one must not hold up the rest.
        var results = await Task.WhenAll(_targets.Select(machine =>
            Task.Run(() => MessageTargets.Send(machine, payload, _masterCertificate))));

        ResultTextBlock.Text = MessageTargets.Summary(results);
        SendButton.IsEnabled = MessageTextBox.Text.Trim().Length > 0;
    }
}
