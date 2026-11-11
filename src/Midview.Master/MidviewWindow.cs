using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Midview.Master;

// The base of every Midview window: the dark surface, the text color and the
// font come from the theme (so a window that sets none of them looks right),
// the title bar is made dark once the window has a handle, and the window
// opens in its own color instead of white.
public class MidviewWindow : Window
{
    private const int EraseBackground = 0x0014; // WM_ERASEBKGND

    private static readonly Color DefaultCaption = Color.FromRgb(0x0F, 0x18, 0x28);

    public MidviewWindow()
    {
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SetResourceReference(FontFamilyProperty, "UiFont");
    }

    private Color Surface => (Background as SolidColorBrush)?.Color ?? DefaultCaption;

    private HwndSource? Source => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.Apply(this, Surface);
        Source?.AddHook(PaintBeforeTheFirstDrawing);
    }

    // From here on what is on the window is WPF's drawing, and an erase that
    // painted over it would blink: erasing is WPF's business again.
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Source?.RemoveHook(PaintBeforeTheFirstDrawing);
    }

    // As Windows shows a window it asks it to erase its background. WPF answers
    // "done" and paints nothing, so until its first drawing, a frame or two
    // later (more the first time a kind of window opens), the window was white.
    // Painting the window's own color here makes it open dark.
    private IntPtr PaintBeforeTheFirstDrawing(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != EraseBackground || !GetClientRect(hwnd, out var client))
            return IntPtr.Zero;

        var brush = CreateSolidBrush(DarkTitleBar.ColorRef(Surface));
        FillRect(wParam, ref client, brush);
        DeleteObject(brush);
        handled = true;
        return new IntPtr(1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hdc, ref Rect rect, IntPtr brush);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint colorRef);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);
}
