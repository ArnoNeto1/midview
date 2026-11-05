using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Midview.Master;

// A question whose buttons say what they do ("Atualizar agora", "Agora não"),
// which a MessageBox can't: its buttons are only Sim/Não. Also the base of
// Midview's own message boxes (see MidviewMessage). Closing the window or
// pressing Esc is the no (or the only button, when there is no "no").
public partial class QuestionWindow : MidviewWindow
{
    public QuestionWindow(QuestionContent content)
    {
        InitializeComponent();
        Title = content.Title;
        Header.Icon = content.Icon;
        Header.Heading = content.Heading;
        if (content.Versions is { } versions)
        {
            FromChip.Content = versions.From;
            ToChip.Content = versions.To;
            VersionsPanel.Visibility = Visibility.Visible;
        }
        if (content.Facts is { Count: > 0 } facts)
        {
            FactsList.ItemsSource = facts;
        }
        if (!string.IsNullOrEmpty(content.Body))
        {
            BodyTextBlock.Text = content.Body;
            BodyTextBlock.Visibility = Visibility.Visible;
        }
        YesButton.Content = content.Yes;
        var (yesFirst, yesStyle) = Arrangement(content.Buttons);
        YesButton.Style = (Style)FindResource(yesStyle);
        if (yesFirst)
        {
            ButtonsPanel.Children.Remove(YesButton);
            ButtonsPanel.Children.Insert(0, YesButton);
            // The gap goes after the first button, and only when there are two.
            YesButton.Margin = content.No is null ? new Thickness(0) : NoButton.Margin;
            NoButton.Margin = new Thickness(0);
        }

        Button defaultButton = YesButton;
        if (content.No is { } no)
        {
            NoButton.Content = no;
            if (content.NoIsDefault)
            {
                defaultButton = NoButton;
            }
        }
        else
        {
            // Only one button: it answers Esc as well.
            NoButton.Visibility = Visibility.Collapsed;
            NoButton.IsCancel = false;
            YesButton.IsCancel = true;
        }
        YesButton.IsDefault = defaultButton == YesButton;
        NoButton.IsDefault = defaultButton == NoButton;
        FocusManager.SetFocusedElement(this, defaultButton);
    }

    // True only when the yes button was clicked. Without an owner the window
    // opens in the middle of the screen.
    public static bool Ask(Window? owner, QuestionContent content)
    {
        var window = new QuestionWindow(content);
        if (owner is null)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.ShowInTaskbar = true;
        }
        else
        {
            window.Owner = owner;
        }
        return window.ShowDialog() == true;
    }

    public static bool Ask(Window owner, string title, string question, string yes, string no) =>
        Ask(owner, new QuestionContent(title, "", question, yes, no));

    // Whether the yes button comes first, and its style (a resource key).
    internal static (bool YesFirst, string YesStyle) Arrangement(QuestionButtons buttons) => buttons switch
    {
        QuestionButtons.YesFirst => (true, "PrimaryButton"),
        QuestionButtons.YesFirstDanger => (true, "DangerButton"),
        _ => (false, "PrimaryButton"),
    };

    private void OnYesClicked(object sender, RoutedEventArgs e) => DialogResult = true;
}
