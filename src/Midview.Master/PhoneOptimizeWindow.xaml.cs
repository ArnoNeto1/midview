using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// "Otimizar" (CELULARES): every phone hides its ad apps and tries the ad-free
// DNS, all at once, as soon as the window opens; each one's line says how it
// went (PhoneOptimizeText). Running it again changes nothing that is already done.
public partial class PhoneOptimizeWindow : MidviewWindow
{
    private readonly X509Certificate2 _masterCertificate;
    private readonly ObservableCollection<PhoneRow> _rows;
    private bool _running;

    // allPhones: phones is every phone in the classroom list.
    public PhoneOptimizeWindow(IReadOnlyList<KnownMachine> phones, bool allPhones, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _masterCertificate = masterCertificate;
        Title = DialogHeading.Heading = PhoneOptimizeText.Heading(phones.Count);
        TargetsTextBlock.Text = PhoneAppBlockText.Targets(phones.Select(phone => phone.Name).ToList(), allPhones);
        _rows = new ObservableCollection<PhoneRow>(phones.Select(phone => new PhoneRow(phone)));
        PhonesList.ItemsSource = _rows;
        Loaded += async (_, _) => await RunAsync();
    }

    private async void OnRunClicked(object sender, RoutedEventArgs e) => await RunAsync();

    private async Task RunAsync()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        RunButton.IsEnabled = false;
        ProgressTextBlock.Text = _rows.Count == 1 ? "Otimizando o celular..." : $"Otimizando os {_rows.Count} celulares...";
        foreach (var row in _rows)
        {
            row.Show(PhoneRowState.Working, PhoneOptimizeText.Working);
        }

        try
        {
            var done = await Task.WhenAll(_rows.Select(row => Task.Run(() => OptimizeOne(row))));
            ProgressTextBlock.Text = PhoneOptimizeText.Summary(
                done.Count(r => r.Done), _rows.Count, done.Count(r => r.Done && r.Removing));
        }
        finally
        {
            _running = false;
            RunButton.IsEnabled = true;
        }
    }

    // Off the UI thread; the row is updated on it. Done: fully optimized;
    // Removing: apps still leaving the phone in the background (the row is
    // a check mark with the "saindo" note, and the summary is not "Pronto."
    // yet).
    private (bool Done, bool Removing) OptimizeOne(PhoneRow row)
    {
        var (state, line, done, removing) = Optimize(row.Phone);
        Dispatcher.Invoke(() => row.Show(state, line));
        return (done, removing);
    }

    private (PhoneRowState State, string Line, bool Done, bool Removing) Optimize(KnownMachine phone)
    {
        if (phone.AgentThumbprint is not { Length: > 0 })
        {
            return (PhoneRowState.Failed, "falta a identidade do celular: pareie-o de novo em \"Máquinas da sala\"", false, false);
        }
        try
        {
            var report = PhoneOptimizeClient.Optimize(phone, _masterCertificate);
            var line = PhoneOptimizeText.Line(report);
            if (report is null)
            {
                return (PhoneRowState.Failed, line, false, false);
            }
            var done = PhoneOptimizeText.IsDone(report);
            return (done ? PhoneRowState.Done : PhoneRowState.Partial, line, done, PhoneOptimizeText.IsRemoving(report));
        }
        catch (Exception ex)
        {
            return (PhoneRowState.Failed, FailureText.DescribePhoneAgent(ex), false, false);
        }
    }
}
