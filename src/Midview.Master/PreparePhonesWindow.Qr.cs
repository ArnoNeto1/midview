using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using Midview.Protocol;
using QRCoder;

namespace Midview.Master;

// The six taps page's QR: this phone's number and a one-use proof, stored
// (PendingPhoneClaimsFile) before it is shown, and sized to the room left.
public partial class PreparePhonesWindow
{
    // The QR as big as the page leaves room for, between these: at 1366x768
    // it still fits without scrolling, and stays large enough to be read.
    private const double QrLargest = 320;
    private const double QrSmallest = 220;

    // A proof this close to expiring is replaced instead of shown again.
    private static readonly TimeSpan FreshEnough = TimeSpan.FromMinutes(10);

    // The QR on show: for which number, until when its proof is valid.
    private (string Number, DateTimeOffset ExpiresAt, BitmapSource Image)? _qr;

    // The QR takes the height left below what is above it on the page.
    private void FitQr()
    {
        if (QrCard.Visibility != Visibility.Visible || !StepPage.IsVisible)
        {
            return;
        }
        // The card's border and padding, and the light margin around the code.
        const double around = 2 * (1 + 12) + 2 * 12;
        var top = QrCard.TranslatePoint(new Point(0, 0), PageContent).Y;
        // ActualHeight, not ViewportHeight: during SizeChanged the viewport is still the old one
        // (there is never a horizontal bar to take from it).
        var room = Math.Floor(PageScroll.ActualHeight - top - around - 2);
        var size = Math.Clamp(room, QrSmallest, QrLargest);
        QrImage.Width = size;
        QrImage.Height = size;
    }

    // The QR of this phone. A proof still fresh for the same number is shown
    // again (the phone may have scanned it already); otherwise a new one is
    // stored first, then the watcher is asked to look.
    private void ShowQr()
    {
        var number = _progress.Number!;
        QrText.Inlines.Clear();
        QrText.Inlines.Add(new Run("Este código já leva o Wi-Fi e o número "));
        QrText.Inlines.Add(new Bold(new Run(number)));
        QrText.Inlines.Add(new Run(". Vale por 1 hora, para um celular."));

        var now = DateTimeOffset.UtcNow;
        if (_progress.QrShownAt is not null && _qr is { } shown && shown.Number == number && shown.ExpiresAt - now > FreshEnough)
        {
            QrImage.Source = shown.Image;
            return;
        }
        if (PhoneWifiFile.Load() is not { } wifi)
        {
            return;
        }

        var token = PhoneClaim.NewToken();
        var expires = now + PhoneProvisioning.ClaimLifetime;
        QrWarningText.Text = "";
        try
        {
            PendingPhoneClaimsFile.Add(new PendingPhoneClaim(number, token, expires));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            QrWarningText.Text = "Este computador não conseguiu guardar o código, então o celular não vai entrar sozinho na turma. " +
                                 $"Quando ele terminar, use \"Procurar e parear\". ({ErrorText.Describe(ex)})";
        }
        var image = QrBitmap(PhoneProvisioning.QrJson(wifi.Ssid, wifi.Password, number, token, _masterThumbprint));
        _qr = (number, expires, image);
        QrImage.Source = image;
        _progress.QrShown(now);
        _kick();
    }

    // Level L: the code is read off a screen, where nothing smudges it, and
    // the lowest correction gives the fewest (so the biggest) squares.
    private static BitmapSource QrBitmap(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.L);
        var png = new PngByteQRCode(data).GetGraphic(12);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
