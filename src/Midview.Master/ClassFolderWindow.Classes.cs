using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Midview.Master;

// ClassFolderWindow: the class list, and creating, renaming and deleting a class.
public partial class ClassFolderWindow
{
    // What the name row is asking for: a new class, or a new name for _renaming.
    private enum NameEdit
    {
        None,
        Create,
        Rename,
    }

    private NameEdit _nameEdit;
    private string? _renaming;

    // The class to select once the list is read again (one just created or renamed).
    private string? _selectNext;

    private ClassRow? SelectedClass => ClassesList.SelectedItem as ClassRow;

    // Null when the classes' folder can't be read just now (the list stays as it was).
    private IReadOnlyList<ClassSummary>? ReadClasses()
    {
        try
        {
            return _store.List();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The classes, the one of the lesson on marked, the selection kept.
    private void ShowClasses(IReadOnlyList<ClassSummary> classes)
    {
        var select = _selectNext ?? SelectedClass?.Turma;
        _selectNext = null;
        var lesson = Lesson;
        var rows = classes
            .Select(summary => new ClassRow(
                summary, ClassFolderText.Summary(summary), lesson.IsOn && SameClass(summary.Turma, lesson.Turma)))
            .ToList();
        ClassesList.ItemsSource = rows;
        ClassesList.SelectedItem = rows.FirstOrDefault(row => SameClass(row.Turma, select));
        if (ClassesList.SelectedItem is { } selected)
        {
            ClassesList.ScrollIntoView(selected);
        }
        NoClassesText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }

    // The lesson started or ended: the mark moves, without reading the disk.
    private void MarkLessonClass()
    {
        if (ClassesList.ItemsSource is IEnumerable<ClassRow> rows)
        {
            ShowClasses(rows.Select(row => row.Class).ToList());
        }
    }

    private static bool SameClass(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void OnClassSelected(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void OnNewClassClicked(object sender, RoutedEventArgs e) =>
        OpenNameRow(NameEdit.Create, null, "Nome da nova turma (por exemplo, 3º A):", "Criar", string.Empty);

    private void OnRenameClicked(object sender, RoutedEventArgs e)
    {
        if (SelectedClass is { IsLessonOn: false } row)
        {
            OpenNameRow(NameEdit.Rename, row.Turma, $"Novo nome para a turma {row.Turma}:", "Renomear", row.Turma);
        }
    }

    private void OpenNameRow(NameEdit edit, string? renaming, string prompt, string ok, string text)
    {
        _nameEdit = edit;
        _renaming = renaming;
        NamePrompt.Text = prompt;
        NameOkButton.Content = ok;
        NameTextBox.Text = text;
        NameErrorText.Visibility = Visibility.Collapsed;
        NamePanel.Visibility = Visibility.Visible;
        NameTextBox.Focus();
        NameTextBox.SelectAll();
    }

    private void CloseNameRow()
    {
        _nameEdit = NameEdit.None;
        _renaming = null;
        NamePanel.Visibility = Visibility.Collapsed;
    }

    private void OnNameCancelClicked(object sender, RoutedEventArgs e) => CloseNameRow();

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ConfirmName();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseNameRow();
            e.Handled = true;
        }
    }

    private void OnNameOkClicked(object sender, RoutedEventArgs e) => ConfirmName();

    // The store makes the name safe for a folder ("Turma 1/2" is "Turma 1-2")
    // and says why not when it can't be one.
    private void ConfirmName()
    {
        var name = NameTextBox.Text.Trim();
        string detail;
        switch (_nameEdit)
        {
            case NameEdit.Create:
                var created = _store.Create(name);
                if (created.Ok)
                {
                    CloseNameRow();
                    _selectNext = created.Turma;
                    ShowResults($"Turma {created.Turma} criada.", Array.Empty<string>());
                    RefreshFromDisk();
                    return;
                }
                detail = created.Detail;
                break;

            case NameEdit.Rename when _renaming is { } turma:
                if (Lesson.IsOn && SameClass(Lesson.Turma, turma))
                {
                    detail = "A turma da aula em andamento não pode ser renomeada.";
                    break;
                }
                var renamed = _store.Rename(turma, name);
                if (renamed.Ok)
                {
                    CloseNameRow();
                    _selectNext = name;
                    ShowResults($"Turma {turma} renomeada.", Array.Empty<string>());
                    RefreshFromDisk();
                    return;
                }
                detail = renamed.Detail;
                break;

            default:
                CloseNameRow();
                return;
        }

        NameErrorText.Text = detail;
        NameErrorText.Visibility = Visibility.Visible;
    }

    // Asks first, naming how many files go; they go to this PC's Recycle Bin.
    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (SelectedClass is not { IsLessonOn: false } row || Lesson.IsOn && SameClass(Lesson.Turma, row.Turma))
        {
            return;
        }
        if (MessageBox.Show(this, ClassFolderText.DeleteQuestion(row.Class), "Excluir turma",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        CloseNameRow();
        SetBusy(true, $"Excluindo a turma {row.Turma}...");
        (bool Ok, string Detail) deleted;
        try
        {
            deleted = await Task.Run(() => _store.Delete(row.Turma));
        }
        catch (Exception ex)
        {
            deleted = (false, "Não foi possível excluir a turma: " + Midview.Protocol.ErrorText.Describe(ex));
        }
        finally
        {
            SetBusy(false);
        }
        ShowResults(
            deleted.Ok ? $"Turma {row.Turma} excluída. Os arquivos dela estão na Lixeira deste PC." : deleted.Detail,
            Array.Empty<string>());
        RefreshFromDisk();
    }
}
