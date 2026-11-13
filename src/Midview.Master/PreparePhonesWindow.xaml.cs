using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Midview.Master;

// What the assistant needs from the main window. DevicesInClass: phones (or
// tablets) in the class now. PhoneOnline: for a name, null when the class
// has no phone with it, else whether it answers now. Adb: the cable tool, made when the
// cable step first needs it. GetApp: the phone app, downloaded and checked.
// AddPhone: puts a phone prepared over the cable in the class (and saves),
// and the main window searches the network for it for 5 minutes, open
// assistant or not. IsOnline: whether that phone answers now. WatchAgain:
// "Procurar de novo", those 5 minutes again. Wipe:
// "Apagar e preparar de novo" for a phone of the class by name; without it,
// a number already taken is prepared again by the normal steps.
internal sealed record PreparePhonesHost(
    Func<AndroidDeviceKind, int> DevicesInClass,
    Func<string, bool?> PhoneOnline,
    Func<IAdb> Adb,
    Func<CancellationToken, Task<(AgentApkResult Result, string ApkPath)>> GetApp,
    string MasterThumbprint,
    Action<KnownMachine> AddPhone,
    Func<KnownMachine, bool> IsOnline,
    Action<KnownMachine> WatchAgain,
    Func<string, Task<(bool Ok, string Detail)>>? Wipe = null);

// "Preparar celulares": one step per page, so a teacher who knows nothing of
// phones prepares a school phone; the Midview then finishes it over the USB
// cable and it joins the class by itself. The pages and their order are
// PhoneSetupProgress; the texts and prints are PhoneSetupGuide.
public partial class PreparePhonesWindow : MidviewWindow
{
    // The phone print in the step pages: a Redmi screen at this width.
    private const double ScreenWidth = 160;

    private readonly PhoneSetupProgress _progress = new();
    private readonly PreparePhonesHost _host;
    private readonly Func<AndroidDeviceKind, int> _devicesInClass;
    private readonly Func<string, bool?> _phoneOnline;
    private readonly Func<string, Task<(bool Ok, string Detail)>>? _wipe;

    // The lab Wi-Fi for this window: the saved one, or the one typed here
    // even when it could not be saved.
    private (string Ssid, string Password)? _wifi = PhoneWifiFile.Load();

    internal PreparePhonesWindow(
        PreparePhonesHost host, AndroidDeviceKind kind = AndroidDeviceKind.Phone, bool optimize = true)
    {
        InitializeComponent();
        _progress.Kind = kind;
        _optimize = optimize;
        ShowMode();
        _host = host;
        _devicesInClass = host.DevicesInClass;
        _phoneOnline = host.PhoneOnline;
        _wipe = host.Wipe;
        // "Antes de começar, separe:" only before the first phone or tablet
        // of the class: the same things are needed for both.
        _progress.Start(_wifi is not null, gathered:
            _devicesInClass(AndroidDeviceKind.Phone) + _devicesInClass(AndroidDeviceKind.Tablet) > 0
            || PhoneTotalFile.Asked() || PhoneTotalFile.Asked(kind: AndroidDeviceKind.Tablet));
        InitializeCable();
        ShowPage();
    }

    // For a phone just wiped: straight to turning it on, "Apareceu Bem-vindo"
    // open (the Wi-Fi first if none is saved yet). wiped: the page says it is
    // being wiped, as the Master already did it from the phone's menu. kind:
    // the device wiped ("Celular 07" or "Tablet 07"), so the window turns to
    // it whatever it was on; "Midview otimizado" stays what the window had
    // (on for a window opened for this).
    public void StartAt(string number, AndroidDeviceKind kind = AndroidDeviceKind.Phone, bool wiped = false)
    {
        _progress.Kind = kind;
        ShowMode();
        _progress.StartAt(number, wiped);
        if (_wifi is null)
        {
            _progress.ChangeWifi();
        }
        ShowPage();
        if (wiped && _progress.Name is { } name)
        {
            WatchWipe(name);
        }
    }

    private void OnNextClicked(object sender, RoutedEventArgs e)
    {
        switch (_progress.Page)
        {
            case SetupPage.Wifi:
                if (!SaveWifi())
                {
                    return;
                }
                break;
            // A wipe that failed and the phone restored by hand: on to the steps.
            case SetupPage.Step when _progress.Wipe == WipeState.Failed:
                _progress.RestoredByHand();
                ShowPage();
                return;
            case SetupPage.Step when _progress.StepId == "numero":
                if (!TakeNumber())
                {
                    return;
                }
                ShowPage();
                return;
            // "Liguei o cabo e toquei em Permitir": look at the cable now.
            case SetupPage.Step when _progress.StepId == "cabo":
                CheckCableNow();
                return;
            // "Tentar de novo": the list again, from the start.
            case SetupPage.CableFailed:
                _progress.Next();
                ShowPage();
                Prepare();
                return;
            // Nothing to click while the Midview finishes the phone.
            case SetupPage.Cable:
                return;
        }
        _progress.Next();
        ShowPage();
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        _progress.Back();
        ShowPage();
    }

