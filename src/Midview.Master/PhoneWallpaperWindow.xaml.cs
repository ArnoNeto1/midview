using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Midview.Protocol;

namespace Midview.Master;

// "Papel de parede dos celulares e tablets" (Configurar celular/tablet): the teacher picks
// a JPG or PNG, sees it in a phone's shape, picks the screens (both, the
// home screen or the lock screen) and "Aplicar" puts it on every device at
// once, as the Device Owner (no "Temas" app, whose terms Xiaomi asks the
// student to accept). The picture goes as PhoneWallpaperImage makes it: no
// bigger than a phone's screen, a JPEG. "Voltar ao padrão do celular" puts
// each device's own wallpaper back on both screens. Each device's line says
// how it went.
public partial class PhoneWallpaperWindow : MidviewWindow
{
    private readonly X509Certificate2 _masterCertificate;
    private readonly ObservableCollection<PhoneRow> _rows;
    private PhoneWallpaperImage.Prepared? _picture;
    private bool _running;

    // allPhones: phones is every Android device in the classroom list.
    public PhoneWallpaperWindow(IReadOnlyList<KnownMachine> phones, bool allPhones, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _masterCertificate = masterCertificate;
        Title = DialogHeading.Heading = PhoneWallpaperText.Heading(phones.Count);
        TargetsTextBlock.Text = PhoneAppBlockText.Targets(phones.Select(phone => phone.Name).ToList(), allPhones);
        BothRadioButton.Content = PhoneWallpaperText.Choice(PhoneWallpaperScreens.Both);
        HomeRadioButton.Content = PhoneWallpaperText.Choice(PhoneWallpaperScreens.Home);
        LockRadioButton.Content = PhoneWallpaperText.Choice(PhoneWallpaperScreens.Lock);
        PictureTextBlock.Text = PhoneWallpaperText.NothingChosen;
        _rows = new ObservableCollection<PhoneRow>(phones.Select(phone => new PhoneRow(phone)));
        PhonesList.ItemsSource = _rows;
        ShowState();
    }

    private PhoneWallpaperScreens ChosenScreens =>
        HomeRadioButton.IsChecked == true ? PhoneWallpaperScreens.Home
        : LockRadioButton.IsChecked == true ? PhoneWallpaperScreens.Lock
        : PhoneWallpaperScreens.Both;

    private async void OnChooseClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher a imagem do papel de parede",
            Filter = PhoneWallpaperText.FileFilter,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            await ChooseAsync(dialog.FileName);
        }
    }

    // The picture made ready off the UI thread (a big photo takes a moment).
    internal async Task ChooseAsync(string path)
    {
        _running = true;
        ShowState();
        PictureTextBlock.Text = "Preparando a imagem...";
        try
        {
            var picture = await Task.Run(() => PhoneWallpaperImage.Prepare(path));
            _picture = picture;
            PictureTextBlock.Text = PhoneWallpaperText.Picture(Path.GetFileName(path), picture.Width, picture.Height, picture.Jpeg.Length);
            ShowPreview(picture.Jpeg);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _picture = null;
            PictureTextBlock.Text = ex is InvalidDataException ? ex.Message : $"Não deu para abrir o arquivo: {ex.Message}";
            ShowPreview(null);
        }
        finally
        {
            _running = false;
            ShowState();
        }
    }

    private void ShowPreview(byte[]? jpeg)
    {
        if (jpeg is null)
        {
            PreviewScreen.Background = null;
            PreviewEmptyTextBlock.Visibility = Visibility.Visible;
            return;
        }
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelHeight = 600;
        image.StreamSource = new MemoryStream(jpeg, writable: false);
        image.EndInit();
        image.Freeze();
        PreviewScreen.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        PreviewEmptyTextBlock.Visibility = Visibility.Collapsed;
    }

    private void ShowState()
    {
        ChooseButton.IsEnabled = RestoreButton.IsEnabled = !_running;
        ApplyButton.IsEnabled = !_running && _picture is not null;
    }

    private async void OnApplyClicked(object sender, RoutedEventArgs e)
    {
        if (_picture is { } picture)
        {
            await RunAsync(ChosenScreens, picture.Jpeg);
        }
    }

    private async void OnRestoreClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(PhoneWallpaperScreens.Both, Array.Empty<byte>());

    private async Task RunAsync(PhoneWallpaperScreens screens, byte[] image)
    {
        if (_running)
        {
            return;
        }

        var restore = image.Length == 0;
        _running = true;
        ShowState();
        ProgressTextBlock.Text = PhoneWallpaperText.Progress(restore, _rows.Count);
        foreach (var row in _rows)
        {
            row.Show(PhoneRowState.Working, PhoneWallpaperText.Working(restore));
        }

        try
        {
            var done = await Task.WhenAll(_rows.Select(row => Task.Run(() => SendOne(row, screens, image))));
            ProgressTextBlock.Text = PhoneWallpaperText.Summary(restore, done.Count(ok => ok), _rows.Count);
        }
        finally
        {
            _running = false;
            ShowState();
        }
    }

    // Off the UI thread; the row is updated on it. True when the device did it.
    private bool SendOne(PhoneRow row, PhoneWallpaperScreens screens, byte[] image)
    {
        var (state, line) = Send(row.Phone, screens, image);
        Dispatcher.Invoke(() => row.Show(state, line));
        return state == PhoneRowState.Done;
    }

    private (PhoneRowState State, string Line) Send(KnownMachine phone, PhoneWallpaperScreens screens, byte[] image)
    {
        if (phone.AgentThumbprint is not { Length: > 0 })
        {
            return (PhoneRowState.Failed, PhoneWallpaperText.NoIdentity);
        }
        try
        {
            var answer = PhoneWallpaperClient.Send(phone, screens, image, _masterCertificate);
            return (answer is { Succeeded: true } ? PhoneRowState.Done : PhoneRowState.Failed, PhoneWallpaperText.Line(answer));
        }
        catch (Exception ex)
        {
            return (PhoneRowState.Failed, FailureText.DescribePhoneAgent(ex));
        }
    }
}
