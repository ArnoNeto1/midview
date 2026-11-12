using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// "Desinstalar programa" on phones: the teacher ticks apps in the phones'
// list (their icons, as in "Bloquear apps nos celulares") and each phone
// removes the ones it has, silently, as the Device Owner (UninstallPrograms
// with their packages on the agent's port). The apps that came with the phone
// are listed faded: Android cannot remove them, so the teacher blocks them
// instead; the phone refuses them anyway, and so the Midview and the apps the
// phone needs. After it the list is read again.
public partial class PhoneUninstallWindow : MidviewWindow
{
    private readonly IReadOnlyList<KnownMachine> _phones;
    private readonly X509Certificate2 _masterCertificate;
    private bool _loading;
    private bool _removing;

    // allPhones: phones is every phone in the classroom list.
    public PhoneUninstallWindow(IReadOnlyList<KnownMachine> phones, bool allPhones, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _phones = phones;
        _masterCertificate = masterCertificate;
        Title = DialogHeading.Heading = PhoneProgramsText.UninstallHeading(phones.Count);
        TargetsTextBlock.Text = PhoneProgramsText.Describe(phones, allPhones);
        HintTextBlock.Text = PhoneProgramsText.UninstallHint;
        ListTitleTextBlock.Text = PhoneAppBlockText.ListTitle(phones.Count);
        UninstallButton.Content = PhoneProgramsText.UninstallButton(phones.Count);
        AppsGrid.ChosenChanged += OnChosenChanged;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_loading)
        {
            return;
        }
        _loading = true;
        UpdateButtons();
        ListTextBlock.Text = "Pedindo a lista de apps...";
        try
        {
            var answers = await PhoneAppGrid.Read(_phones, _masterCertificate);
            var answered = answers.Count(answer => answer is not null);
            if (answered > 0)
            {
                AppsGrid.Show(answers, forUninstall: true);
            }
            ListTextBlock.Text = answered > 0 && _phones.Count == 1
                ? "Apps deste celular. Marque os que quer desinstalar."
                : PhoneAppBlockText.ListLine(answered, _phones.Count);
        }
        finally
        {
            _loading = false;
            UpdateButtons();
        }
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void OnChosenChanged()
    {
        var chosen = AppsGrid.Chosen.Count;
        ChosenTextBlock.Text = chosen switch
        {
            0 => string.Empty,
            1 => "1 marcado",
            _ => $"{chosen} marcados",
        };
        UpdateButtons();
    }

    private async void OnUninstallClicked(object sender, RoutedEventArgs e)
    {
        var packages = AppsGrid.Chosen;
        if (packages.Count == 0)
        {
            ProgressTextBlock.Text = PhoneProgramsText.UninstallNone;
            return;
        }
        var names = string.Join(", ", packages.Select(AppsGrid.NameOf));
        _removing = true;
        UpdateButtons();
        ResultTextBlock.Text = string.Empty;
        ProgressTextBlock.Text = $"Desinstalando em {_phones.Count} celular(es): {names}.";
        try
        {
            var results = await Task.WhenAll(_phones.Select(phone => Task.Run(() =>
            {
                var (succeeded, detail) = PhoneAgentCommand.Send(
                    phone, PhoneProgramsText.OldAgent(MessageType.UninstallPrograms),
                    _ => LabConfigClient.UninstallPrograms(phone, _masterCertificate, packages));
                return (phone.Name, succeeded, detail);
            })));
            var failures = results.Count(result => !result.succeeded);
            ProgressTextBlock.Text = failures == 0
                ? results.Length == 1 ? "Feito." : $"Feito nos {results.Length} celulares."
                : $"Terminou em {results.Length} celular(es), com avisos em {failures}.";
            ResultTextBlock.Text = string.Join("\n", results.Select(result => $"{(result.succeeded ? "✓" : "✗")} {result.Name}: {result.detail}"));
        }
        finally
        {
            _removing = false;
            UpdateButtons();
        }
        await LoadAsync();
    }

    private void UpdateButtons()
    {
        UninstallButton.IsEnabled = !_removing && !_loading && AppsGrid.Chosen.Count > 0;
        RefreshButton.IsEnabled = !_loading && !_removing;
    }
}
