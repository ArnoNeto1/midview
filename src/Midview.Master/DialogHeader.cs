using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Midview.Master;

// The top of a dialog: a rounded square holding a glyph, the heading beside it
// and, below, whatever the header is given as content (the hint text). The
// template lives in Themes/Painel.Dialogs.xaml.
public sealed class DialogHeader : ContentControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(DialogHeader), new PropertyMetadata(""));

    public static readonly DependencyProperty HeadingProperty =
        DependencyProperty.Register(nameof(Heading), typeof(string), typeof(DialogHeader), new PropertyMetadata(""));

    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(DialogHeader), new PropertyMetadata(Brushes.Transparent));

    public DialogHeader()
    {
        SetResourceReference(AccentProperty, "AccentBrush");
    }

    // A glyph of the icon font; empty hides the square.
    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Heading
    {
        get => (string)GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    // Color of the glyph and, at 0.18 opacity, of the square behind it.
    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }
}
