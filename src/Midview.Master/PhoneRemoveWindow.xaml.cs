using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;

namespace Midview.Master;

// "Tirar o Midview dos celulares e tablets" (Configurar celular/tablet), the panic button:
// the teacher picks "Desfazer e tirar o Midview" (the school's locks and
// settings undone, the student's files kept, the Midview uninstalled) or
// "Restaurar de fábrica" (everything erased), each explained, types the word
// that confirms it (REMOVER or APAGAR) and every device is asked at once.
// Each device's line says how it went; one that did it leaves the room's
// list (removeFromRoom), as "Remover da sala" does. A later run asks only
// the devices where it did not go; after an undo that failed somewhere,
// "Restaurar de fábrica" is chosen for them (a phone that kept a lock is
// still the school's, and still takes the wipe).
public partial class PhoneRemoveWindow : MidviewWindow
{
    private readonly X509Certificate2 _masterCertificate;
    private readonly Action<KnownMachine> _removeFromRoom;
    private readonly ObservableCollection<PhoneRow> _rows;
    private bool _running;

    // allPhones: phones is every Android device in the classroom list.
    public PhoneRemoveWindow(
        IReadOnlyList<KnownMachine> phones, bool allPhones, X509Certificate2 masterCertificate, Action<KnownMachine> removeFromRoom)
    {
        InitializeComponent();
        _masterCertificate = masterCertificate;
        _removeFromRoom = removeFromRoom;
        Title = DialogHeading.Heading = PhoneRemoveText.Heading(phones.Count);
        TargetsTextBlock.Text = PhoneAppBlockText.Targets(phones.Select(phone => phone.Name).ToList(), allPhones);
        UndoTitleTextBlock.Text = PhoneRemoveText.Title(PhoneRemoveChoice.Undo);
        UndoTextBlock.Text = PhoneRemoveText.Explanation(PhoneRemoveChoice.Undo);
        ResetTitleTextBlock.Text = PhoneRemoveText.Title(PhoneRemoveChoice.FactoryReset);
        ResetTextBlock.Text = PhoneRemoveText.Explanation(PhoneRemoveChoice.FactoryReset);
        OnThePhoneTextBlock.Text = PhoneRemoveText.OnThePhone;
        LeavesTextBlock.Text = PhoneRemoveText.LeavesTheRoom;
        _rows = new ObservableCollection<PhoneRow>(phones.Select(phone => new PhoneRow(phone)));
        PhonesList.ItemsSource = _rows;
        ShowState();
    }

    internal PhoneRemoveChoice Choice =>
        ResetRadioButton.IsChecked == true ? PhoneRemoveChoice.FactoryReset : PhoneRemoveChoice.Undo;

    // Another choice wants its own word: what was typed for the other goes.
    private void OnChoiceChanged(object sender, RoutedEventArgs e)
    {
        if (WordTextBox is null)
        {
            return;
        }
        WordTextBox.Text = string.Empty;
        ShowState();
    }

    private void OnWordChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ShowState();

    private void ShowState()
    {
        if (GoButton is null)
        {
            return;
        }
        PromptTextBlock.Text = PhoneRemoveText.Prompt(Choice);
        GoButton.Content = PhoneRemoveText.Button(Choice);
        GoButton.IsEnabled = !_running && Pending.Count > 0 && PhoneRemoveText.IsConfirmed(Choice, WordTextBox.Text);
        UndoRadioButton.IsEnabled = ResetRadioButton.IsEnabled = WordTextBox.IsEnabled = !_running;
    }

    // The devices still to ask: every one but those where it went.
    private IReadOnlyList<PhoneRow> Pending => _rows.Where(row => row.State != PhoneRowState.Done).ToList();

    private async void OnGoClicked(object sender, RoutedEventArgs e) => await RunAsync();

    internal async Task RunAsync()
    {
        var choice = Choice;
        if (_running || !PhoneRemoveText.IsConfirmed(choice, WordTextBox.Text))
        {
            return;
        }

        var rows = Pending;
        if (rows.Count == 0)
        {
            return;
        }
        _running = true;
        WordTextBox.Text = string.Empty;
        ShowState();
        ProgressTextBlock.Text = PhoneRemoveText.Progress(choice, rows.Count);
        foreach (var row in rows)
        {
            row.Show(PhoneRowState.Working, PhoneRemoveText.Working(choice));
        }

        var failed = 0;
        try
        {
            var done = await Task.WhenAll(rows.Select(row => Task.Run(() => SendOne(row, choice))));
            foreach (var row in rows.Where((_, i) => done[i]))
            {
                _removeFromRoom(row.Phone);
            }
            failed = done.Count(ok => !ok);
            ProgressTextBlock.Text = PhoneRemoveText.Summary(choice, rows.Count - failed, rows.Count);
            if (choice == PhoneRemoveChoice.Undo && failed > 0)
            {
                ProgressTextBlock.Text += " " + PhoneRemoveText.OfferReset(failed);
            }
        }
        finally
        {
            _running = false;
            if (choice == PhoneRemoveChoice.Undo && failed > 0)
            {
                ResetRadioButton.IsChecked = true;
            }
            ShowState();
        }
    }

    // Off the UI thread; the row is updated on it. True when the device did it.
    private bool SendOne(PhoneRow row, PhoneRemoveChoice choice)
    {
        var (state, line) = Send(row.Phone, choice);
        Dispatcher.Invoke(() => row.Show(state, line));
        return state == PhoneRowState.Done;
    }

    private (PhoneRowState State, string Line) Send(KnownMachine phone, PhoneRemoveChoice choice)
    {
        if (phone.AgentThumbprint is not { Length: > 0 })
        {
            return (PhoneRowState.Failed, PhoneRemoveText.NoIdentity);
        }
        try
        {
            var answer = PhoneRemoveClient.Send(phone, choice, _masterCertificate);
            return (answer is { Succeeded: true } ? PhoneRowState.Done : PhoneRowState.Failed, PhoneRemoveText.Line(choice, answer));
        }
        catch (Exception ex)
        {
            return (PhoneRowState.Failed, FailureText.DescribePhoneAgent(ex));
        }
    }
}
