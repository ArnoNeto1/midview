using System.Drawing;
using System.Runtime.InteropServices;

namespace Midview.Session;

// Draws the mouse cursor onto a captured frame. Windows draws the cursor on
// a layer of its own, above the screen, so CopyFromScreen never has it:
// without this the teacher couldn't see where the student is pointing.
// Whatever cursor is showing (arrow, hand, I-beam, hourglass) is drawn; a
// hidden one (while typing, in a game) isn't.
public static class CursorOverlay
{
    private const int CursorShowing = 0x1;
    private const int DiNormal = 0x3;

    // Where the cursor's image goes on a frame whose top-left corner is
    // screenOrigin: its hot spot (the arrow's tip, the I-beam's middle) on
    // the cursor's position.
    public static Point ImagePosition(Point cursor, Point hotspot, Point screenOrigin) =>
        new(cursor.X - hotspot.X - screenOrigin.X, cursor.Y - hotspot.Y - screenOrigin.Y);

    public static void Draw(Graphics graphics, Point screenOrigin)
    {
        var info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref info) || (info.flags & CursorShowing) == 0 || info.hCursor == IntPtr.Zero
            || !GetIconInfo(info.hCursor, out var icon))
        {
            return;
        }

        try
        {
            var at = ImagePosition(new Point(info.ptScreenPos.X, info.ptScreenPos.Y), new Point(icon.xHotspot, icon.yHotspot), screenOrigin);
            var hdc = graphics.GetHdc();
            try
            {
                DrawIconEx(hdc, at.X, at.Y, info.hCursor, 0, 0, 0, IntPtr.Zero, DiNormal);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }
        finally
        {
            // GetIconInfo hands over copies of the cursor's bitmaps.
            if (icon.hbmMask != IntPtr.Zero)
            {
                DeleteObject(icon.hbmMask);
            }
            if (icon.hbmColor != IntPtr.Zero)
            {
                DeleteObject(icon.hbmColor);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CURSORINFO info);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(
        IntPtr hdc, int x, int y, IntPtr icon, int width, int height, int step, IntPtr brush, int flags);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