    // The footer's other button: "Terminei" on the ready pages, "Preparar
    // outro celular" when no step fixes what the cable found.
    private void OnOtherClicked(object sender, RoutedEventArgs e)
    {
        if (_progress.Page == SetupPage.CableFailed)
        {
            _progress.Again();
            ShowPage();
            return;
        }
        Close();
    }

    private void OnChangeWifiClicked(object sender, RoutedEventArgs e)
    {
        _progress.ChangeWifi();
        ShowPage();
    }

    // Same rules as before the assistant: a name, and a password of 8 or more.
    private bool SaveWifi()
    {
        var ssid = SsidInput.Text.Trim();
        if (ssid.Length == 0)
        {
            WifiErrorText.Text = "Digite o nome do Wi-Fi do laboratório.";
            return false;
        }
        if (PasswordInput.Text.Length < 8)
        {
            WifiErrorText.Text = "A senha do Wi-Fi tem pelo menos 8 caracteres.";
            return false;
        }
        WifiErrorText.Text = "";
        _wifi = (ssid, PasswordInput.Text);
        try
        {
            PhoneWifiFile.Save(ssid, PasswordInput.Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Not remembering the Wi-Fi is not worth refusing it: this
            // window keeps it, and the next one asks again.
        }
        return true;
    }

    // Next on the number page: a valid number, the school's total the first
    // time, and "Já existe um Celular 07" when the class has it already.
    private bool TakeNumber()
    {
        if (!_progress.SetNumber(NumberInput.Text))
        {
            NumberError("Digite só o número escrito atrás do celular, com 1 a 3 algarismos (por exemplo, 7).");
            return false;
        }
        NumberError("");
        if (!PhoneTotalFile.Asked(kind: _progress.Kind))
        {
            var typed = TotalInput.Text.Trim();
            var total = PhoneTotalFile.Parse(typed);
            if (typed.Length > 0 && total is null)
            {
                NumberError($"No total, digite quantos {DeviceSetupText.Words(_progress.Kind)} (de 1 a 999) ou deixe em branco.");
                return false;
            }
            try
            {
                PhoneTotalFile.Save(total, kind: _progress.Kind);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Only the "de 11" of the ready page is lost; asked again next time.
            }
        }
        if (_phoneOnline(_progress.Name!) is { } online)
        {
            _progress.NumberTaken(online && _wipe is not null);
        }
        else
        {
            _progress.Next();
        }
        return true;
    }

    private void NumberError(string text)
    {
        NumberErrorText.Text = text;
        NumberErrorText.Visibility = Shown(text.Length > 0);
    }

    // Draws the page PhoneSetupProgress is on.
    private void ShowPage()
    {
        var page = _progress.Page;
        WifiPage.Visibility = Shown(page == SetupPage.Wifi);
        GatherPage.Visibility = Shown(page == SetupPage.Gather);
        StepPage.Visibility = Shown(page == SetupPage.Step);
        ConfirmRedoPage.Visibility = Shown(page == SetupPage.ConfirmRedo);
        CablePage.Visibility = Shown(page == SetupPage.Cable);
        CableFailedPage.Visibility = Shown(page == SetupPage.CableFailed);
        ReadyPage.Visibility = Shown(page == SetupPage.Ready);
        NotAppearedPage.Visibility = Shown(page == SetupPage.NotAppeared);

        var wifi = _wifi;
        WifiNameRun.Text = wifi?.Ssid ?? "";
        WifiLine.Visibility = Shown(page == SetupPage.Step && wifi is not null);
        CaptionText.Text = page switch
        {
            SetupPage.Step => _progress.Name is { } name && _progress.StepId != "numero"
                ? $"{_progress.StepCaption} · {name}"
                : _progress.StepCaption,
            SetupPage.Wifi => _progress.ReturnsAfterWifi ? "Trocar o Wi-Fi" : "Antes de começar",
            SetupPage.Gather => "Antes de começar",
            SetupPage.Cable or SetupPage.CableFailed => $"{_progress.Name} · no cabo",
            _ => _progress.Name ?? "",
        };

        switch (page)
        {
            case SetupPage.Wifi:
                if (wifi is { } saved && SsidInput.Text.Length == 0)
                {
                    SsidInput.Text = saved.Ssid;
                    PasswordInput.Text = saved.Password;
                }
                Footer(back: _progress.ReturnsAfterWifi, next: "Guardar e continuar");
                break;
            case SetupPage.Gather:
                ShowGather();
                break;
            case SetupPage.Step:
                ShowStep(_progress.Step!);
                break;
            case SetupPage.ConfirmRedo:
                ConfirmRedoTitle.Text = _progress.ConfirmRedoText;
                ConfirmRedoHint.Text = _phoneOnline(_progress.Name!) == true && _wipe is not null
                    ? $"O {_progress.Name} está ligado agora: no próximo passo, o Midview pode apagá-lo daqui, sem mexer nos menus do celular."
                    : $"Siga os passos com o {DeviceWord} na mão. O {_progress.Name} antigo é trocado por este quando este ficar pronto. " +
                      $"Se for o mesmo {DeviceWord}, ele já é da escola e precisa ser apagado antes; como não está ligado agora, " +
                      "apague-o pelo modo de recuperação (veja \"Celular travado com PIN\" em \"Deu errado?\").";
                Footer(back: true, next: $"Sim, preparar de novo o {_progress.Number}", backText: "Não, trocar o número");
                break;
            case SetupPage.Cable:
            case SetupPage.CableFailed:
            case SetupPage.Ready:
            case SetupPage.NotAppeared:
                ShowCablePage();
                break;
        }

        ShowApp();
        // "Apareceu Bem-vindo" right after a wipe: open under the steps, the
        // page kept at its top so the wipe's card stays in sight.
        _helpOpen = _progress.OpensWelcomeHelp;
        ShowHelp();
        PageScroll.ScrollToTop();
        UpdateCableWatch();
    }

    private void ShowGather()
    {
        var gather = PhoneSetupGuide.Gather;
        GatherTitle.Text = gather.Title;
        GatherList.Children.Clear();
        for (var i = 0; i < gather.Items.Count; i++)
        {
            GatherList.Children.Add(NumberedRow(i + 1, new TextBlock { Text = gather.Items[i], TextWrapping = TextWrapping.Wrap, FontSize = 13 }));
        }
        Footer(back: false, next: "Já separei tudo, começar");
    }

    private void ShowStep(GuideStep step)
    {
        StepTitle.Text = step.Title;
        FillTaps(step.Taps);
        TimingText.Text = step.Timing ?? "";
        TimingText.Visibility = Shown(step.Timing is not null);

        var isNumber = step.Id == "numero";
        NumberInputs.Visibility = Shown(isNumber);
        if (isNumber)
        {
            NumberInput.Text = _progress.Number ?? "";
            NumberError("");
            TotalInputs.Visibility = Shown(!PhoneTotalFile.Asked(kind: _progress.Kind));
            Dispatcher.BeginInvoke(() => NumberInput.Focus(), DispatcherPriority.Input);
        }
        CableWaitCard.Visibility = Shown(step.Id == "cabo");
        if (step.Id == "cabo")
        {
            CableWaitText.Text = _cableWait;
        }

        ShowPrint(step, ScreenWidth);
        ShowWipe();
    }

    // The phone print at a fixed width, the outline drawn over its Target.
    private void ShowPrint(GuideStep step, double width)
    {
        if (step.Image is null)
        {
            PhoneFrame.Visibility = Visibility.Collapsed;
            return;
        }
        var image = new BitmapImage(new Uri("pack://application:,,,/Midview.Master;component/" + step.Image, UriKind.Absolute));
        var height = width * image.PixelHeight / image.PixelWidth;
        StepImage.Source = image;
        StepImage.Width = width;
        StepImage.Height = height;
        PhoneScreen.Clip = new RectangleGeometry(new Rect(0, 0, width, height), 15, 15);
        PhoneFrame.Visibility = Visibility.Visible;

        if (step.Target is { } target)
        {
            const double glow = 4;
            Canvas.SetLeft(TargetGlow, target.X * width - glow);
            Canvas.SetTop(TargetGlow, target.Y * height - glow);
            TargetGlow.Width = target.Width * width + 2 * glow;
            TargetGlow.Height = target.Height * height + 2 * glow;
            TargetGlow.Visibility = Visibility.Visible;
        }
        else
        {
            TargetGlow.Visibility = Visibility.Collapsed;
        }
    }

    private void Footer(bool back, string? next, string? other = null, string backText = "Voltar", bool primary = true)
    {
        BackButton.Visibility = Shown(back);
        BackButton.Content = backText;
        NextButton.Visibility = Shown(next is not null);
        NextButton.Content = next ?? "";
        NextButton.Style = (Style)FindResource(primary ? "PrimaryButton" : typeof(Button));
        NextButton.IsDefault = primary;
        NextButton.IsEnabled = true;
        OtherButton.Visibility = Shown(other is not null);
        OtherButton.Content = other ?? "";
    }

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;
}
