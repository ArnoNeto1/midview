using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Midview.Master;

// A button of the classroom window's toolbar: a Windows icon over a short
// label, and optionally a small badge on the icon (a lock for locking the
// screens, a "no" sign for blocking programs and sites). Icon and Badge are
// glyphs of the Segoe Fluent Icons font (Segoe MDL2 Assets on Windows 10);
// the look is in Themes/Painel.Main.xaml. OpensOptions puts a small down
// arrow by the icon, for the buttons that open a window of choices instead
// of acting right away. Caption is a short text in a small pill under the
// label ("3º A" on "Pasta da turma" while a lesson is on), shown only when
// not empty; it takes the label's second line, so it goes on a button whose
// label fits on one. IconBrush is the color of the button's group, set once
// on the group's panel and inherited by its buttons and its caption's dot.
// BadgeInScreen draws the badge inside the icon's screen instead of on its
// corner, for a monitor icon ("Bloquear telas": the padlock in the screen).
public sealed class ToolbarButton : Button
{
    public static readonly DependencyProperty IconBrushProperty =
        DependencyProperty.RegisterAttached(nameof(IconBrush), typeof(Brush), typeof(ToolbarButton),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static Brush? GetIconBrush(DependencyObject element) => (Brush?)element.GetValue(IconBrushProperty);

    public static void SetIconBrush(DependencyObject element, Brush? value) => element.SetValue(IconBrushProperty, value);

    public Brush? IconBrush
    {
        get => (Brush?)GetValue(IconBrushProperty);
        set => SetValue(IconBrushProperty, value);
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(ToolbarButton), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BadgeProperty =
        DependencyProperty.Register(nameof(Badge), typeof(string), typeof(ToolbarButton), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(ToolbarButton), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty OpensOptionsProperty =
        DependencyProperty.Register(nameof(OpensOptions), typeof(bool), typeof(ToolbarButton), new PropertyMetadata(false));

    public static readonly DependencyProperty BadgeInScreenProperty =
        DependencyProperty.Register(nameof(BadgeInScreen), typeof(bool), typeof(ToolbarButton), new PropertyMetadata(false));

    public bool BadgeInScreen
    {
        get => (bool)GetValue(BadgeInScreenProperty);
        set => SetValue(BadgeInScreenProperty, value);
    }

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
