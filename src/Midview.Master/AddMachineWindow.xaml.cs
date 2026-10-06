using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Midview.Protocol;

namespace Midview.Master;

// "Configurações avançadas": this teacher PC's identity, and a machine
// added or edited by hand -- tested, then saved to the classroom list --
// for the exceptions "Máquinas da sala" doesn't cover. Site blocking,
// which used to live here, has its own window (SiteBlockWindow).
public partial class AddMachineWindow : Window
{
    private readonly X509Certificate2 _masterCertificate;
    private readonly Action<KnownMachine?, KnownMachine> _saveToRoster;

    // The roster entry this window edits, if any; replaced by each save, so
    // saving twice from here updates the same entry.
    private KnownMachine? _original;

    // The host and Session thumbprint known to work -- from the last
    // successful Connect, or from the roster entry being edited (it was
    // tested when it was saved). Only a verified pair can be saved.
    private (string Host, string Thumbprint)? _verified;

    public AddMachineWindow(
        X509Certificate2 masterCertificate, Action<KnownMachine?, KnownMachine> saveToRoster, KnownMachine? existing = null)
    {
        InitializeComponent();
        _masterCertificate = masterCertificate;
        _saveToRoster = saveToRoster;
        _original = existing;
        MyThumbprintTextBlock.Text = masterCertificate.Thumbprint;
        KeyFilePathTextBlock.Text = MasterIdentity.DefaultCertificatePath;

        if (existing is null)
        {
            HostAddressTextBox.Text = "localhost";
        }
        else
        {
            _verified = (existing.HostAddress, existing.SessionThumbprint);
            NameTextBox.Text = existing.Name;
            HostAddressTextBox.Text = existing.HostAddress;
            ThumbprintTextBox.Text = existing.SessionThumbprint;
            AgentThumbprintTextBox.Text = existing.AgentThumbprint ?? string.Empty;
        }
        UpdateSaveButton();
    }

    private async void OnConnectClicked(object sender, RoutedEventArgs e)
    {
        ConnectStatusTextBlock.Text = "Conectando...";
        var hostAddress = HostAddressTextBox.Text.Trim();
        var thumbprint = ThumbprintTextBox.Text.Trim();
        try
        {
            var client = await Task.Run(() => ConnectionClient.Connect(
                hostAddress, ConnectionClient.SessionPort, thumbprint, _masterCertificate));

            ConnectStatusTextBlock.Text = "Conectado.";
            _verified = (hostAddress, thumbprint);
            UpdateSaveButton();
            var name = NameTextBox.Text.Trim();
            new ViewControlWindow(client, name.Length > 0 ? name : hostAddress, control: false).Show();
        }
        catch (Exception ex)
        {
            ConnectStatusTextBlock.Text = $"Falhou: {FailureText.Describe(ex)}";
        }
    }

    private void OnRosterFieldChanged(object sender, TextChangedEventArgs e) => UpdateSaveButton();

    private void UpdateSaveButton()
    {
        var isVerified = _verified == (HostAddressTextBox.Text.Trim(), ThumbprintTextBox.Text.Trim());
        var hasName = NameTextBox.Text.Trim().Length > 0;
        SaveButton.IsEnabled = isVerified && hasName;
        SaveStatusTextBlock.Text = !isVerified
            ? "Conecte com sucesso primeiro: só uma máquina testada pode ser salva."
            : !hasName
                ? "Dê um nome à máquina para salvar."
                : string.Empty;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var agentThumbprint = AgentThumbprintTextBox.Text.Trim();
        var machine = new KnownMachine(
            NameTextBox.Text.Trim(), HostAddressTextBox.Text.Trim(), ThumbprintTextBox.Text.Trim(),
            agentThumbprint.Length > 0 ? agentThumbprint : null);
        try
        {
            _saveToRoster(_original, machine);
            _original = machine;
            SaveStatusTextBlock.Text = $"Salvo na lista da sala como \"{machine.Name}\".";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveStatusTextBlock.Text = $"Está na grade, mas a lista não pôde ser gravada em disco: {ErrorText.Describe(ex)}";
        }
    }
}
