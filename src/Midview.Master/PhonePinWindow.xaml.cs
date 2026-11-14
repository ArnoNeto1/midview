using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Midview.Master;

// "Senha dos celulares e tablets" (Configurar celular/tablet): the teacher types a 4-digit
// PIN and puts it on every device at once ("Pôr o PIN"), or takes it off
// ("Tirar o PIN"); each device's line says how it went (PhonePinText). The
// PIN lives only in the box while the window is open: nothing saves it.
public partial class PhonePinWindow : MidviewWindow
{
    private readonly X509Certificate2 _masterCertificate;
    private readonly ObservableCollection<PhoneRow> _rows;
    private bool _running;
    private bool _syncing;

    // allPhones: phones is every Android device in the classroom list.
    public PhonePinWindow(IReadOnlyList<KnownMachine> phones, bool allPhones, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _masterCertificate = masterCertificate;
        Title = DialogHeading.Heading = PhonePinText.Heading(phones.Count);
        TargetsTextBlock.Text = PhoneAppBlockText.Targets(phones.Select(phone => phone.Name).ToList(), allPhones);
        _rows = new ObservableCollection<PhoneRow>(phones.Select(phone => new PhoneRow(phone)));
        PhonesList.ItemsSource = _rows;
        DataObject.AddPastingHandler(PinPasswordBox, OnPinPasting);
        DataObject.AddPastingHandler(PinTextBox, OnPinPasting);
        ShowPinState();
    }

    private string Pin => ShowPinCheckBox.IsChecked == true ? PinTextBox.Text : PinPasswordBox.Password;

    // Digits only: anything else typed is dropped before it lands.
    private void OnPinPreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = PhonePinText.Digits(e.Text).Length != e.Text.Length;

    // A space never reaches PreviewTextInput.
    private void OnPinPreviewKeyDown(object sender, KeyEventArgs e) => e.Handled |= e.Key == Key.Space;

    private void OnPinPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text || PhonePinText.Digits(text).Length != text.Length)
        {
            e.CancelCommand();
        }
    }

    // Anything else that still got in is taken out; both boxes always hold
    // the same digits.
    private void OnPinPasswordChanged(object sender, RoutedEventArgs e) => Sync(PinPasswordBox.Password);

    private void OnPinTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Sync(PinTextBox.Text);

    private void Sync(string typed)
    {
        if (_syncing)
        {
            return;
        }
        _syncing = true;
        try
        {
            var digits = PhonePinText.Digits(typed);
            if (PinPasswordBox.Password != digits)
            {
                PinPasswordBox.Password = digits;
            }
            if (PinTextBox.Text != digits)
            {
                PinTextBox.Text = digits;
                PinTextBox.CaretIndex = digits.Length;
            }
        }
        finally
        {
            _syncing = false;
        }
        ShowPinState();
    }

    private void OnShowPinChanged(object sender, RoutedEventArgs e)
    {
        var show = ShowPinCheckBox.IsChecked == true;
        PinTextBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PinPasswordBox.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (show)
        {
            PinTextBox.Focus();
            PinTextBox.CaretIndex = PinTextBox.Text.Length;
        }
        else
        {
            PinPasswordBox.Focus();
        }
    }

    private void ShowPinState()
    {
        PinHintTextBlock.Text = PhonePinText.PinHint(Pin);
        SetButton.IsEnabled = !_running && PhonePinText.IsReady(Pin);
        RemoveButton.IsEnabled = !_running;
    }

    private async void OnSetClicked(object sender, RoutedEventArgs e)
    {
        if (PhonePinText.IsReady(Pin))
        {
            await RunAsync(Pin);
        }
    }

    private async void OnRemoveClicked(object sender, RoutedEventArgs e) => await RunAsync(Midview.Protocol.PhoneScreenPassword.None);

    private async Task RunAsync(string pin)
    {
        if (_running)
        {
            return;
        }

        var set = pin.Length > 0;
        _running = true;
        ShowPinState();
        ProgressTextBlock.Text = PhonePinText.Progress(set, _rows.Count);
        foreach (var row in _rows)
        {
            row.Show(PhoneRowState.Working, PhonePinText.Working(set));
        }

        try
        {
            var done = await Task.WhenAll(_rows.Select(row => Task.Run(() => SendOne(row, pin))));
            ProgressTextBlock.Text = PhonePinText.Summary(set, done.Count(ok => ok), _rows.Count);
        }
        finally
        {
            _running = false;
            ShowPinState();
        }
    }

    // Off the UI thread; the row is updated on it. True when the device did it.
    private bool SendOne(PhoneRow row, string pin)
    {
        var (state, line) = Send(row.Phone, pin);
        Dispatcher.Invoke(() => row.Show(state, line));
        return state == PhoneRowState.Done;
    }

    private (PhoneRowState State, string Line) Send(KnownMachine phone, string pin)
    {
        if (phone.AgentThumbprint is not { Length: > 0 })
        {
            return (PhoneRowState.Failed, PhonePinText.NoIdentity);
        }
        try
        {
            var answer = PhonePinClient.Send(phone, pin, _masterCertificate);
            return (answer is { Succeeded: true } ? PhoneRowState.Done : PhoneRowState.Failed, PhonePinText.Line(answer));
        }
        catch (Exception ex)
        {
            return (PhoneRowState.Failed, FailureText.DescribePhoneAgent(ex));
        }
    }
}
