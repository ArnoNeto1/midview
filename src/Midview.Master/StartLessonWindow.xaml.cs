using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Midview.Master;

// "Iniciar aula": the teacher picks the class that's in the lab (the ones
// kept so far, in ClassStore), or names a new one. ChosenClass is set when
// the window closes with "Iniciar aula".
public partial class StartLessonWindow : Window
{
    public StartLessonWindow(IReadOnlyList<string> classes)
    {
        InitializeComponent();
        ClassesListBox.ItemsSource = classes;
    }

    public string? ChosenClass { get; private set; }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (ClassesListBox.Items.Count == 0)
        {
            NewClassTextBox.Focus();
        }
        else
        {
            ClassesListBox.Focus();
        }
    }

    private void OnClassSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ClassesListBox.SelectedItem is not null && NewClassTextBox.Text.Length > 0)
        {
            NewClassTextBox.Text = string.Empty;
        }
        UpdateStartButton();
    }

    private void OnNewClassChanged(object sender, TextChangedEventArgs e)
    {
        if (NewClassTextBox.Text.Length > 0)
        {
            ClassesListBox.SelectedItem = null;
        }
        UpdateStartButton();
    }

    private void OnClassDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (ClassesListBox.SelectedItem is not null)
        {
            Start();
        }
    }

    private void OnStartClicked(object sender, RoutedEventArgs e) => Start();

    private void UpdateStartButton()
    {
        ProblemTextBlock.Text = string.Empty;
        StartButton.IsEnabled = ClassesListBox.SelectedItem is not null || NewClassTextBox.Text.Trim().Length > 0;
    }

    private void Start()
    {
        var name = ClassesListBox.SelectedItem as string ?? NewClassTextBox.Text.Trim();
        if (name.Length == 0)
        {
            return;
        }

        // The class is kept in a folder of that name: what Windows doesn't
        // take in a folder name is said before anything is created.
        // (Changing the text box clears the message, so the text goes first.)
        var folderName = ClassStore.FolderName(name).TrimStart('.');
        if (folderName != name)
        {
            NewClassTextBox.Text = folderName;
            ProblemTextBlock.Text = folderName.Length == 0
                ? "Escreva o nome da turma com letras ou números."
                : $"Esse nome não serve para uma pasta do Windows. Que tal \"{folderName}\"?";
            NewClassTextBox.Focus();
            NewClassTextBox.SelectAll();
            return;
        }

        ChosenClass = name;
        DialogResult = true;
    }
}
