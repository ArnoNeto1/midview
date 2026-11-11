using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Midview.Protocol;

namespace Midview.Master;

// "Preparar celulares": one step per page, so a teacher who knows nothing of
// phones prepares a school phone that then joins the class by itself. The
// pages and their order are PhoneSetupProgress; the texts and prints are
// PhoneSetupGuide. Showing the QR stores its one-use proof (PendingPhoneClaimsFile)
// and kicks the main window's watcher, which adds the phone when it appears,
// even with this window closed.
public partial class PreparePhonesWindow : MidviewWindow
{
    // The phone print in the step pages: a Redmi screen at this width, and
    // smaller beside the QR, which needs the room.
    private const double ScreenWidth = 160;
    private const double ScreenWidthBesideQr = 100;

    private readonly PhoneSetupProgress _progress = new();
    private readonly string _masterThumbprint;
    private readonly Func<int> _phonesInClass;
    private readonly Func<string, bool?> _phoneOnline;
    private readonly Action _kick;
    private readonly Func<string, Task<(bool Ok, string Detail)>>? _wipe;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(15) };

    // The lab Wi-Fi for this window: the saved one, or the one typed here
    // even when it could not be saved (the QR still carries it).
    private (string Ssid, string Password)? _wifi = PhoneWifiFile.Load();

    // phonesInClass: phones in the class now. phoneOnline: for a name, null
    // when the class has no phone with it, else whether it answers now.
    // kick: look for the phone right away. wipe: "Apagar e preparar de novo"
    // for a phone of the class by name; without it, a number already taken is
    // prepared again by the normal steps.
    internal PreparePhonesWindow(
        string masterThumbprint, Func<int> phonesInClass, Func<string, bool?> phoneOnline, Action kick,
        Func<string, Task<(bool Ok, string Detail)>>? wipe = null)
    {
        InitializeComponent();
        _masterThumbprint = masterThumbprint;
        _phonesInClass = phonesInClass;
        _phoneOnline = phoneOnline;
        _kick = kick;
        _wipe = wipe;
        _progress.Start(_wifi is not null);
        _clock.Tick += (_, _) => OnClockTick();
        _clock.Start();
        Closed += (_, _) => _clock.Stop();
        PageScroll.SizeChanged += (_, _) => FitQr();
        ShowPage();
    }

    // For a phone just wiped: straight to the six taps with its number (the
    // Wi-Fi first if none is saved yet). wiped: the page says it is being
    // wiped, as the Master already did it from the phone's menu.
    public void StartAt(string number, bool wiped = false)
    {
        _progress.StartAt(number, wiped);
        if (_wifi is null)
        {
            _progress.ChangeWifi();
        }
        ShowPage();
    }

    // The phone this window is waiting to see join the class, or null. Moving
    // the window to another phone (StartAt) would stop waiting for it.
    public string? WaitingForName =>
        _progress.Page is SetupPage.Waiting or SetupPage.NotAppeared ? _progress.Name : null;

    // MainWindow.PhoneJoined: a prepared phone entered the class.
    public void PhoneJoined(string name)
    {
        if (_progress.Joined(name))
        {
            ShowPage();
        }
    }

    private void OnClockTick()
    {
        var before = _progress.Page;
        _progress.Tick(DateTimeOffset.UtcNow);
        if (_progress.Page != before)
        {
            ShowPage();
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
            // A wipe that failed and the phone restored by hand: on to the steps and the QR.
            case SetupPage.Step when _progress.Wipe == WipeState.Failed:
                _progress.RestoredByHand();
                ShowPage();
                return;
            // Only past a QR for this very phone.
            case SetupPage.Step when _progress.StepId == "seis-toques" && _qr?.Shown.Number != _progress.Number:
                return;
            case SetupPage.Step when _progress.StepId == "numero":
                if (!TakeNumber())
                {
                    return;
                }
                ShowPage();
                return;
            case SetupPage.NotAppeared:
                _progress.Restart();
                ShowPage();
                return;
            case SetupPage.Ready:
                _progress.Again();
                ShowPage();
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

    // The footer's other button: "Terminei" on the ready page.
    private void OnOtherClicked(object sender, RoutedEventArgs e) => Close();

    private void OnNotAppearedNowClicked(object sender, RoutedEventArgs e)
    {
        _progress.NotAppearedNow();
        ShowPage();
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
        var typed = (Ssid: ssid, Password: PasswordInput.Text);
        if (_wifi == typed)
        {
            return true; // the same: the QR on show (and its token) stays
        }
        _wifi = typed; // a new QR is due (PhoneQrReuse compares the Wi-Fi)
        try
        {
            PhoneWifiFile.Save(typed.Ssid, typed.Password);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Not remembering the Wi-Fi is not worth refusing the code: this
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
        if (!PhoneTotalFile.Asked())
        {
            var typed = TotalInput.Text.Trim();
            var total = PhoneTotalFile.Parse(typed);
            if (typed.Length > 0 && total is null)
            {
                NumberError("No total, digite quantos celulares (de 1 a 999) ou deixe em branco.");
                return false;
            }
            try
            {
                PhoneTotalFile.Save(total);
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
        if (_progress.StepId == "seis-toques" && _wifi is null)
        {
            _progress.ChangeWifi(); // no QR without the Wi-Fi it carries
        }
        var page = _progress.Page;
        var step = _progress.Step;
        WifiPage.Visibility = Shown(page == SetupPage.Wifi);
        StepPage.Visibility = Shown(page == SetupPage.Step);
        ConfirmRedoPage.Visibility = Shown(page == SetupPage.ConfirmRedo);
        WaitingPage.Visibility = Shown(page == SetupPage.Waiting);
        NotAppearedPage.Visibility = Shown(page == SetupPage.NotAppeared);
        ReadyPage.Visibility = Shown(page == SetupPage.Ready);

        var wifi = _wifi;
        WifiNameRun.Text = wifi?.Ssid ?? "";
        // Also on "ainda não apareceu": its checks send the teacher to the Wi-Fi.
        WifiLine.Visibility = Shown((page is SetupPage.Step or SetupPage.NotAppeared) && wifi is not null);
        CaptionText.Text = page switch
        {
            SetupPage.Step => _progress.Name is { } name && _progress.StepId != "numero"
                ? $"{_progress.StepCaption} · {name}"
                : _progress.StepCaption,
            SetupPage.Wifi => _progress.ReturnsAfterWifi ? "Trocar o Wi-Fi" : "Antes de começar",
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
            case SetupPage.Step:
                ShowStep(step!);
                break;
            case SetupPage.ConfirmRedo:
                ConfirmRedoTitle.Text = _progress.ConfirmRedoText;
                ConfirmRedoHint.Text = _phoneOnline(_progress.Name!) == true && _wipe is not null
                    ? $"O {_progress.Name} está ligado agora: no próximo passo, o Midview pode apagá-lo daqui, sem mexer nos menus do celular."
                    : $"Siga os passos com o celular na mão. O {_progress.Name} antigo sai da turma quando este aparecer. " +
                      "Um celular que já era da escola não deixa restaurar pelo menu das Configurações: no passo de restaurar, use \"Restaurar sem o PIN\" (veja em \"Deu errado?\").";
                Footer(back: true, next: $"Sim, preparar de novo o {_progress.Number}", backText: "Não, trocar o número");
                break;
            case SetupPage.Waiting:
                WaitingTitle.Text = $"Esperando o {_progress.Name} terminar…";
                WaitingJoinText.Text = $"Quando terminar, ele entra sozinho na turma com o nome \"{_progress.Name}\" e esta tela muda. " +
                                       "Pode fechar esta janela: o Midview continua esperando.";
                Footer(back: true, next: null);
                break;
            case SetupPage.NotAppeared:
                NotAppearedTitle.Text = $"O {_progress.Name} ainda não apareceu";
                FillNumbered(NotAppearedList, PhoneSetupGuide.NotAppeared);
                Footer(back: true, next: "Recomeçar este celular", primary: false);
                break;
            case SetupPage.Ready:
                ReadyTitle.Text = _progress.ReadyText(_phonesInClass(), PhoneTotalFile.Load());
                ReadyHint.Text = $"Ele já está na turma, no filtro Celulares. Na tela do celular aparece \"Pronto. Este é o {_progress.Name} da escola.\"";
                Footer(back: false, next: "Preparar o próximo", other: "Terminei");
                break;
        }

        _helpOpen = false;
        ShowHelp();
        PageScroll.ScrollToTop();
        Dispatcher.BeginInvoke(FitQr, DispatcherPriority.Loaded);
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
            TotalInputs.Visibility = Shown(!PhoneTotalFile.Asked());
            Dispatcher.BeginInvoke(() => NumberInput.Focus(), DispatcherPriority.Input);
        }

        var isQr = step.Id == "seis-toques";
        ShowPrint(step, isQr ? ScreenWidthBesideQr : ScreenWidth);

        QrCard.Visibility = Shown(isQr);
        if (isQr)
        {
            ShowQr(_wifi!.Value);
        }
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
        OtherButton.Visibility = Shown(other is not null);
        OtherButton.Content = other ?? "";
    }

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;
}
