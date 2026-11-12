using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;

namespace Midview.Master;

// "Abrir programa" on phones: the teacher picks one app from the phones'
// list (their icons, as in "Bloquear apps nos celulares") and each phone
// opens it (RemoteLaunch.OpenPhoneApp), or once the teacher's lock screen
// goes. A phone without the app, or with it blocked, says so.
public partial class PhoneOpenAppWindow : MidviewWindow
{
    private readonly IReadOnlyList<KnownMachine> _phones;
    private readonly X509Certificate2 _masterCertificate;
    private bool _loading;
    private bool _opening;

    // allPhones: phones is every phone in the classroom list.
    public PhoneOpenAppWindow(IReadOnlyList<KnownMachine> phones, bool allPhones, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _phones = phones;
        _masterCertificate = masterCertificate;
        Title = DialogHeading.Heading = PhoneProgramsText.OpenAppHeading(phones.Count);
        TargetsTextBlock.Text = PhoneProgramsText.Describe(phones, allPhones);
        HintTextBlock.Text = PhoneProgramsText.OpenAppHint;
        ListTitleTextBlock.Text = PhoneAppBlockText.ListTitle(phones.Count);
        OpenButton.Content = PhoneProgramsText.OpenAppButton(phones.Count);
        AppsGrid.ChosenChanged += UpdateButtons;
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
                AppsGrid.Show(answers, forUninstall: false);
            }
            ListTextBlock.Text = answered > 0 && _phones.Count == 1
                ? "Apps que o aluno pode abrir neste celular. Escolha um."
                : PhoneAppBlockText.ListLine(answered, _phones.Count);
        }
        finally
        {
            _loading = false;
            UpdateButtons();
        }
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private async void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        if (AppsGrid.Chosen is not [var package])
        {
            ProgressTextBlock.Text = PhoneProgramsText.PickOneApp;
            return;
        }
        var name = AppsGrid.NameOf(package);
        _opening = true;
        UpdateButtons();
        ResultTextBlock.Text = string.Empty;
        ProgressTextBlock.Text = $"Abrindo {name}...";
        try
        {
            var results = await RemoteLaunch.OpenPhoneApp(_phones, _masterCertificate, package);
            var failures = results.Count(result => !result.Succeeded);
            ProgressTextBlock.Text = PhoneProgramsText.Opened(_phones, results.Length, failures, name);
            ResultTextBlock.Text = LaunchText.Lines(results);
        }
        finally
        {
            _opening = false;
            UpdateButtons();
        }
    }

    private void UpdateButtons()
    {
        OpenButton.IsEnabled = !_opening && AppsGrid.Chosen.Count == 1;
        RefreshButton.IsEnabled = !_loading && !_opening;
    }
}
