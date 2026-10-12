using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Midview.Protocol;

namespace Midview.Master;

// The teacher's side of the class chat (ClassChat). Opening this window opens
// the chat window on every student's screen; closing it ends the
// conversation on all of them.
public partial class ClassChatWindow : Window
{
    private readonly ClassChat _chat;
    private bool _closing;

    public ClassChatWindow(IReadOnlyList<KnownMachine> targets, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _chat = new ClassChat(targets, machine => ChatConnection.Open(machine, masterCertificate));
        _chat.LineReceived += line => Dispatcher.InvokeAsync(() => Append(line));
        _chat.Changed += () => Dispatcher.InvokeAsync(ShowStatus);
        InputTextBox.MaxLength = TextMessage.MaxLength;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        ShowStatus();
        InputTextBox.Focus();
        // Each machine updates the status line as it answers.
        await _chat.ConnectAsync();
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
        if (text.Length == 0 || _chat.ConnectedCount == 0)
        {
            return;
        }

        InputTextBox.Clear();
        Append($"Você: {text}");
        if (await _chat.SendAsync(text) == 0)
        {
            Append("(não enviada: nenhuma máquina está na conversa)");
        }
    }

    private void ShowStatus()
    {
        if (_closing)
        {
            return;
        }
        StatusTextBlock.Text = _chat.Status;
        UpdateSendButton();
    }

    private void UpdateSendButton() =>
        SendButton.IsEnabled = _chat.ConnectedCount > 0 && InputTextBox.Text.Trim().Length > 0;

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
        _chat.Dispose();
    }
}
