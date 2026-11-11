using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using QRCoder;

namespace Midview.Master;

// "Preparar celular": asks for the lab Wi-Fi (once; remembered) and shows the
// QR code a factory-reset Android phone scans to become a Midview phone.
public partial class PreparePhoneWindow : MidviewWindow
{
    private static readonly string[] Steps =
    {
        "Nos Xiaomi, saia da conta Mi (Configurações → Conta Mi → Sair).",
        "Restaure o celular de fábrica.",
        "Na primeira tela, toque 6 vezes no fundo branco.",
        "Escaneie este código.",
        "Quando o celular terminar, use \"Procurar e parear\".",
    };

    public PreparePhoneWindow()
    {
        InitializeComponent();
        for (var i = 0; i < Steps.Length; i++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new Label { Content = (i + 1).ToString(), Style = (Style)FindResource("StepBadge"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0) });
            row.Children.Add(new TextBlock { Text = Steps[i], TextWrapping = TextWrapping.Wrap, Width = 420, VerticalAlignment = VerticalAlignment.Center });
            StepsPanel.Children.Add(row);
        }
        if (PhoneWifiFile.Load() is { } saved)
        {
            SsidInput.Text = saved.Ssid;
            PasswordInput.Text = saved.Password;
            Show(saved.Ssid, saved.Password);
        }
    }

    private void OnGenerateClicked(object sender, RoutedEventArgs e)
    {
        var ssid = SsidInput.Text.Trim();
        if (ssid.Length == 0)
        {
            ErrorTextBlock.Text = "Digite o nome do Wi-Fi do laboratório.";
            return;
        }
        if (PasswordInput.Text.Length < 8)
        {
            ErrorTextBlock.Text = "A senha de um Wi-Fi WPA tem pelo menos 8 caracteres.";
            return;
        }
        ErrorTextBlock.Text = "";
        try
        {
            PhoneWifiFile.Save(ssid, PasswordInput.Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not remembering the Wi-Fi is not worth refusing the code.
        }
        Show(ssid, PasswordInput.Text);
    }

    private void Show(string ssid, string password)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(PhoneProvisioning.QrJson(ssid, password), QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(12);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png);
        image.EndInit();
        image.Freeze();
        QrImage.Source = image;
        ResultPanel.Visibility = Visibility.Visible;
    }
}
