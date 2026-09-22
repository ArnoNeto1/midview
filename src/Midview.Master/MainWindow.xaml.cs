using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

public partial class MainWindow : Window
{
    private readonly X509Certificate2 _masterCertificate = MasterIdentity.LoadOrCreate(MasterIdentity.DefaultCertificatePath);

    public MainWindow()
    {
        InitializeComponent();
        MyThumbprintTextBlock.Text = _masterCertificate.Thumbprint;
    }

    private async void OnConnectClicked(object sender, RoutedEventArgs e)
    {
        ConnectStatusTextBlock.Text = "Connecting...";
        var hostAddress = HostAddressTextBox.Text;
        var thumbprint = ThumbprintTextBox.Text;
        try
        {
            var client = await Task.Run(() => ConnectionClient.Connect(
                hostAddress, 5901, thumbprint, _masterCertificate));

            ConnectStatusTextBlock.Text = string.Empty;
            var viewControlWindow = new ViewControlWindow(client);
            viewControlWindow.Show();
        }
        catch (Exception ex)
        {
            ConnectStatusTextBlock.Text = $"Failed: {DescribeException(ex)}";
        }
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
                hostAddress, AgentServerWorkerAgentPort, thumbprint, _masterCertificate, commandType, payload));
            SiteBlockStatusTextBlock.Text = success ? "OK" : "Failed";
        }
        catch (Exception ex)
        {
            SiteBlockStatusTextBlock.Text = $"Failed: {DescribeException(ex)}";
        }
    }

    private static string DescribeException(Exception ex) =>
        ex.InnerException is not null ? $"{ex.Message} -> {ex.InnerException.Message}" : ex.Message;

    private const int AgentServerWorkerAgentPort = 28900; // must match Midview.Agent.AgentServerWorker.AgentPort
}
