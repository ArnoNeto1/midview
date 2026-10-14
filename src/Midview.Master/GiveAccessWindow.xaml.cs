using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// "Dar acesso a um colega", for the teacher who has the class (InviteHost):
// shows the code while the invite lasts, then who got the class. Closing it
// cancels the invite.
public partial class GiveAccessWindow : Window
{
    private readonly X509Certificate2 _masterCertificate;
    private readonly Func<IReadOnlyList<KnownMachine>> _roster;
    private InviteHost? _host;

    public GiveAccessWindow(X509Certificate2 masterCertificate, Func<IReadOnlyList<KnownMachine>> roster)
    {
        InitializeComponent();
        _masterCertificate = masterCertificate;
        _roster = roster;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The class as it is now: a machine added while the code is on the
        // screen goes with the next invite.
        var machines = _roster();
        if (machines.Count == 0)
        {
            ShowOver("A lista da sala está vazia: não há máquinas para dar ao colega.");
            return;
        }

        try
        {
            _host = InviteHost.Open(
                _masterCertificate, Environment.MachineName,
                grant => ClassGrant.Give(machines, ClassGrant.OverTheNetwork(_masterCertificate, grant)),
                TeacherInvite.Lifetime,
                joining: name => Dispatcher.InvokeAsync(() => StatusTextBlock.Text = $"{name} entrou. Liberando as máquinas..."));
        }
        catch (SocketException ex)
        {
            ShowOver($"Não foi possível abrir o convite neste computador: {ErrorText.Describe(ex)}");
            return;
        }

        CodeTextBlock.Text = TeacherInvite.Format(_host.Code);
        HintTextBlock.Text = InviteText.Waiting(machines.Count, DateTime.Now + TeacherInvite.Lifetime);
        var outcome = await _host.Outcome;
        LeadTextBlock.Text = "O código não vale mais.";
        CodeTextBlock.Text = string.Empty;
        HintTextBlock.Text = string.Empty;
        StatusTextBlock.Text = InviteText.Ended(outcome);
        CloseButton.Content = "Fechar";
    }

    private void ShowOver(string text)
    {
        LeadTextBlock.Text = text;
        CodeTextBlock.Visibility = Visibility.Collapsed;
        CloseButton.Content = "Fechar";
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e) => _host?.Dispose();
}
