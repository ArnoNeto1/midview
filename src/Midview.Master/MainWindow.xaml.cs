using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

public partial class MainWindow : Window
{
    private readonly X509Certificate2 _masterCertificate = SelfSignedCertificate.Create("MidviewMaster");

    public MainWindow()
    {
        InitializeComponent();
        MyThumbprintTextBlock.Text = _masterCertificate.Thumbprint;
    }

    private void OnConnectClicked(object sender, RoutedEventArgs e)
    {
        var client = ConnectionClient.Connect(
            HostAddressTextBox.Text, 5901, ThumbprintTextBox.Text, _masterCertificate);

        var viewControlWindow = new ViewControlWindow(client);
        viewControlWindow.Show();
    }

    private void OnBlockInternetClicked(object sender, RoutedEventArgs e) =>
        SendAgentCommand(MessageType.BlockInternet, Array.Empty<byte>());

    private void OnUnblockInternetClicked(object sender, RoutedEventArgs e) =>
        SendAgentCommand(MessageType.UnblockInternet, Array.Empty<byte>());

    private void OnBlockDomainClicked(object sender, RoutedEventArgs e) =>
        SendAgentCommand(MessageType.BlockDomain, Encoding.UTF8.GetBytes(DomainTextBox.Text));

    private void OnUnblockDomainClicked(object sender, RoutedEventArgs e) =>
        SendAgentCommand(MessageType.UnblockDomain, Encoding.UTF8.GetBytes(DomainTextBox.Text));

    private void SendAgentCommand(MessageType commandType, byte[] payload)
    {
        var success = AgentCommandClient.SendCommand(
            HostAddressTextBox.Text, AgentServerWorkerAgentPort, AgentThumbprintTextBox.Text,
            _masterCertificate, commandType, payload);
        SiteBlockStatusTextBlock.Text = success ? "OK" : "Failed";
    }

    private const int AgentServerWorkerAgentPort = 5900; // must match Midview.Agent.AgentServerWorker.AgentPort
}
