using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderWindow: starting and ending a lesson, "Trocar de turma…", and
// what each PC answered.
public partial class ClassFolderWindow
{
    // "Começar a aula do <turma>". While switching from a lesson that is on,
    // that lesson ends first as the teacher chose; with "Manter os arquivos"
    // the start alone replaces it (the sync keeps its files, and what its PCs
    // set aside comes in over the next rounds), so the PCs see one change.
    private async void OnStartClicked(object sender, RoutedEventArgs e)
    {
        if (SelectedClass is not { } row)
        {
            return;
        }
        var turma = row.Turma;
        var current = Lesson;
        if (current.IsOn && SameClass(current.Turma, turma))
        {
            BackToLesson();
            return;
        }

        var choice = SelectedChoice();
        var ending = current.IsOn && choice != EndChoice.Keep;
        if (ending && !await ConfirmEndAsync(current, choice))
        {
            return;
        }

        CloseNameRow();
        SetBusy(true, current.IsOn
            ? $"Encerrando a aula do {current.Turma} e começando a do {turma}..."
            : $"Começando a aula do {turma}...");
        var headline = string.Empty;
        var lines = new List<string>();
        try
        {
            if (ending)
            {
                var ended = await Task.Run(() => _sync.EndAsync(choice));
                headline = ClassFolderText.Outcome(started: false, current.Turma, ended.Count(result => result.Ok), ended.Count) + " ";
                lines.AddRange(EndProblems());
            }
            var started = await Task.Run(() => _sync.StartAsync(turma));
            headline += ClassFolderText.Outcome(started: true, turma, started.Count(result => result.Ok), started.Count);
            lines.InsertRange(0, ResultLines(started));
            _switching = false;
            KeepChoice.IsChecked = true;
        }
        catch (ArgumentException ex)
        {
            // The class was deleted meanwhile ("Essa turma não existe mais.").
            headline += ex.Message;
        }
        catch (Exception ex)
        {
            headline += $"Não foi possível começar a aula do {turma}: {ErrorText.Describe(ex)}";
        }
        finally
        {
            SetBusy(false);
        }
        ShowResults(headline, lines);
        ShowView();
        RefreshFromDisk();
    }

    // "Encerrar a aula": the PCs' last files come in first, so it can take a while.
    private async void OnEndClicked(object sender, RoutedEventArgs e)
    {
        var lesson = Lesson;
        if (!lesson.IsOn)
        {
            ShowView();
            return;
        }
        var choice = SelectedChoice();
        if (choice != EndChoice.Keep && !await ConfirmEndAsync(lesson, choice))
        {
            return;
        }

        SetBusy(true, $"Encerrando a aula do {lesson.Turma}: trazendo os últimos arquivos dos PCs...");
        string headline;
        var lines = new List<string>();
        try
        {
            var ended = await Task.Run(() => _sync.EndAsync(choice));
            headline = ClassFolderText.Outcome(started: false, lesson.Turma, ended.Count(result => result.Ok), ended.Count);
            lines.AddRange(ResultLines(ended));
            lines.AddRange(EndProblems());
            KeepChoice.IsChecked = true;
        }
        catch (Exception ex)
        {
            headline = $"Não foi possível encerrar a aula do {lesson.Turma}: {ErrorText.Describe(ex)}";
        }
        finally
        {
            SetBusy(false);
        }
        ShowResults(headline, lines);
        ShowView();
        RefreshFromDisk();
    }

    // Back to the list with the lesson still on.
    private void OnSwitchClicked(object sender, RoutedEventArgs e)
    {
        _switching = true;
        ShowView();
        RefreshFromDisk();
    }

    private void OnBackToLessonClicked(object sender, RoutedEventArgs e) => BackToLesson();

    private void BackToLesson()
    {
        _switching = false;
        CloseNameRow();
        ShowView();
        RefreshFromDisk();
    }

    private EndChoice SelectedChoice() =>
        DeleteAllChoice.IsChecked == true ? EndChoice.DeleteAll
        : DeleteTodayChoice.IsChecked == true ? EndChoice.DeleteToday
        : EndChoice.Keep;

    // Deleting asks first, saying how many files go (read now if not read yet).
    private async Task<bool> ConfirmEndAsync(ClassLesson lesson, EndChoice choice)
    {
        var files = _lessonFiles is { } read && SameClass(read.Turma, lesson.Turma)
            ? read
            : await Task.Run(() => ReadLessonFiles(lesson.Turma));
        if (files is null)
        {
            ShowResults("A pasta da turma deste PC não pôde ser lida agora. Tente de novo em instantes.", Array.Empty<string>());
            return false;
        }
        var question = ClassFolderText.EndQuestion(choice, lesson.Turma, files.Today, files.Files);
        return question is null || MessageBox.Show(this, question, "Encerrar a aula", MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private static IEnumerable<string> ResultLines(IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)> results) =>
        results.Select(result => ClassFolderText.Result(result.Machine.Name, result.Ok, result.Detail));

    // What the end couldn't do with the class's files on this PC.
    private IEnumerable<string> EndProblems()
    {
        var problems = _sync.LastEndProblems;
        return problems.Count == 0
            ? Array.Empty<string>()
            : problems.Select(problem => $"✗ Neste PC: {problem}");
    }

    private void SetBusy(bool busy, string? progress = null)
    {
        _busy = busy;
        if (progress is not null)
        {
            ShowResults(progress, Array.Empty<string>());
        }
        UpdateButtons();
    }

    private void ShowResults(string headline, IEnumerable<string> lines)
    {
        ProgressText.Text = headline;
        ResultText.Text = string.Join("\n", lines);
        ResultText.Visibility = ResultText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ResultsPanel.Visibility = headline.Length == 0 && ResultText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
