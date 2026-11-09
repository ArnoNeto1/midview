using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Midview.Master;

// The size the viewer window opens at: the shape of the student's screen, so
// no black band is left beside the picture.
internal static class ViewWindowFit
{
    // The room (in WPF units) the picture gets: one student pixel per teacher
    // pixel at most, shrunk to fit the room, never stretched out of shape.
    // Empty when there is no picture or no room.
    public static Size ClientSize(int pixelWidth, int pixelHeight, double dpiScaleX, double dpiScaleY, Size room)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0 || dpiScaleX <= 0 || dpiScaleY <= 0
            || room.IsEmpty || room.Width <= 0 || room.Height <= 0)
        {
            return Size.Empty;
        }

        var width = pixelWidth / dpiScaleX;
        var height = pixelHeight / dpiScaleY;
        var scale = Math.Min(1, Math.Min(room.Width / width, room.Height / height));
        return new Size(width * scale, height * scale);
    }

    // The screen area the window is on, without the taskbar, in WPF units;
    // the main screen's when Windows can't say.
    public static Rect WorkArea(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (hwnd != IntPtr.Zero && GetMonitorInfo(MonitorFromWindow(hwnd, MonitorDefaultToNearest), ref monitor))
            {
                var dpi = VisualTreeHelper.GetDpi(window);
                var work = monitor.Work;
                return new Rect(
                    work.Left / dpi.DpiScaleX, work.Top / dpi.DpiScaleY,
                    (work.Right - work.Left) / dpi.DpiScaleX, (work.Bottom - work.Top) / dpi.DpiScaleY);
            }
        }
        catch (Exception)
        {
            // No user32 answer: the main screen below.
        }
        return SystemParameters.WorkArea;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect32 Monitor;
        public Rect32 Work;
        public uint Flags;
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
