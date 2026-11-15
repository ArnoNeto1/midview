using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// Three cards of Configurar celular/tablet, one window. "Contas do
// aparelho": the school's devices keep their accounts locked; the teacher
// frees them on every device at once for 10, 30 or 60 minutes ("Liberar"),
// to put the school's Google account on, after which they lock again by
// themselves, or locks them now ("Travar agora"). "Depuração USB" the same
// way: USB debugging freed and turned on, to check a device over the cable,
// then off and locked again (with a warning of what a computer on the cable
// may ask meanwhile). "Sair das contas do Chrome": Chrome's data erased on
// every device, after a question, since it cannot be undone. Each device's
// line says how it went (PhoneAccountsText).
public partial class PhoneAccountsWindow : MidviewWindow
{
    private readonly X509Certificate2 _masterCertificate;
    private readonly ObservableCollection<PhoneRow> _rows;
    private readonly bool _clearChrome;
    private readonly bool _debugging;
    private bool _running;

    // allPhones: phones is every Android device in the classroom list.
    public PhoneAccountsWindow(IReadOnlyList<KnownMachine> phones, bool allPhones, X509Certificate2 masterCertificate, PhoneAccountsCard card)
    {
        InitializeComponent();
        _masterCertificate = masterCertificate;
        _clearChrome = card == PhoneAccountsCard.ClearChrome;
        _debugging = card == PhoneAccountsCard.Debugging;
        Title = DialogHeading.Heading = PhoneAccountsText.Heading(FreeAction, phones.Count);
        if (_clearChrome)
        {
            DialogHeading.Icon = "";
        }
        else if (_debugging)
        {
            // Segoe MDL2 Assets' USB.
            DialogHeading.Icon = "";
            WarningTextBlock.Text = PhoneAccountsText.DebuggingWarning(DeviceSetupText.Word(PhoneUpdateText.KindOf(phones)));
            WarningTextBlock.Visibility = Visibility.Visible;
        }
        TargetsTextBlock.Text = PhoneAppBlockText.Targets(phones.Select(phone => phone.Name).ToList(), allPhones);
        ExplanationTextBlock.Text = _clearChrome
            ? PhoneAccountsText.ClearChromeExplanation
            : _debugging ? PhoneAccountsText.DebuggingExplanation : PhoneAccountsText.AccountsExplanation;
        MinutesPanel.Visibility = AccountsButtons.Visibility = _clearChrome ? Visibility.Collapsed : Visibility.Visible;
        ClearChromeButton.Visibility = _clearChrome ? Visibility.Visible : Visibility.Collapsed;
        FreeButton.IsDefault = !_clearChrome;
        _rows = new ObservableCollection<PhoneRow>(phones.Select(phone => new PhoneRow(phone)));
        PhonesList.ItemsSource = _rows;
    }

    private int ChosenMinutes =>
        ThirtyMinutesRadioButton.IsChecked == true ? 30 : SixtyMinutesRadioButton.IsChecked == true ? 60 : PhoneNetworkFree.DefaultMinutes;

    private PhoneAccountsAction FreeAction =>
        _clearChrome ? PhoneAccountsAction.ClearChrome : _debugging ? PhoneAccountsAction.FreeDebugging : PhoneAccountsAction.Free;

    private PhoneAccountsAction LockAction => _debugging ? PhoneAccountsAction.LockDebugging : PhoneAccountsAction.Lock;

    // Freeing USB debugging opens the devices to a computer on the cable: asked first.
    private async void OnFreeClicked(object sender, RoutedEventArgs e)
    {
        if (_debugging && !_running && MidviewMessage.Show(
                this, PhoneAccountsText.FreeDebuggingConfirm(_rows.Count, ChosenMinutes), Title,
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        await RunAsync(FreeAction, ChosenMinutes);
    }

    private async void OnLockClicked(object sender, RoutedEventArgs e) => await RunAsync(LockAction, PhoneNetworkFree.LockNow);

    // It cannot be undone: asked first.
    private async void OnClearChromeClicked(object sender, RoutedEventArgs e)
    {
        if (!_clearChrome || _running)
        {
            return;
        }
        var answer = MidviewMessage.Show(
            this, PhoneAccountsText.ClearChromeConfirm(_rows.Count), Title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Yes)
        {
            await RunAsync(PhoneAccountsAction.ClearChrome, 0);
        }
    }

    private async Task RunAsync(PhoneAccountsAction action, int minutes)
    {
        if (_running)
        {
            return;
        }

        _running = true;
        FreeButton.IsEnabled = LockButton.IsEnabled = ClearChromeButton.IsEnabled = false;
        ProgressTextBlock.Text = PhoneAccountsText.Progress(action, _rows.Count);
        foreach (var row in _rows)
        {
            row.Show(PhoneRowState.Working, PhoneAccountsText.Working(action));
        }

        try
        {
            var done = await Task.WhenAll(_rows.Select(row => Task.Run(() => SendOne(row, action, minutes))));
            ProgressTextBlock.Text = PhoneAccountsText.Summary(action, minutes, done.Count(ok => ok), _rows.Count);
        }
        finally
        {
            _running = false;
            FreeButton.IsEnabled = LockButton.IsEnabled = ClearChromeButton.IsEnabled = true;
        }
    }

    // Off the UI thread; the row is updated on it. True when the device did it.
    private bool SendOne(PhoneRow row, PhoneAccountsAction action, int minutes)
    {
        var (state, line) = Send(row.Phone, action, minutes);
        Dispatcher.Invoke(() => row.Show(state, line));
        return state == PhoneRowState.Done;
    }

    private (PhoneRowState State, string Line) Send(KnownMachine phone, PhoneAccountsAction action, int minutes)
    {
        if (phone.AgentThumbprint is not { Length: > 0 })
        {
            return (PhoneRowState.Failed, PhoneAccountsText.NoIdentity);
        }
        try
        {
            var answer = action switch
            {
                PhoneAccountsAction.ClearChrome => PhoneAccountsClient.ClearChrome(phone, _masterCertificate),
                PhoneAccountsAction.FreeDebugging or PhoneAccountsAction.LockDebugging =>
                    PhoneAccountsClient.FreeDebugging(phone, minutes, _masterCertificate),
                _ => PhoneAccountsClient.Free(phone, minutes, _masterCertificate),
            };
            return (answer is { Succeeded: true } ? PhoneRowState.Done : PhoneRowState.Failed, PhoneAccountsText.Line(answer));
        }
        catch (Exception ex)
        {
            return (PhoneRowState.Failed, FailureText.DescribePhoneAgent(ex));
        }
    }
}
