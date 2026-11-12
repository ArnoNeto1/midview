using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// Closes the student's programs on one machine or on the whole class (the
// Session's WindowsAppCloser), after warning the student for the time the
// teacher picks. Forcing goes ahead without asking the teacher: the choice
// itself says that unsaved work is lost. A phone warns the student for the
// same time and then closes all its apps (CloseAppsTargets): for phones
// alone the asking-or-forcing card is not shown, and with PCs a line says
// that card is for the PCs.
public partial class CloseAppsWindow : MidviewWindow
{
    private readonly IReadOnlyList<KnownMachine> _targets;
    private readonly X509Certificate2 _masterCertificate;

    // wholeClass: targets is every machine in the classroom list (TargetsText).
    public CloseAppsWindow(IReadOnlyList<KnownMachine> targets, bool wholeClass, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        TargetsTextBlock.Text = CloseAppsTargets.Describe(targets, wholeClass);
        HintTextBlock.Text = CloseAppsTargets.Hint(targets);
        CloseButton.Content = CloseAppsTargets.Button(targets);
        if (CloseAppsTargets.OnlyPhones(targets))
        {
            ModeCard.Visibility = Visibility.Collapsed;
            DelayCard.Margin = new Thickness(0, 12, 0, 14);
            // Without the asking-or-forcing card, a shorter window.
            MinHeight = 460;
            Height = 560;
        }
        if (CloseAppsTargets.Note(targets) is { } note)
        {
            PhoneNoteTextBlock.Text = note;
            PhoneNoteTextBlock.Visibility = Visibility.Visible;
        }
    }

    private async void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        var mode = ForceRadioButton.IsChecked == true ? CloseAppsMode.Force : CloseAppsMode.Ask;
        var delaySeconds = OneMinuteRadioButton.IsChecked == true ? 60
            : ThirtySecondsRadioButton.IsChecked == true ? 30
            : 0;
        CloseButton.IsEnabled = false;
        ResultTextBlock.Text = string.Empty;
        ProgressTextBlock.Text = CloseAppsTargets.Progress(delaySeconds);

        // All machines at once: an offline one must not hold up the rest.
        var payload = CloseAppsCommand.Encode(mode, delaySeconds);
        var results = await Task.WhenAll(_targets.Select(machine => Task.Run(() => CloseOn(machine, payload))));

        ProgressTextBlock.Text = CloseAppsTargets.Done(
            results.Length, results.Count(result => !result.Succeeded), CloseAppsTargets.OnlyPhones(_targets));
        ResultTextBlock.Text = string.Join("\n", results.Select(result =>
            $"{(result.Succeeded ? "✓" : "✗")} {result.Name}: {result.Detail}"));
        CloseButton.IsEnabled = true;
    }

    private (string Name, bool Succeeded, string Detail) CloseOn(KnownMachine machine, byte[] payload)
    {
        var (succeeded, detail) = CloseAppsTargets.Send(machine, payload, _masterCertificate);
        return (machine.Name, succeeded, detail);
    }
}
