using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// Two cards of Configurar celular/tablet, one window. "Contas do aparelho":
// the school's devices keep their accounts locked; the teacher frees them on
// every device at once for 10, 30 or 60 minutes ("Liberar"), to put the
// school's Google account on, after which they lock again by themselves, or
// locks them now ("Travar agora"). "Sair das contas do Chrome" (clearChrome):
// Chrome's data erased on every device, after a question, since it cannot be
// undone. Each device's line says how it went (PhoneAccountsText).
public partial class PhoneAccountsWindow : MidviewWindow
{
    private readonly X509Certificate2 _masterCertificate;
    private readonly ObservableCollection<PhoneRow> _rows;
    private readonly bool _clearChrome;
    private bool _running;

    // allPhones: phones is every Android device in the classroom list.
    public PhoneAccountsWindow(IReadOnlyList<KnownMachine> phones, bool allPhones, X509Certificate2 masterCertificate, bool clearChrome)
    {
        InitializeComponent();
        _masterCertificate = masterCertificate;
        _clearChrome = clearChrome;
        Title = DialogHeading.Heading = PhoneAccountsText.Heading(
            clearChrome ? PhoneAccountsAction.ClearChrome : PhoneAccountsAction.Free, phones.Count);
        if (clearChrome)
        {
            DialogHeading.Icon = "";
        }
        TargetsTextBlock.Text = PhoneAppBlockText.Targets(phones.Select(phone => phone.Name).ToList(), allPhones);
        ExplanationTextBlock.Text = clearChrome ? PhoneAccountsText.ClearChromeExplanation : PhoneAccountsText.AccountsExplanation;
        MinutesPanel.Visibility = AccountsButtons.Visibility = clearChrome ? Visibility.Collapsed : Visibility.Visible;
        ClearChromeButton.Visibility = clearChrome ? Visibility.Visible : Visibility.Collapsed;
        FreeButton.IsDefault = !clearChrome;
        _rows = new ObservableCollection<PhoneRow>(phones.Select(phone => new PhoneRow(phone)));
        PhonesList.ItemsSource = _rows;
    }

    private int ChosenMinutes =>
        ThirtyMinutesRadioButton.IsChecked == true ? 30 : SixtyMinutesRadioButton.IsChecked == true ? 60 : PhoneNetworkFree.DefaultMinutes;

    private async void OnFreeClicked(object sender, RoutedEventArgs e) => await RunAsync(PhoneAccountsAction.Free, ChosenMinutes);

    private async void OnLockClicked(object sender, RoutedEventArgs e) => await RunAsync(PhoneAccountsAction.Lock, PhoneNetworkFree.LockNow);

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
            var answer = action == PhoneAccountsAction.ClearChrome
                ? PhoneAccountsClient.ClearChrome(phone, _masterCertificate)
                : PhoneAccountsClient.Free(phone, minutes, _masterCertificate);
            return (answer is { Succeeded: true } ? PhoneRowState.Done : PhoneRowState.Failed, PhoneAccountsText.Line(answer));
        }
        catch (Exception ex)
        {
            return (PhoneRowState.Failed, FailureText.DescribePhoneAgent(ex));
        }
    }
}
