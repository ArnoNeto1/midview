using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Midview.Protocol;

namespace Midview.Master;

// "Liberar Wi-Fi e modo avião" (Configurar celular/tablet): the school's phones keep their
// Wi-Fi and airplane mode locked; the teacher frees them on every device at
// once for 10, 30 or 60 minutes ("Liberar"), after which they lock again by
// themselves, or locks them now ("Travar agora"). Below, the phones' admin
// password, with which the same is done on the phone itself: only its salted
// hash is sent, and nothing here keeps the password. Each device's line says
// how it went (PhoneNetworkText).
public partial class PhoneNetworkWindow : MidviewWindow
{
    private readonly X509Certificate2 _masterCertificate;
    private readonly ObservableCollection<PhoneRow> _rows;
    private bool _running;
    private bool _syncing;

    // allPhones: phones is every Android device in the classroom list.
    public PhoneNetworkWindow(IReadOnlyList<KnownMachine> phones, bool allPhones, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _masterCertificate = masterCertificate;
        Title = DialogHeading.Heading = PhoneNetworkText.Heading(phones.Count);
        TargetsTextBlock.Text = PhoneAppBlockText.Targets(phones.Select(phone => phone.Name).ToList(), allPhones);
        _rows = new ObservableCollection<PhoneRow>(phones.Select(phone => new PhoneRow(phone)));
        PhonesList.ItemsSource = _rows;
        DataObject.AddPastingHandler(PasswordBox, OnPasswordPasting);
        DataObject.AddPastingHandler(PasswordTextBox, OnPasswordPasting);
        ShowState();
    }

    private string Password => ShowPasswordCheckBox.IsChecked == true ? PasswordTextBox.Text : PasswordBox.Password;

    private int ChosenMinutes =>
        ThirtyMinutesRadioButton.IsChecked == true ? 30 : SixtyMinutesRadioButton.IsChecked == true ? 60 : PhoneNetworkFree.DefaultMinutes;

    // Letters and digits only: anything else typed is dropped before it lands.
    private void OnPasswordPreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = PhoneNetworkText.Clean(e.Text).Length != e.Text.Length;

    // A space never reaches PreviewTextInput.
    private void OnPasswordPreviewKeyDown(object sender, KeyEventArgs e) => e.Handled |= e.Key == Key.Space;

    private void OnPasswordPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text || PhoneNetworkText.Clean(text).Length != text.Length)
        {
            e.CancelCommand();
        }
    }

    // Both boxes always hold the same letters and digits.
    private void OnPasswordChanged(object sender, RoutedEventArgs e) => Sync(PasswordBox.Password);

    private void OnPasswordTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Sync(PasswordTextBox.Text);

    private void Sync(string typed)
    {
        if (_syncing)
        {
            return;
        }
        _syncing = true;
        try
        {
            var clean = PhoneNetworkText.Clean(typed);
            if (PasswordBox.Password != clean)
            {
                PasswordBox.Password = clean;
            }
            if (PasswordTextBox.Text != clean)
            {
                PasswordTextBox.Text = clean;
                PasswordTextBox.CaretIndex = clean.Length;
            }
        }
        finally
        {
            _syncing = false;
        }
        ShowState();
    }

    private void OnShowPasswordChanged(object sender, RoutedEventArgs e)
    {
        var show = ShowPasswordCheckBox.IsChecked == true;
        PasswordTextBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PasswordBox.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (show)
        {
            PasswordTextBox.Focus();
            PasswordTextBox.CaretIndex = PasswordTextBox.Text.Length;
        }
        else
        {
            PasswordBox.Focus();
        }
    }

    private void ShowState()
    {
        PasswordHintTextBlock.Text = PhoneNetworkText.PasswordHint(Password);
        FreeButton.IsEnabled = LockButton.IsEnabled = RemovePasswordButton.IsEnabled = !_running;
        SetPasswordButton.IsEnabled = !_running && PhoneNetworkText.IsPasswordReady(Password);
    }

    private async void OnFreeClicked(object sender, RoutedEventArgs e) => await RunAsync(PhoneNetworkAction.Free, ChosenMinutes, null);

    private async void OnLockClicked(object sender, RoutedEventArgs e) => await RunAsync(PhoneNetworkAction.Lock, PhoneNetworkFree.LockNow, null);

    private async void OnSetPasswordClicked(object sender, RoutedEventArgs e)
    {
        var password = Password;
        if (PhoneNetworkText.IsPasswordReady(password))
        {
            await RunAsync(PhoneNetworkAction.SetPassword, 0, password);
        }
    }

    private async void OnRemovePasswordClicked(object sender, RoutedEventArgs e) =>
        await RunAsync(PhoneNetworkAction.RemovePassword, 0, null);

    private async Task RunAsync(PhoneNetworkAction action, int minutes, string? password)
    {
        if (_running)
        {
            return;
        }

        _running = true;
        ShowState();
        ProgressTextBlock.Text = PhoneNetworkText.Progress(action, _rows.Count);
        foreach (var row in _rows)
        {
            row.Show(PhoneRowState.Working, PhoneNetworkText.Working(action));
        }

        try
        {
            // One salt for this send; the hashing takes a moment.
            var payload = password is null ? Array.Empty<byte>() : await Task.Run(() => PhoneAdminPassword.Encode(password));
            var done = await Task.WhenAll(_rows.Select(row => Task.Run(() => SendOne(row, action, minutes, payload))));
            ProgressTextBlock.Text = PhoneNetworkText.Summary(action, minutes, done.Count(ok => ok), _rows.Count);
        }
        finally
        {
            _running = false;
            ShowState();
        }
    }

    // Off the UI thread; the row is updated on it. True when the device did it.
    private bool SendOne(PhoneRow row, PhoneNetworkAction action, int minutes, byte[] passwordPayload)
    {
        var (state, line) = Send(row.Phone, action, minutes, passwordPayload);
        Dispatcher.Invoke(() => row.Show(state, line));
        return state == PhoneRowState.Done;
    }

    private (PhoneRowState State, string Line) Send(KnownMachine phone, PhoneNetworkAction action, int minutes, byte[] passwordPayload)
    {
        if (phone.AgentThumbprint is not { Length: > 0 })
        {
            return (PhoneRowState.Failed, PhoneNetworkText.NoIdentity);
        }
        try
        {
            var answer = action is PhoneNetworkAction.Free or PhoneNetworkAction.Lock
                ? PhoneNetworkClient.Free(phone, minutes, _masterCertificate)
                : PhoneNetworkClient.SetAdminPassword(phone, passwordPayload, _masterCertificate);
            return (answer is { Succeeded: true } ? PhoneRowState.Done : PhoneRowState.Failed, PhoneNetworkText.Line(answer));
        }
        catch (Exception ex)
        {
            return (PhoneRowState.Failed, FailureText.DescribePhoneAgent(ex));
        }
    }
}
