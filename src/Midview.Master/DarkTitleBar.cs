using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Midview.Master;

// Makes a window's title bar dark through the Windows desktop window manager.
// On a Windows that does not know the attributes the bar stays as Windows
// draws it, which the design accepts.
internal static class DarkTitleBar
{
    private const int UseImmersiveDarkModeBefore20H1 = 19;
    private const int UseImmersiveDarkMode = 20;
    private const int CaptionColor = 35;
    private const int Windows11Build = 22000;

    public static void Apply(Window window, Color caption)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            int on = 1;
            if (DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, UseImmersiveDarkModeBefore20H1, ref on, sizeof(int));

            if (Environment.OSVersion.Version.Build >= Windows11Build)
            {
                int color = unchecked((int)ColorRef(caption));
                DwmSetWindowAttribute(hwnd, CaptionColor, ref color, sizeof(int));
            }
        }
        catch (Exception)
        {
            // No dwmapi, or a Windows without these attributes: keep the default bar.
        }
    }

    // Windows takes a color as a COLORREF: 0x00BBGGRR.
    internal static uint ColorRef(Color color) =>
        (uint)color.R | ((uint)color.G << 8) | ((uint)color.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
