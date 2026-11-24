using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Midview.Protocol;

namespace Midview.Master;

// "Papel de parede e PIN da escola": asked once for each kind, right after
// the Wi-Fi (or opened with "Papel de parede e PIN" above the steps). What
// is saved here is the school's default (PhoneDefaultsFile), the same one
// "Pôr também nos próximos preparados" keeps in "Papel de parede" and
// "Senha (PIN)": put on each device prepared next as it joins the class.
// The third card, the phones' admin password, is in
// PreparePhonesWindow.AdminPassword.cs. "Pular" saves nothing and is not
// asked again.
public partial class PreparePhonesWindow
{
    // The picture on the page: the saved one, or one just chosen; null for none.
    private byte[]? _defaultsJpeg;

    private bool _defaultsBusy;

    private PhoneWallpaperScreens DefaultsScreens =>
        DefaultsBothRadio.IsChecked == true ? PhoneWallpaperScreens.Both
        : DefaultsHomeRadio.IsChecked == true ? PhoneWallpaperScreens.Home
        : PhoneWallpaperScreens.Lock;

    private string DefaultsPin => DefaultsShowPin.IsChecked == true ? DefaultsPinText.Text : DefaultsPinBox.Password;

    // Filled from what is saved each time the page opens.
    private void ShowDefaultsPage()
    {
        var kind = _progress.Kind;
        DefaultsTitle.Text = PhoneDefaultsText.Heading;
        DefaultsIntro.Text = PhoneDefaultsText.Intro(kind);
        DefaultsPinHint.Text = PhoneDefaultsText.PinHint;
        DefaultsLockRadio.Content = PhoneWallpaperText.Choice(PhoneWallpaperScreens.Lock);
        DefaultsBothRadio.Content = PhoneWallpaperText.Choice(PhoneWallpaperScreens.Both);
        DefaultsHomeRadio.Content = PhoneWallpaperText.Choice(PhoneWallpaperScreens.Home);
        DefaultsErrorText.Text = "";

        var saved = PhoneDefaultsFile.Load();
        var picture = saved.For(kind);
        _defaultsJpeg = picture?.Jpeg;
        var screens = picture?.Screens ?? PhoneWallpaperScreens.Lock;
        DefaultsLockRadio.IsChecked = screens == PhoneWallpaperScreens.Lock;
        DefaultsBothRadio.IsChecked = screens == PhoneWallpaperScreens.Both;
        DefaultsHomeRadio.IsChecked = screens == PhoneWallpaperScreens.Home;
        DefaultsPictureText.Text = picture is null ? PhoneDefaultsText.NoPicture : "A imagem guardada da escola.";
        ShowDefaultsPicture();

        DefaultsShowPin.IsChecked = false;
        DefaultsPinBox.Password = saved.Pin ?? "";
        DefaultsPinText.Text = saved.Pin ?? "";
        DefaultsPinBox.Visibility = Visibility.Visible;
        DefaultsPinText.Visibility = Visibility.Collapsed;

        ShowDefaultsAdmin(saved);

        Footer(back: true, next: PhoneDefaultsText.Save, other: PhoneDefaultsText.Skip);
    }

    private void ShowDefaultsPicture()
    {
        var has = _defaultsJpeg is { Length: > 0 };
        DefaultsLockRadio.IsEnabled = DefaultsBothRadio.IsEnabled = DefaultsHomeRadio.IsEnabled = has;
        DefaultsNoPictureLink.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        DefaultsChooseButton.IsEnabled = !_defaultsBusy;
        if (!has)
        {
            DefaultsPreview.Background = null;
            return;
        }
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelHeight = 200;
        image.StreamSource = new MemoryStream(_defaultsJpeg!, writable: false);
        image.EndInit();
        image.Freeze();
        DefaultsPreview.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
    }

    private async void OnDefaultsChooseClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher a imagem do papel de parede",
            Filter = PhoneWallpaperText.FileFilter,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        _defaultsBusy = true;
        DefaultsPictureText.Text = "Preparando a imagem...";
        ShowDefaultsPicture();
        try
        {
            var path = dialog.FileName;
            var picture = await Task.Run(() => PhoneWallpaperImage.Prepare(path));
            _defaultsJpeg = picture.Jpeg;
            DefaultsPictureText.Text = PhoneWallpaperText.Picture(Path.GetFileName(path), picture.Width, picture.Height, picture.Jpeg.Length);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            DefaultsPictureText.Text = ex switch
            {
                InvalidDataException => ex.Message,
                OutOfMemoryException => PhoneWallpaperText.TooManyPixels,
                _ => $"Não deu para abrir o arquivo: {ex.Message}",
            };
        }
        finally
        {
            _defaultsBusy = false;
            ShowDefaultsPicture();
        }
    }

    private void OnDefaultsNoPictureClicked(object sender, RoutedEventArgs e)
    {
        _defaultsJpeg = null;
        DefaultsPictureText.Text = PhoneDefaultsText.NoPicture;
        ShowDefaultsPicture();
    }

    // Digits only, as in "Senha (PIN)".
    private void OnDefaultsPinTyping(object sender, TextCompositionEventArgs e) =>
        e.Handled = PhonePinText.Digits(e.Text).Length != e.Text.Length;

    private void OnDefaultsPinChanged(object sender, RoutedEventArgs e) => DefaultsErrorText.Text = "";

    private void OnDefaultsShowPinClicked(object sender, RoutedEventArgs e)
    {
        var show = DefaultsShowPin.IsChecked == true;
        if (show)
        {
            DefaultsPinText.Text = DefaultsPinBox.Password;
        }
        else
        {
            DefaultsPinBox.Password = DefaultsPinText.Text;
        }
        DefaultsPinBox.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        DefaultsPinText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnChangeDefaultsClicked(object sender, RoutedEventArgs e)
    {
        _progress.ChangeDefaults();
        ShowPage();
    }

    // "Guardar e seguir": the picture for this kind, the PIN (4 digits, or
    // none) and the admin password (its hash) become the school's defaults.
    // False, said on the page, for a PIN that is not 4 digits, an admin
    // password not complete or not repeated the same, or a disk that refused.
    private bool SaveDefaults()
    {
        if (_defaultsBusy)
        {
            return false;
        }
        var pin = DefaultsPin;
        if (pin.Length > 0 && !PhonePinText.IsReady(pin))
        {
            DefaultsErrorText.Text = "O PIN tem 4 números. Digite os 4 ou deixe em branco para não pôr PIN.";
            return false;
        }
        if (DefaultsAdminProblem() is { } adminProblem)
        {
            DefaultsErrorText.Text = adminProblem;
            return false;
        }
        try
        {
            PhoneDefaultsFile.SaveWallpaper(
                _progress.Kind, _defaultsJpeg is { Length: > 0 } jpeg ? new PhoneDefaultsFile.Wallpaper(DefaultsScreens, jpeg) : null);
            PhoneDefaultsFile.SavePin(pin);
            SaveDefaultsAdmin();
            PhoneDefaultsFile.MarkAsked(_progress.Kind);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            DefaultsErrorText.Text = $"Não deu para guardar neste computador: {ex.Message}";
            return false;
        }
        DefaultsErrorText.Text = "";
        return true;
    }

    // "Pular": nothing saved, not asked again for this kind.
    private void SkipDefaults()
    {
        // A password typed and skipped is not kept on the page either.
        ClearDefaultsAdmin();
        try
        {
            PhoneDefaultsFile.MarkAsked(_progress.Kind);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Asked again next time; nothing else is lost.
        }
    }
}
