using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Midview.Protocol;

namespace Midview.Master;

// The teacher's side of a private chat with one machine. Opening this window
// opens the chat window on the student's screen; closing it ends the
// conversation there.
public partial class ChatWindow : Window
{
    private readonly KnownMachine _machine;
    private readonly X509Certificate2 _masterCertificate;
    private ChatConnection? _connection;
    private bool _closing;

    public ChatWindow(KnownMachine machine, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _machine = machine;
        _masterCertificate = masterCertificate;
        Title = $"Conversa com {machine.Name}";
        InputTextBox.MaxLength = TextMessage.MaxLength;
    }

    public KnownMachine Machine => _machine;

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        StatusTextBlock.Text = "Conectando...";
        try
        {
            var connection = await Task.Run(() => ChatConnection.Open(_machine, _masterCertificate));
            if (_closing)
            {
                connection.Dispose();
                return;
            }

            connection.StudentMessageReceived += text => Dispatcher.InvokeAsync(() => Append($"{_machine.Name}: {text}"));
            connection.Closed += reason => Dispatcher.InvokeAsync(() => OnConversationEnded(reason));
            _connection = connection;
            connection.Start();

            StatusTextBlock.Text = "Conectado: a janela de conversa abriu na tela do aluno.";
            UpdateSendButton();
            InputTextBox.Focus();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Não foi possível conectar: {FailureText.Describe(ex)}";
        }
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e) => UpdateSendButton();

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SendInput();
            e.Handled = true;
        }
    }

    private void OnSendClicked(object sender, RoutedEventArgs e) => SendInput();

    private async void SendInput()
    {
        var text = InputTextBox.Text.Trim();
        var connection = _connection;
        if (connection is null || text.Length == 0)
        {
            return;
        }

        InputTextBox.Clear();
        Append($"Você: {text}");
        // Off the UI thread: a dropping connection can take a while to fail.
        bool sent;
        try
        {
            sent = await Task.Run(() => connection.Send(text));
        }
        catch (EncoderFallbackException)
        {
            Append("(não enviada: o texto tem um caractere inválido)");
            return;
        }
        if (!sent)
        {
            Append("(não enviada: a conversa caiu)");
        }
    }

    private void OnConversationEnded(string reason)
    {
        if (_closing)
        {
            return;
        }
        _connection = null;
        StatusTextBlock.Text = $"Conversa encerrada ({reason}). Feche e abra de novo para reconectar.";
        UpdateSendButton();
    }

    private void UpdateSendButton() =>
        SendButton.IsEnabled = _connection is not null && InputTextBox.Text.Trim().Length > 0;

    private void Append(string line)
    {
        if (TranscriptTextBox.Text.Length > 0)
        {
            TranscriptTextBox.AppendText(Environment.NewLine);
        }
        TranscriptTextBox.AppendText(line);
        TranscriptTextBox.ScrollToEnd();
    }

    private void OnWindowClosed(object sender, EventArgs e)
    {
        _closing = true;
        _connection?.Dispose();
    }
}
