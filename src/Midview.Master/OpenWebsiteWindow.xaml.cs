using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using Midview.Protocol;

namespace Midview.Master;

// Opens a website in the browser of one machine or of the whole class (the
// Session's WindowsWebsiteOpener). Only a website: what the teacher typed
// is checked here first (WebsiteAddress), and again on each machine. It can
// also go into the "Abrir site" menu (SavedMenu), to open with a click.
public partial class OpenWebsiteWindow : MidviewWindow
{
    private readonly IReadOnlyList<KnownMachine> _targets;
    private readonly X509Certificate2 _masterCertificate;

    // wholeClass: targets is every machine in the classroom list (TargetsText).
    public OpenWebsiteWindow(IReadOnlyList<KnownMachine> targets, bool wholeClass, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        TargetsTextBlock.Text = TargetsText.Describe(TargetsPreposition.In, targets, wholeClass);
        OpenButton.Content = targets.Count == 1 ? "Abrir na máquina" : "Abrir nas máquinas";
    }

    private void OnAddressChanged(object sender, TextChangedEventArgs e) =>
        ProblemTextBlock.Visibility = Visibility.Collapsed;

    private void OnSaveChanged(object sender, RoutedEventArgs e) =>
        NamePanel.IsEnabled = SaveCheckBox.IsChecked == true;

    private async void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        // One site, or several separated by commas or spaces.
        if (!WebsiteAddress.TryParseMany(AddressTextBox.Text, out var addresses, out var problem))
        {
            ProblemTextBlock.Text = problem;
            ProblemTextBlock.Visibility = Visibility.Visible;
            AddressTextBox.Focus();
            return;
        }

        // Several sites are saved as one item, which opens them all.
        var saved = SaveCheckBox.IsChecked == true
            ? SavedMenu.AddAndSave(SavedMenu.WebsitesPath, new MenuEntry(
                NameTextBox.Text.Trim() is { Length: > 0 } name ? name : SavedMenu.NameFor(addresses),
                string.Join(", ", addresses.Select(address => address.AbsoluteUri))))
            : null;
        var what = addresses.Count == 1 ? addresses[0].AbsoluteUri : $"{addresses.Count} sites";

        // The window stays open, and "Abrir site" brings it back as it was:
        // it is cleared now, ready for the next site (the summary below still
        // names this one), and what the teacher types while this one goes out
        // is kept.
        AddressTextBox.Clear();
        NameTextBox.Clear();
        SaveCheckBox.IsChecked = false;
        AddressTextBox.Focus();

        OpenButton.IsEnabled = false;
        ResultTextBlock.Text = string.Empty;
        ProgressTextBlock.Text = $"Abrindo {what}...";

        var results = await RemoteLaunch.OpenWebsites(_targets, _masterCertificate, addresses);

        var failures = results.Count(result => !result.Succeeded);
        var summary = failures == 0
            ? results.Length == 1 ? $"Aberto: {what}" : $"Aberto nas {results.Length} máquinas: {what}"
            : $"Aberto em {results.Length - failures} de {results.Length}: {what}";
        ProgressTextBlock.Text = saved is null ? summary : $"{summary}\n{saved}";
        ResultTextBlock.Text = LaunchText.Lines(results);
        OpenButton.IsEnabled = true;
    }
}
