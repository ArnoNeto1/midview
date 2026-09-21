using System.Security.Cryptography.X509Certificates;
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
}
