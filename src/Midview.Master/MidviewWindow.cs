using System.Windows;
using System.Windows.Media;

namespace Midview.Master;

// The base of every Midview window: the dark surface, the text color and the
// font come from the theme (so a window that sets none of them looks right),
// and the title bar is made dark once the window has a handle.
public class MidviewWindow : Window
{
    private static readonly Color DefaultCaption = Color.FromRgb(0x0F, 0x18, 0x28);

    public MidviewWindow()
    {
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SetResourceReference(FontFamilyProperty, "UiFont");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.Apply(this, (Background as SolidColorBrush)?.Color ?? DefaultCaption);
    }
}
