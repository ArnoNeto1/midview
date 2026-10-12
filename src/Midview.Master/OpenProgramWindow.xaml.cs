using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;

namespace Midview.Master;

// Starts programs on one machine or on the whole class, as the student (the
// Session's WindowsProgramStarter), from commands typed as in Windows' Run
// box, one per line. What was typed is checked here first (TypedText, then
// ProgramCommand), and again on each machine. It can also go into the
// "Abrir programa" menu (SavedMenu), to start with a click.
public partial class OpenProgramWindow : Window
{
    private readonly IReadOnlyList<KnownMachine> _targets;
    private readonly X509Certificate2 _masterCertificate;

    public OpenProgramWindow(IReadOnlyList<KnownMachine> targets, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        TargetsTextBlock.Text = targets.Count == 1
            ? $"Em: {targets[0].Name}"
            : $"Na turma toda ({targets.Count} máquinas)";
        OpenButton.Content = targets.Count == 1 ? "Abrir na máquina" : "Abrir nas máquinas";
    }

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
