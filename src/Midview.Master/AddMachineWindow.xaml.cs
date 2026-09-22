using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// Where a machine is proven to work before it joins the classroom list, and
// where one-off unsaved connections still happen. Opened blank from the
// grid's "Adicionar máquina", or prefilled with a saved machine -- which is
// also what scopes the site-blocking buttons to that machine.
public partial class AddMachineWindow : Window
{
    private readonly X509Certificate2 _masterCertificate;

    // What the last successful Connect used. Only this exact pair can be
    // saved: the roster should never hold an entry nobody has tested.
    private (string HostAddress, string Thumbprint)? _verified;

    public AddMachineWindow(X509Certificate2 masterCertificate, KnownMachine? machine)
    {
        InitializeComponent();
        _masterCertificate = masterCertificate;
        MyThumbprintTextBlock.Text = _masterCertificate.Thumbprint;
        KeyFilePathTextBlock.Text = MasterIdentity.DefaultCertificatePath;

        if (machine is not null)
        {
            MachineNameTextBox.Text = machine.Name;
            HostAddressTextBox.Text = machine.HostAddress;
            ThumbprintTextBox.Text = machine.SessionThumbprint;
            AgentThumbprintTextBox.Text = machine.AgentThumbprint ?? string.Empty;
        }

        // Subscribed here, not in XAML: TextChanged already fires inside
        // InitializeComponent (for HostAddressTextBox's default text),
        // before SaveToRosterButton has been created.
        HostAddressTextBox.TextChanged += (_, _) => UpdateSaveButton();
        ThumbprintTextBox.TextChanged += (_, _) => UpdateSaveButton();
    }

    // Raised on the UI thread. MainWindow adds the machine to the roster, or
    // replaces the saved entry with the same host address.
    public event Action<KnownMachine>? MachineSaved;

    private async void OnConnectClicked(object sender, RoutedEventArgs e)
    {
        ConnectStatusTextBlock.Text = "Connecting...";
        var hostAddress = HostAddressTextBox.Text;
        var thumbprint = ThumbprintTextBox.Text;
        try
        {
            var client = await Task.Run(() => ConnectionClient.Connect(
                hostAddress, MidviewPorts.Session, thumbprint, _masterCertificate));

            ConnectStatusTextBlock.Text = string.Empty;
            _verified = (hostAddress, thumbprint);
            UpdateSaveButton();
            var viewControlWindow = new ViewControlWindow(client);
            viewControlWindow.Show();
        }
        catch (Exception ex)
        {
            ConnectStatusTextBlock.Text = $"Failed: {ConnectionErrors.Describe(ex)}";
        }
    }

    private void UpdateSaveButton() =>
        SaveToRosterButton.IsEnabled = _verified is { } verified
            && HostAddressTextBox.Text == verified.HostAddress
            && ThumbprintTextBox.Text == verified.Thumbprint;

    private void OnSaveToRosterClicked(object sender, RoutedEventArgs e)
    {
        if (_verified is not { } verified)
        {
            return;
        }

        var name = MachineNameTextBox.Text.Trim();
        var agentThumbprint = AgentThumbprintTextBox.Text;
        var machine = new KnownMachine(
            name.Length > 0 ? name : verified.HostAddress,
            verified.HostAddress,
            verified.Thumbprint,
            string.IsNullOrWhiteSpace(agentThumbprint) ? null : agentThumbprint);

        MachineSaved?.Invoke(machine);
        SaveStatusTextBlock.Text = $"\"{machine.Name}\" salva na lista da turma.";
    }

    private async void OnBlockInternetClicked(object sender, RoutedEventArgs e) =>
        await SendAgentCommandAsync(MessageType.BlockInternet, Array.Empty<byte>());

    private async void OnUnblockInternetClicked(object sender, RoutedEventArgs e) =>
        await SendAgentCommandAsync(MessageType.UnblockInternet, Array.Empty<byte>());

    private async void OnBlockDomainClicked(object sender, RoutedEventArgs e) =>
        await SendAgentCommandAsync(MessageType.BlockDomain, Encoding.UTF8.GetBytes(DomainTextBox.Text));

    private async void OnUnblockDomainClicked(object sender, RoutedEventArgs e) =>
        await SendAgentCommandAsync(MessageType.UnblockDomain, Encoding.UTF8.GetBytes(DomainTextBox.Text));

    private async Task SendAgentCommandAsync(MessageType commandType, byte[] payload)
    {
        SiteBlockStatusTextBlock.Text = "Sending...";
        var hostAddress = HostAddressTextBox.Text;
        var thumbprint = AgentThumbprintTextBox.Text;
        try
        {
            var success = await Task.Run(() => AgentCommandClient.SendCommand(
                hostAddress, MidviewPorts.Agent, thumbprint, _masterCertificate, commandType, payload));
            SiteBlockStatusTextBlock.Text = success ? "OK" : "Failed";
        }
        catch (Exception ex)
        {
            SiteBlockStatusTextBlock.Text = $"Failed: {ConnectionErrors.Describe(ex)}";
        }
    }
}
