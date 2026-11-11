using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Midview.Protocol;
using QRCoder;

namespace Midview.Master;

// The six taps page's QR: this phone's number and a one-use proof, stored
// (PendingPhoneClaimsFile) before it is shown, and sized to the room left.
public partial class PreparePhonesWindow
{
    // The QR as big as the page leaves room for, up to this; each square a
    // whole number of screen pixels (at least 2), so all squares are even.
    private const double QrLargest = 330;
    private const int QrSmallestSquare = 2;

    // The QR on show and its image (one pixel per square, white border included).
    private (ShownQr Shown, BitmapSource Image)? _qr;

    // The QR takes the height left below what is above it on the page.
    private void FitQr()
    {
        if (QrCard.Visibility != Visibility.Visible || !StepPage.IsVisible || QrImage.Source is not BitmapSource image)
        {
            return;
        }
        // The card's border and padding, and the rounded white frame around the code.
        const double around = 2 * (1 + 12) + 2 * 4;
        var top = QrCard.TranslatePoint(new Point(0, 0), PageContent).Y;
        // ActualHeight, not ViewportHeight: during SizeChanged the viewport is still the old one
        // (there is never a horizontal bar to take from it).
        var room = Math.Min(PageScroll.ActualHeight - top - around - 2, QrLargest);
        var scale = VisualTreeHelper.GetDpi(QrImage).DpiScaleY;
        var squares = image.PixelWidth;
        var square = Math.Max(QrSmallestSquare, (int)Math.Floor(room * scale / squares));
        var size = square * squares / scale;
        QrImage.Width = size;
        QrImage.Height = size;
    }

    // The QR of this phone. One still fresh for the same number and Wi-Fi is
    // shown again (PhoneQrReuse: the phone may have read it already); otherwise
    // a new token is stored first, then the watcher is asked to look.
    // The Wi-Fi is known here: ShowPage goes to the Wi-Fi page without one.
    private void ShowQr((string Ssid, string Password) wifi)
    {
        var number = _progress.Number!;
        QrText.Inlines.Clear();
        QrText.Inlines.Add(new Run("Este código já leva o Wi-Fi e o número "));
        QrText.Inlines.Add(new Bold(new Run(number)));
        QrText.Inlines.Add(new Run(". Vale por 1 hora, para um celular."));

        var now = DateTimeOffset.UtcNow;
        if (_progress.QrShownAt is not null && _qr is { } current
            && PhoneQrReuse.Reuse(current.Shown, number, wifi.Ssid, wifi.Password, now))
        {
            QrImage.Source = current.Image;
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
        _qr = (new ShownQr(number, wifi.Ssid, wifi.Password, token, expires), image);
        QrImage.Source = image;
        _progress.QrShown(now);
        _kick();
    }

    // Level L: the code is read off a screen, where nothing smudges it, and
    // the lowest correction gives the fewest (so the biggest) squares. One
    // pixel per square: FitQr enlarges it by a whole number.
    private static BitmapSource QrBitmap(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.L);
        var png = new PngByteQRCode(data).GetGraphic(1);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
