using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Midview.Session;

namespace Midview.Desk;

// Captures the screen of whichever desktop the calling thread is attached to.
// That is why it is raw GDI and keeps nothing between frames: a display DC is
// made per call, and BitBlt reads whatever desktop the thread is attached to
// at the moment it runs (a probe showed the binding is at BitBlt time, not
// CreateDC time). Here that distinction is harmless because CreateDC and BitBlt
// run on the same thread in one Draw() with no SetThreadDesktop between, so
// once the helper's capture thread has done SetThreadDesktop to the secure
// (UAC) desktop, the next frame is that desktop's, and the user's again when
// the thread moves back. GdiScreenCapture, which goes through WinForms'
// Screen and Graphics.CopyFromScreen, is for the user's desktop.
//
// The size is what GetSystemMetrics says to the calling thread, so the process
// has to be DPI aware, which the helper's Main sets before the first capture:
// otherwise a scaled display gives only the screen's top-left part. A frame is
// 32-bit ARGB like GdiScreenCapture's (TileDiff reads that without
// converting). Windows keeps the cursor off the screen's pixels, so
// CursorOverlay draws it onto each frame, as it does for GdiScreenCapture.
public sealed class DeskScreenCapture : IReusableScreenCapture
{
    private const int SmCxScreen = 0; // SM_CXSCREEN
    private const int SmCyScreen = 1; // SM_CYSCREEN
    private const int SrcCopy = 0x00CC0020; // SRCCOPY

    public Bitmap CaptureFrame()
    {
        var size = ScreenSize();
        if (size.Width <= 0 || size.Height <= 0)
        {
            // No screen on this desktop (a service's session, say). Bitmap
            // would throw an error that doesn't tell that.
            throw new InvalidOperationException("There is no screen to capture on this desktop.");
        }

        var frame = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        try
        {
            Draw(frame);
            return frame;
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    public bool TryCaptureInto(Bitmap target)
    {
        var size = ScreenSize();
        if (target.Width != size.Width || target.Height != size.Height)
        {
            return false;
        }

        Draw(target);
        return true;
    }

    private static Size ScreenSize() => new(GetSystemMetrics(SmCxScreen), GetSystemMetrics(SmCyScreen));

    // Copies the screen's top-left bitmap.Width x bitmap.Height pixels into
    // bitmap, then draws the cursor over them. They go straight into the
    // bitmap: a GDI bitmap in between would be one more full-screen allocation
    // and copy per frame, which TryCaptureInto exists to avoid.
    private static void Draw(Bitmap bitmap)
    {
        // Made for each frame, not kept. The desktop it reads is bound at the
        // BitBlt below, not here, but both run on this one thread with no
        // SetThreadDesktop between, so it reads the thread's current desktop.
        var screen = CreateDC("DISPLAY", null, null, IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            var target = graphics.GetHdc();
            try
            {
                if (!BitBlt(target, 0, 0, bitmap.Width, bitmap.Height, screen, 0, 0, SrcCopy))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                graphics.ReleaseHdc(target);
            }

            // The cursor needs the Graphics' DC again, so only now that this
            // one is given back. The primary screen's corner is always 0,0.
            CursorOverlay.Draw(graphics, Point.Empty);
        }
        finally
        {
            // CreateDC's DCs go back with DeleteDC; ReleaseDC is for GetDC's.
            DeleteDC(screen);
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("gdi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateDCW")]
    private static extern IntPtr CreateDC(string driver, string? device, string? port, IntPtr initData);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(
        IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int operation);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);
}
