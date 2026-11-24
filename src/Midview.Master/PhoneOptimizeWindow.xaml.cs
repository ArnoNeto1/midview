using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// "Otimizar" (CELULARES): every phone hides its ad apps, removes the apps the
// class does not use and those someone installed (the teacher's "Instalar
// programa" ones stay), tries the ad-free DNS and arranges its home screen, all
// at once, as soon as the window opens (asking first for the admin
// password when none is saved: OptimizeAdminWindow); then it gets the school's admin
// password, when one is saved (PhoneOptimizeAdmin). Each one's line says how
// it went (PhoneOptimizeText). Running it again changes nothing that is
// already done.
public partial class PhoneOptimizeWindow : MidviewWindow
{
    private readonly X509Certificate2 _masterCertificate;
    private readonly ObservableCollection<PhoneRow> _rows;
    private bool _running;
    private byte[]? _adminPayload;

    // allPhones: phones is every phone in the classroom list.
    public PhoneOptimizeWindow(IReadOnlyList<KnownMachine> phones, bool allPhones, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _masterCertificate = masterCertificate;
        Title = DialogHeading.Heading = PhoneOptimizeText.Heading(phones.Count);
        TargetsTextBlock.Text = PhoneAppBlockText.Targets(phones.Select(phone => phone.Name).ToList(), allPhones);
        _rows = new ObservableCollection<PhoneRow>(phones.Select(phone => new PhoneRow(phone)));
        PhonesList.ItemsSource = _rows;
        Loaded += async (_, _) =>
        {
            AskAdminPassword();
            await RunAsync();
        };
    }

    // None saved yet: the teacher is asked for one first (OptimizeAdminWindow),
    // so every device optimized gets it; skipping optimizes without one.
    private void AskAdminPassword()
    {
        if (PhoneDefaultsFile.Load().AdminPassword is not null)
        {
            return;
        }
        new OptimizeAdminWindow { Owner = this }.ShowDialog();
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
        // The school's admin password saved in "Preparar celulares" (its hash), if any.
        _adminPayload = PhoneDefaultsFile.Load().AdminPassword;
        ProgressTextBlock.Text = _rows.Count == 1 ? "Otimizando o celular..." : $"Otimizando os {_rows.Count} celulares...";
        foreach (var row in _rows)
        {
            row.Show(PhoneRowState.Working, PhoneOptimizeText.Working);
        }

        try
        {
            var done = await Task.WhenAll(_rows.Select(row => Task.Run(() => OptimizeOne(row))));
            ProgressTextBlock.Text = PhoneOptimizeText.Summary(
                done.Count(r => r.Done), _rows.Count, done.Count(r => r.Done && r.Removing), done.Count(r => r.Done && r.Arranging));
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
    // yet); Arranging: the home screen still being arranged, the same way.
    private (bool Done, bool Removing, bool Arranging) OptimizeOne(PhoneRow row)
    {
        var (state, line, done, report) = Optimize(row.Phone);
        Dispatcher.Invoke(() => row.Show(state, line));
        return (done, PhoneOptimizeText.IsRemoving(report), PhoneOptimizeText.IsArranging(report));
    }

    private (PhoneRowState State, string Line, bool Done, PhoneOptimizeReport? Report) Optimize(KnownMachine phone)
    {
        if (phone.AgentThumbprint is not { Length: > 0 })
        {
            return (PhoneRowState.Failed, "falta a identidade do celular: pareie-o de novo em \"Máquinas da sala\"", false, null);
        }
        try
        {
            var report = PhoneOptimizeClient.Optimize(phone, _masterCertificate);
            var adminNote = PhoneOptimizeAdmin.Send(
                report, _adminPayload, payload => PhoneNetworkClient.SetAdminPassword(phone, payload, _masterCertificate));
            var line = PhoneOptimizeAdmin.Line(PhoneOptimizeText.Line(report), adminNote);
            if (report is null)
            {
                return (PhoneRowState.Failed, line, false, null);
            }
            var done = PhoneOptimizeText.IsDone(report);
            return (done ? PhoneRowState.Done : PhoneRowState.Partial, line, done, report);
        }
        catch (Exception ex)
        {
            return (PhoneRowState.Failed, FailureText.DescribePhoneAgent(ex), false, null);
        }
    }
}
