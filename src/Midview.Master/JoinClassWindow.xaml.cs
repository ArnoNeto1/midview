using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Media;
using Midview.Protocol;

namespace Midview.Master;

// "Entrar na sala de um colega" (InviteJoin): type the code a colleague
// reads out, and that colleague's class comes into this one.
public partial class JoinClassWindow : Window
{
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xB3, 0x26, 0x1E));

    private readonly X509Certificate2 _masterCertificate;
    private readonly Action<IReadOnlyList<KnownMachine>> _addMachines;

    // addMachines puts the machines in the classroom list (MainWindow); it
    // may throw when the list can't be written to disk.
    public JoinClassWindow(X509Certificate2 masterCertificate, Action<IReadOnlyList<KnownMachine>> addMachines)
    {
        InitializeComponent();
        _masterCertificate = masterCertificate;
        _addMachines = addMachines;
    }

    private async void OnJoinClicked(object sender, RoutedEventArgs e)
    {
        if (!TeacherInvite.TryRead(CodeTextBox.Text, out var code))
        {
            ShowStatus(TeacherInvite.BadCode, isError: true);
            CodeTextBox.Focus();
            return;
        }

        JoinButton.IsEnabled = false;
        CodeTextBox.IsEnabled = false;
        IProgress<string> progress = new Progress<string>(text => ShowStatus(text, isError: false));
        try
        {
            var teacherName = Environment.MachineName;
            // Looking up the network cards is inside Task.Run too: it can
            // throw (NetworkInformationException), and the UI thread
            // shouldn't wait for it.
            var (_, result) = await Task.Run(() => InviteJoin.Join(
                code, _masterCertificate, teacherName,
                WakeOnLanSender.BroadcastTargets(WakeOnLanSender.LocalNetworks(), TeacherInvite.Port),
                TeacherInvite.ListenTime, progress.Report));
            try
            {
                _addMachines(InviteJoin.ToKnownMachines(result));
                ShowStatus(InviteText.Received(result), isError: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowStatus($"{InviteText.Received(result)} Mas a lista da sala não pôde ser gravada: {ErrorText.Describe(ex)}", isError: true);
            }
        }
        catch (InviteJoinException ex)
        {
            ShowStatus(ex.Message, isError: true);
            JoinButton.IsEnabled = true;
            CodeTextBox.IsEnabled = true;
        }
        catch (SocketException ex)
        {
            ShowStatus($"A procura falhou: {ErrorText.Describe(ex)}", isError: true);
            JoinButton.IsEnabled = true;
            CodeTextBox.IsEnabled = true;
        }
        catch (Exception ex)
        {
            // This is async void and Master has no handler for exceptions
            // left unhandled on the UI thread: anything else (the network
            // cards couldn't be read, say) would close the program in the
            // middle of the class.
            ShowStatus($"A procura falhou: {ErrorText.Describe(ex)}", isError: true);
            JoinButton.IsEnabled = true;
            CodeTextBox.IsEnabled = true;
        }
    }

    private void ShowStatus(string text, bool isError)
    {
        StatusTextBlock.Text = text;
        StatusTextBlock.Foreground = isError ? ErrorBrush : (Brush)FindResource("InkBrush");
    }
}
