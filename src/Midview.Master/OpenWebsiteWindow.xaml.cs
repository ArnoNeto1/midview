using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using Midview.Protocol;

namespace Midview.Master;

// Opens a website in the browser of one machine or of the whole class (the
// Session's WindowsWebsiteOpener). Only a website: what the teacher typed
// is checked here first (WebsiteAddress), and again on each machine. It can
// also go into the "Abrir site" menu (SavedMenu), to open with a click.
public partial class OpenWebsiteWindow : Window
{
    private readonly IReadOnlyList<KnownMachine> _targets;
    private readonly X509Certificate2 _masterCertificate;

    public OpenWebsiteWindow(IReadOnlyList<KnownMachine> targets, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        TargetsTextBlock.Text = targets.Count == 1
            ? $"Em: {targets[0].Name}"
            : $"Na turma toda ({targets.Count} máquinas)";
        OpenButton.Content = targets.Count == 1 ? "Abrir na máquina" : "Abrir nas máquinas";
    }

    private void OnAddressChanged(object sender, TextChangedEventArgs e) =>
        ProblemTextBlock.Visibility = Visibility.Collapsed;

    private void OnSaveChanged(object sender, RoutedEventArgs e) =>
        NamePanel.IsEnabled = SaveCheckBox.IsChecked == true;

    private async void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        if (!WebsiteAddress.TryParse(AddressTextBox.Text, out var address, out var problem))
        {
            ProblemTextBlock.Text = problem;
            ProblemTextBlock.Visibility = Visibility.Visible;
            AddressTextBox.Focus();
            return;
        }

        var saved = SaveCheckBox.IsChecked == true
            ? SavedMenu.AddAndSave(SavedMenu.WebsitesPath, new MenuEntry(
                NameTextBox.Text.Trim() is { Length: > 0 } name ? name : SavedMenu.NameFor(address), address.AbsoluteUri))
            : null;

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
        ProgressTextBlock.Text = $"Abrindo {address.AbsoluteUri}...";

        var results = await RemoteLaunch.OpenWebsite(_targets, _masterCertificate, address);

        var failures = results.Count(result => !result.Succeeded);
        var summary = failures == 0
            ? results.Length == 1 ? $"Aberto: {address.AbsoluteUri}" : $"Aberto nas {results.Length} máquinas: {address.AbsoluteUri}"
            : $"Aberto em {results.Length - failures} de {results.Length}: {address.AbsoluteUri}";
        ProgressTextBlock.Text = saved is null ? summary : $"{summary}\n{saved}";
        ResultTextBlock.Text = LaunchText.Lines(results);
        OpenButton.IsEnabled = true;
    }
}
