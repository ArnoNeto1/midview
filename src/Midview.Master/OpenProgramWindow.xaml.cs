using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;

namespace Midview.Master;

// Starts programs on one machine or on the whole class, as the student (the
// Session's WindowsProgramStarter), from commands typed as in Windows' Run
// box, one per line. What was typed is checked here first (TypedText, then
// ProgramCommand), and again on each machine. It can also go into the
// "Abrir programa" menu (SavedMenu), to start with a click. Phones among
// the machines pick an app of their own list instead (PhoneOpenAppWindow,
// opened from here: openPhones).
public partial class OpenProgramWindow : MidviewWindow
{
    private readonly IReadOnlyList<KnownMachine> _targets;
    private readonly X509Certificate2 _masterCertificate;
    private readonly Action? _openPhones;

    // wholeClass: targets is every machine in the classroom list (TargetsText).
    // phones: the phones that were among the machines, left to openPhones.
    public OpenProgramWindow(
        IReadOnlyList<KnownMachine> targets, bool wholeClass, X509Certificate2 masterCertificate,
        int phones = 0, Action? openPhones = null)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        _openPhones = openPhones;
        TargetsTextBlock.Text = TargetsText.Describe(TargetsPreposition.In, targets, wholeClass);
        OpenButton.Content = targets.Count == 1 ? "Abrir na máquina" : "Abrir nas máquinas";
        if (phones > 0 && openPhones is not null)
        {
            PhonesTextBlock.Text = PhoneProgramsText.PhonesNote(phones);
            PhonesButton.Content = PhoneProgramsText.PhonesButton(phones);
            PhonesCard.Visibility = Visibility.Visible;
            Height += 70;
        }
    }

    private void OnPhonesClicked(object sender, RoutedEventArgs e) => _openPhones?.Invoke();

    private void OnCommandChanged(object sender, TextChangedEventArgs e) =>
        ProblemTextBlock.Visibility = Visibility.Collapsed;

    private void OnSaveChanged(object sender, RoutedEventArgs e) =>
        NamePanel.IsEnabled = SaveCheckBox.IsChecked == true;

    private async void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        if (!TypedText.TryParseCommands(CommandTextBox.Text, out var commands, out var problem, out var invalidAt))
        {
            ProblemTextBlock.Text = invalidAt < 0 ? problem : $"{problem} Ele ficou selecionado: apague-o e tente de novo.";
            ProblemTextBlock.Visibility = Visibility.Visible;
            CommandTextBox.Focus();
            if (invalidAt >= 0)
            {
                // The text stays, with the character selected, for the teacher to fix.
                CommandTextBox.Select(invalidAt, 1);
            }
            return;
        }

        var target = string.Join("\n", commands);
        var saved = SaveCheckBox.IsChecked == true
            ? SavedMenu.AddAndSave(SavedMenu.ProgramsPath, new MenuEntry(
                NameTextBox.Text.Trim() is { Length: > 0 } name ? name : SavedMenu.NameFor(target), target))
            : null;

        var what = commands.Count == 1 ? commands[0] : $"{commands.Count} programas";
        OpenButton.IsEnabled = false;
        ResultTextBlock.Text = string.Empty;
        ProgressTextBlock.Text = $"Abrindo {what}...";

        var results = await RemoteLaunch.OpenPrograms(_targets, _masterCertificate, commands);

        var failures = results.Count(result => !result.Succeeded);
        var summary = failures == 0
            ? results.Length == 1 ? $"Aberto: {what}" : $"Aberto nas {results.Length} máquinas: {what}"
            : $"Aberto em {results.Length - failures} de {results.Length}: {what}";
        ProgressTextBlock.Text = saved is null ? summary : $"{summary}\n{saved}";
        ResultTextBlock.Text = LaunchText.Lines(results);
        OpenButton.IsEnabled = true;
    }
}
