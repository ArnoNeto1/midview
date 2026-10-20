using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// "Pasta da turma" (ClassFolderSyncer): the lesson bar at the bottom of the
// window. When Master opens with no lesson going on, it asks which class is
// in the lab; a lesson left open carries on by itself. While it lasts, the
// machines that are on are kept in step with the class's copy on this PC
// every minute, and "Encerrar aula" (also offered when closing Master) ends
// it.
public partial class MainWindow
{
    private ClassFolderSyncer? _lesson;

    // Set once the lesson was ended on the way out, so closing goes ahead.
    private bool _closeAfterLesson;

    private void ResumeLesson()
    {
        try
        {
            if (ClassLessonFile.Load(ClassLessonFile.DefaultPath) is { } lesson)
            {
                StartSyncing(lesson);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LessonTextBlock.Text = $"A aula em andamento não pôde ser lida: {ex.Message}";
        }
        UpdateLessonBar();
    }

    // Asked once the window is up, so it opens over the classroom.
    private void OnWindowContentRendered(object? sender, EventArgs e)
    {
        if (_lesson is null && _tiles.Count > 0)
        {
            OpenStartLessonWindow();
        }
    }

    private void OnLessonClicked(object sender, RoutedEventArgs e)
    {
        if (_lesson is null)
        {
            OpenStartLessonWindow();
        }
        else
        {
            OpenEndLessonWindow(closeAfter: false);
        }
    }

    private void OnOpenClassFolderClicked(object sender, RoutedEventArgs e)
    {
        if (_lesson?.Store.DirectoryPath is { } directory && Directory.Exists(directory))
        {
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
    }

    private void OpenStartLessonWindow()
    {
        IReadOnlyList<string> classes;
        try
        {
            classes = ClassStore.ListClasses(ClassStore.DefaultClassesDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            classes = Array.Empty<string>();
        }

        var window = new StartLessonWindow(classes) { Owner = this };
        if (window.ShowDialog() != true || window.ChosenClass is not { } className)
        {
            return;
        }

        var lesson = ClassLesson.Start(className, DateTime.UtcNow);
        try
        {
            Directory.CreateDirectory(new ClassStore(ClassStore.DefaultClassesDirectory, className).DirectoryPath);
            ClassLessonFile.Save(ClassLessonFile.DefaultPath, lesson);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"A aula não pôde começar: {ex.Message}", "Iniciar aula", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        StartSyncing(lesson);
        UpdateLessonBar();
        LessonTextBlock.Text = $"Aula: {className} · carregando a pasta da turma nas máquinas...";
    }

    private async void OpenEndLessonWindow(bool closeAfter)
    {
        if (_lesson is not { } syncer)
        {
            return;
        }

        var window = new EndLessonWindow(syncer, OnlineMachines, _masterCertificate) { Owner = this };
        window.ShowDialog();
        // The window stopped the rounds; whatever round was still finishing
        // is over before anything starts again.
        await syncer.StopAsync();
        syncer.Dispose();
        _lesson = null;
        if (window.Ended)
        {
            UpdateLessonBar();
            if (closeAfter)
            {
                _closeAfterLesson = true;
                Close();
            }
            return;
        }

        // Closed without ending: the same lesson goes on.
        StartSyncing(syncer.Lesson);
        UpdateLessonBar();
    }

    private void StartSyncing(ClassLesson lesson)
    {
        var store = new ClassStore(ClassStore.DefaultClassesDirectory, lesson.ClassName);
        var syncer = new ClassFolderSyncer(
            lesson, store, _masterCertificate, ClassLessonFile.DefaultPath, new WindowsRecycleBin(), OnlineMachines);
        syncer.RoundFinished += (time, results) => Dispatcher.InvokeAsync(() =>
        {
            if (_lesson == syncer)
            {
                ShowLessonRound(time, results);
            }
        });
        _lesson = syncer;
        syncer.Start();
    }

    // Every machine not known to be off (one still being reached for the
    // first time included, so a lesson resumed at start doesn't wait a round);
    // asked from the syncer's thread too.
    private IReadOnlyList<KnownMachine> OnlineMachines() =>
        Dispatcher.Invoke(() => _tiles.Where(tile => tile.Presence != TilePresence.Offline).Select(tile => tile.Machine).ToList());

    private void UpdateLessonBar()
    {
        LessonButton.Content = _lesson is null ? "Iniciar aula" : "Encerrar aula";
        LessonButton.ToolTip = _lesson is null
            ? "Escolhe a turma que está no laboratório: os arquivos dela vão para a \"Pasta da turma\" de cada máquina e o que os alunos salvarem lá é guardado neste computador."
            : "Salva as máquinas uma última vez, decide o que fazer com os arquivos da turma e esvazia a \"Pasta da turma\" das máquinas.";
        OpenClassFolderButton.Visibility = _lesson is null ? Visibility.Collapsed : Visibility.Visible;
        if (_lesson is { } syncer)
        {
            LessonTextBlock.Text = $"Aula: {syncer.Lesson.ClassName}";
        }
        else
        {
            LessonTextBlock.Text = string.Empty;
            LessonTextBlock.ToolTip = null;
        }
    }

    private void ShowLessonRound(DateTime time, IReadOnlyList<MachineSyncResult> results)
    {
        var saved = results.Count(result => result.Succeeded);
        LessonTextBlock.Text = results.Count == 0
            ? $"Aula: {_lesson!.Lesson.ClassName} · nenhuma máquina ligada"
            : $"Aula: {_lesson!.Lesson.ClassName} · pasta salva às {time:HH:mm} ({saved} de {results.Count} máquina(s))";
        LessonTextBlock.ToolTip = results.Count == 0
            ? null
            : string.Join("\n", results.Select(result => $"{(result.Succeeded ? "✓" : "✗")} {result.Name}: {result.Detail}"));
    }

    // Closing Master with a lesson going on: end it now, or leave it for
    // the next time Master opens (the students' files are only saved while
    // it's open).
    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_lesson is null || _closeAfterLesson)
        {
            return;
        }

        var answer = MessageBox.Show(this,
            $"A aula da turma {_lesson.Lesson.ClassName} ainda está aberta.\n\n" +
            "Sim: encerra a aula agora (salva as máquinas e esvazia a \"Pasta da turma\" delas).\n" +
            "Não: fecha o Midview e a aula continua quando ele abrir de novo. Enquanto ele estiver fechado, o que os alunos salvarem fica só nas máquinas.",
            "Encerrar aula?", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (answer == MessageBoxResult.No)
        {
            return;
        }
        e.Cancel = true;
        if (answer == MessageBoxResult.Yes)
        {
            Dispatcher.InvokeAsync(() => OpenEndLessonWindow(closeAfter: true));
        }
    }
}
