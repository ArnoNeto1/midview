using System.Windows;

namespace Midview.Master;

// Midview's own MessageBox: same arguments, answers and button order, drawn in
// the Painel look. Closing the window or pressing Esc answers No in a YesNo box
// and OK in an OK box, as the system one does.
public static class MidviewMessage
{
    public static MessageBoxResult Show(
        Window? owner, string text, string title, MessageBoxButton buttons, MessageBoxImage image,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var (yes, no) = Labels(buttons);
        var content = new QuestionContent(
            title, IconFor(image), title, yes, no, Body: text, NoIsDefault: defaultResult == MessageBoxResult.No,
            Buttons: ButtonsFor(buttons, image, defaultResult));
        var answeredYes = QuestionWindow.Ask(owner, content);
        if (buttons == MessageBoxButton.YesNo)
        {
            return answeredYes ? MessageBoxResult.Yes : MessageBoxResult.No;
        }
        return MessageBoxResult.OK;
    }

    // For a message shown when no window is left to own it.
    public static MessageBoxResult Show(string text, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Show(null, text, title, buttons, image);

    // Only the two button sets the app uses.
    internal static (string Yes, string? No) Labels(MessageBoxButton buttons) =>
        buttons == MessageBoxButton.YesNo ? ("Sim", "Não") : ("OK", null);

    // Windows' order, "Sim" before "Não". A "Sim" that a warning asks about, or
    // that the box doesn't offer as its default, is drawn as dangerous.
    internal static QuestionButtons ButtonsFor(MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult) =>
        buttons == MessageBoxButton.YesNo && (image == MessageBoxImage.Warning || defaultResult == MessageBoxResult.No)
            ? QuestionButtons.YesFirstDanger
            : QuestionButtons.YesFirst;

    // Glyphs of the icon font: info, warning, error, question.
    internal static string IconFor(MessageBoxImage image) => image switch
    {
        MessageBoxImage.Warning => "",
        MessageBoxImage.Error => "",
        MessageBoxImage.Question => "",
        _ => "",
    };
}
