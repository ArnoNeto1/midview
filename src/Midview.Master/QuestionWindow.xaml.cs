using System.Windows;

namespace Midview.Master;

// A yes/no question whose buttons say what they do ("Atualizar agora",
// "Agora não"), which a MessageBox can't: its buttons are only Sim/Não.
// Closing the window or pressing Esc is the no.
public partial class QuestionWindow : MidviewWindow
{
    public QuestionWindow(string title, string question, string yes, string no)
    {
        InitializeComponent();
        Title = title;
        QuestionTextBlock.Text = question;
        YesButton.Content = yes;
        NoButton.Content = no;
    }

    // True only when the yes button was clicked.
    public static bool Ask(Window owner, string title, string question, string yes, string no) =>
        new QuestionWindow(title, question, yes, no) { Owner = owner }.ShowDialog() == true;

    private void OnYesClicked(object sender, RoutedEventArgs e) => DialogResult = true;
}
