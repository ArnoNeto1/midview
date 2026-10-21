using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderWindow: starting and ending a lesson, and what each PC answered.
public partial class ClassFolderWindow
{
    // "Começar a aula do <turma>": only from the list, so with no lesson on.
    private async void OnStartClicked(object sender, RoutedEventArgs e)
    {
        if (SelectedClass is not { } row || Lesson.IsOn)
        {
            ShowView();
            return;
        }
        var turma = row.Turma;

        CloseNameRow();
        SetBusy(true, $"Começando a aula do {turma}...");
        string headline;
        var lines = new List<string>();
        try
        {
            var started = await Task.Run(() => _sync.StartAsync(turma));
            headline = ClassFolderText.Outcome(started: true, turma, started.Count(result => result.Ok), started.Count, Offline(started));
            lines.AddRange(ResultLines(started));
        }
        catch (ArgumentException ex)
        {
            // The class was deleted meanwhile ("Essa turma não existe mais.").
            headline = ex.Message;
        }
        catch (Exception ex)
        {
            headline = $"Não foi possível começar a aula do {turma}: {ErrorText.Describe(ex)}";
        }
        finally
        {
            SetBusy(false);
        }
        // The view first: moving to the lesson clears what the list said.
        ShowView();
        ShowResults(headline, lines);
        RefreshFromDisk();
    }

    private void OnEndClicked(object sender, RoutedEventArgs e) => EndLesson(nextClass: false);

    // "Trocar de turma…": the lesson ends at once, as chosen, and the list comes back for the next class.
    private void OnSwitchClicked(object sender, RoutedEventArgs e) => EndLesson(nextClass: true);

    // The PCs' last files come in first (EndAsync's last rounds), so it can take a while.
    private async void EndLesson(bool nextClass)
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
            headline = ClassFolderText.Outcome(started: false, lesson.Turma, ended.Count(result => result.Ok), ended.Count, Offline(ended));
            if (nextClass)
            {
                headline += " " + ClassFolderText.ChooseNextClass;
            }
            lines.AddRange(ResultLines(ended));
            lines.AddRange(EndProblems());
        }
        catch (Exception ex)
        {
            headline = $"Não foi possível encerrar a aula do {lesson.Turma}: {ErrorText.Describe(ex)}";
        }
        finally
        {
            SetBusy(false);
        }
        // The view first: moving to the list clears what the lesson view said.
        ShowView();
        ShowResults(headline, lines);
        RefreshFromDisk();
        if (nextClass && !Lesson.IsOn)
        {
            ClassesList.Focus();
        }
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
        var question = ClassFolderText.EndQuestion(choice, lesson.Turma, files.Today, files.Files, files.TooBigForTheBin(choice));
        return question is null || MessageBox.Show(this, question, "Encerrar a aula", MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private static IEnumerable<string> ResultLines(IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)> results) =>
        results.Select(result => ClassFolderText.Result(result.Machine.Name, result.Ok, result.Detail));

    // The PCs that didn't take it because they were switched off (their poller hands it over later).
    private static int Offline(IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)> results) =>
        results.Count(result => !result.Ok && result.Detail == FailureText.Offline);

    // What the end couldn't do with the class's files on this PC.
    private IEnumerable<string> EndProblems() =>
        _sync.LastEndProblems.Select(problem => $"✗ Neste PC: {problem}");

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
