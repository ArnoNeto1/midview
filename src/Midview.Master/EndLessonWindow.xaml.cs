using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;

namespace Midview.Master;

// "Encerrar aula": stops the saving rounds, saves the machines one last
// time, then -- as the teacher chooses -- keeps the class's files on this
// PC, takes out the ones this lesson added, or all of them, and empties every
// machine's "Pasta da turma" for the next class. Closed before that, the
// lesson goes on (Ended stays false and MainWindow starts saving again).
public partial class EndLessonWindow : Window
{
    private readonly ClassFolderSyncer _syncer;
    private readonly Func<IReadOnlyList<KnownMachine>> _machines;
    private readonly X509Certificate2 _masterCertificate;
    private bool _busy = true;

    // machines: the machines that are on, asked when saving and again when
    // emptying.
    public EndLessonWindow(ClassFolderSyncer syncer, Func<IReadOnlyList<KnownMachine>> machines, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _syncer = syncer;
        _machines = machines;
        _masterCertificate = masterCertificate;
        TitleTextBlock.Text = $"Aula da turma {syncer.Lesson.ClassName}";
    }

    public bool Ended { get; private set; }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        await _syncer.StopAsync();
        var machines = _machines();
        var results = await _syncer.SyncNowAsync(machines);
        var saved = results.Count(result => result.Succeeded);
        ProgressTextBlock.Text = machines.Count == 0
            ? "Nenhuma máquina ligada para salvar agora; o que já foi salvo está neste computador."
            : $"Arquivos salvos de {saved} de {machines.Count} máquina(s) ligada(s).";
        ResultTextBlock.Text = Describe(results);
        ChoicesPanel.IsEnabled = true;
        EndButton.IsEnabled = true;
        _busy = false;
    }

    private async void OnEndClicked(object sender, RoutedEventArgs e)
    {
        if (Ended)
        {
            Close();
            return;
        }
        if (AllRadioButton.IsChecked == true && MessageBox.Show(this,
                $"Apagar todos os arquivos da turma {_syncer.Lesson.ClassName}? Eles vão para a Lixeira deste computador.",
                "Encerrar aula", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        _busy = true;
        ChoicesPanel.IsEnabled = false;
        EndButton.IsEnabled = false;
        ProgressTextBlock.Text = "Encerrando a aula...";

        var lines = new List<string>();
        if (TodayRadioButton.IsChecked == true)
        {
            var (recycled, problems) = await Task.Run(() => _syncer.RecycleAdded());
            lines.Add($"{recycled} arquivo(s) desta aula foram para a Lixeira deste computador.");
            lines.AddRange(problems.Select(problem => $"✗ não saiu: {problem}"));
        }
        else if (AllRadioButton.IsChecked == true)
        {
            var (recycled, problems) = await Task.Run(() => _syncer.RecycleAll());
            lines.Add(recycled == 0 && problems.Count == 0
                ? "A pasta da turma já estava vazia."
                : $"{recycled} item(ns) da pasta da turma foram para a Lixeira deste computador.");
            lines.AddRange(problems.Select(problem => $"✗ não saiu: {problem}"));
        }
        else
        {
            lines.Add($"Os arquivos da turma continuam em {_syncer.Store.DirectoryPath}.");
        }

        var cleared = await ClassFolderSyncer.ClearAsync(_machines(), _masterCertificate);
        lines.Add(string.Empty);
        lines.Add("Pastas das máquinas:");
        lines.Add(Describe(cleared));
        try
        {
            _syncer.Forget();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lines.Add($"✗ O registro da aula não pôde ser apagado ({ex.Message}); ao abrir o Midview de novo, encerre a aula outra vez.");
        }
        Ended = true;
        _busy = false;

        ProgressTextBlock.Text = "Aula encerrada.";
        ResultTextBlock.Text = string.Join("\n", lines);
        EndButton.Content = "Fechar";
        EndButton.IsEnabled = true;
    }

    private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        var directory = _syncer.Store.DirectoryPath;
        if (Directory.Exists(directory))
        {
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
    }

    // Not while saving or ending: what's half done would be left that way.
    private void OnWindowClosing(object? sender, CancelEventArgs e) => e.Cancel = _busy;

    private static string Describe(IReadOnlyList<MachineSyncResult> results) =>
        results.Count == 0
            ? "Nenhuma máquina ligada."
            : string.Join("\n", results.Select(result => $"{(result.Succeeded ? "✓" : "✗")} {result.Name}: {result.Detail}"));
}
