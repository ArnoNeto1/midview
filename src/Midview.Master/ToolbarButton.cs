using System.Windows;
using System.Windows.Controls;

namespace Midview.Master;

// A button of the classroom window's toolbar: a Windows icon over a short
// label, and optionally a small badge on the icon (a lock for locking the
// screens, a "no" sign for blocking programs and sites). Icon and Badge are
// glyphs of the Segoe Fluent Icons font (Segoe MDL2 Assets on Windows 10);
// the look is in App.xaml. OpensOptions puts a small down arrow by the
// icon, for the buttons that open a window of choices instead of acting
// right away. Caption is a short text in a small pill under the label
// ("3º A" on "Pasta da turma" while a lesson is on), shown only when not
// empty; it takes the label's second line, so it goes on a button whose
// label fits on one.
public sealed class ToolbarButton : Button
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(ToolbarButton), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BadgeProperty =
        DependencyProperty.Register(nameof(Badge), typeof(string), typeof(ToolbarButton), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(ToolbarButton), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty OpensOptionsProperty =
        DependencyProperty.Register(nameof(OpensOptions), typeof(bool), typeof(ToolbarButton), new PropertyMetadata(false));

    public bool OpensOptions
    {
        get => (bool)GetValue(OpensOptionsProperty);
        set => SetValue(OpensOptionsProperty, value);
    }

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Badge
    {
        get => (string)GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }
}
