using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Midview.Session;

// Putting a Midview window over everything else on the student's screen,
// for the lock screen, the teacher's messages and the countdown banner.
// TopMost alone isn't enough: another program's own topmost window, or
// one that was already active, can still come up over it.
internal static class KeepOnTop
{
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    // Over every other window, without taking the keyboard: the student
    // can keep typing in their own program.
    public static void Raise(Form form) =>
        SetWindowPos(form.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    // Over every other window, and the active one. The taskbar only steps
    // behind a full-screen window that is the active one -- the first live
    // run had it showing over the lock -- and Windows doesn't let a program
    // in the background activate its window just like that. Joining the
    // active window's input for a moment lets it, which also takes focus
    // from the student's program.
    public static void ComeToFront(Form form)
    {
        var handle = form.Handle;
        SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        var foreground = GetForegroundWindow();
        if (foreground == handle)
        {
            return;
        }

        var ownThread = GetCurrentThreadId();
        var foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, IntPtr.Zero);
        var attached = foregroundThread != 0 && foregroundThread != ownThread
                       && AttachThreadInput(ownThread, foregroundThread, true);
        try
        {
            SetForegroundWindow(handle);
            form.Activate();
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(ownThread, foregroundThread, false);
            }
        }
    }

    public static bool IsActive(Form form) => GetForegroundWindow() == form.Handle;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool join);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
