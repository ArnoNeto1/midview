using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Midview.Protocol;

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

    // The classes, the selection kept.
    private void ShowClasses(IReadOnlyList<ClassSummary> classes)
    {
        var select = _selectNext ?? SelectedClass?.Turma;
        _selectNext = null;
        var rows = classes.Select(summary => new ClassRow(summary, ClassFolderText.Summary(summary))).ToList();
        ClassesList.ItemsSource = rows;
        ClassesList.SelectedItem = rows.FirstOrDefault(row => SameClass(row.Turma, select));
        if (ClassesList.SelectedItem is { } selected)
        {
            ClassesList.ScrollIntoView(selected);
        }
        NoClassesText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }

    private static bool SameClass(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private bool IsLessonClass(string turma) => Lesson.IsOn && SameClass(Lesson.Turma, turma);

    private void OnClassSelected(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void OnNewClassClicked(object sender, RoutedEventArgs e) =>
        OpenNameRow(NameEdit.Create, null, "Nome da nova turma (por exemplo, 3º A):", "Criar", string.Empty);

    private void OnRenameClicked(object sender, RoutedEventArgs e)
    {
        if (SelectedClass is { } row && !IsLessonClass(row.Turma))
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
    // and says why not when it can't be one. Off the UI thread: it moves folders.
    private async void ConfirmName()
    {
        if (_busy)
        {
            return;
        }
        var name = NameTextBox.Text.Trim();
        var edit = _nameEdit;
        var turma = _renaming;
        if (edit == NameEdit.Rename && turma is not null && IsLessonClass(turma))
        {
            ShowNameError("A turma da aula em andamento não pode ser renomeada.");
            return;
        }

        SetBusy(true);
        (bool Ok, string Detail, string Selected, string Said) outcome;
        try
        {
            outcome = edit switch
            {
                NameEdit.Create => await Task.Run(() =>
                {
                    var created = _store.Create(name);
                    return (created.Ok, created.Detail, created.Turma, $"Turma {created.Turma} criada.");
                }),
                NameEdit.Rename when turma is not null => await Task.Run(() =>
                {
                    var renamed = _store.Rename(turma, name);
                    var stored = ClassStore.CleanName(name);
                    return (renamed.Ok, renamed.Detail, stored, ClassFolderText.Renamed(turma, stored));
                }),
                _ => (false, string.Empty, string.Empty, string.Empty),
            };
        }
        catch (Exception ex)
        {
            outcome = (false, ErrorText.Describe(ex), string.Empty, string.Empty);
        }
        finally
        {
            SetBusy(false);
        }

        if (outcome.Ok)
        {
            CloseNameRow();
            _selectNext = outcome.Selected;
            ShowResults(outcome.Said, Array.Empty<string>());
            RefreshFromDisk();
        }
        else if (outcome.Detail.Length > 0)
        {
            ShowNameError(outcome.Detail);
        }
        else
        {
            CloseNameRow();
        }
    }

    private void ShowNameError(string detail)
    {
        NameErrorText.Text = detail;
        NameErrorText.Visibility = Visibility.Visible;
    }

    // Asks first, naming how many files go; they go to this PC's Recycle Bin, or
    // for good when they are more than it holds (and then the question says so).
    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (SelectedClass is not { } row || IsLessonClass(row.Turma))
        {
            return;
        }
        var tooBigForTheBin = row.Class.Bytes > _store.RecycleBinBytes();
        if (MessageBox.Show(this, ClassFolderText.DeleteQuestion(row.Class, tooBigForTheBin), "Excluir turma",
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
            deleted = (false, "Não foi possível excluir a turma: " + ErrorText.Describe(ex));
        }
        finally
        {
            SetBusy(false);
        }
        ShowResults(deleted.Ok ? ClassFolderText.Deleted(row.Class, tooBigForTheBin) : deleted.Detail, Array.Empty<string>());
        RefreshFromDisk();
    }
}
