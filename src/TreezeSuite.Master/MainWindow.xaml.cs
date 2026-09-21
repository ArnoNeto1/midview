using System.Windows;
using TreezeSuite.Protocol;

namespace TreezeSuite.Master;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnConnectClicked(object sender, RoutedEventArgs e)
    {
        var masterCertificate = SelfSignedCertificate.Create("TreezeMaster");
        var client = ConnectionClient.Connect(
            HostAddressTextBox.Text, 5901, ThumbprintTextBox.Text, masterCertificate);

        var viewControlWindow = new ViewControlWindow(client);
        viewControlWindow.Show();
    }
}
