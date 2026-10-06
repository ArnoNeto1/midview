using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Midview.Master;

// Opening Midview while it's already open (the shortcut again, the Start
// menu): the copy already running comes to the front, restored if it was
// minimized, and the new one goes away. Two would each poll the same
// machines and fight over their screens.
internal static class OtherInstance
{
    private const int SwRestore = 9;

    // The other Midview of this Windows session, if it has a window yet.
    public static void BringBack()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var other in Process.GetProcessesByName(current.ProcessName))
        {
            using (other)
            {
                if (other.Id == current.Id || other.SessionId != current.SessionId || other.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }
                if (IsIconic(other.MainWindowHandle))
                {
                    ShowWindow(other.MainWindowHandle, SwRestore);
                }
                SetForegroundWindow(other.MainWindowHandle);
                return;
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
