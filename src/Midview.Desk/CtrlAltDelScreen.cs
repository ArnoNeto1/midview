using System.Diagnostics;
using System.Runtime.InteropServices;
using Midview.Protocol;
using Midview.Session;

namespace Midview.Desk;

// The real sources and the key press CtrlAltDelDismisser is wired to in
// Program.cs. Verified live on a lab machine, not by unit tests (they need the
// Winlogon desktop and a SYSTEM process), except ScreenLocked.
internal static class CtrlAltDelScreen
{
    // How often the loop in Program.cs polls: often enough that the screen
    // closes in well under a second, cheap enough for a helper always running
    // (one desktop name read per poll while nothing is locked or open).
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private const ushort VkEscape = 0x1B;

    // Whether the Agent says the teacher has the screen locked (ScreenLockSignal).
    // No event (no Agent, or an Agent from before this) reads as unlocked.
    public static bool ScreenLocked(string name = ScreenLockEvent.Name)
    {
        if (!EventWaitHandle.TryOpenExisting(name, out var signal))
        {
            return false;
        }
        using (signal)
        {
            return signal.WaitOne(0);
        }
    }

    // The name of the desktop taking the keyboard and mouse ("Default", or
    // "Winlogon" for the Ctrl+Alt+Del screen, the UAC prompt and sign-in), or
    // null when it can't be opened right now.
    public static string? InputDesktopName()
    {
        var desktop = DeskNativeMethods.OpenInputDesktop(0, false, DeskNativeMethods.GenericAll);
        if (desktop == IntPtr.Zero)
        {
            return null;
        }
        try
        {
            return DeskNativeMethods.DesktopName(desktop);
        }
        finally
        {
            DeskNativeMethods.CloseDesktop(desktop);
        }
    }

    // A user's shell is running at the console: someone is logged on, so the
    // secure desktop isn't the sign-in screen.
    public static bool SomeoneLoggedOn() => RunningAtTheConsole("explorer");

    // The UAC prompt (consent.exe) is up at the console.
    public static bool UacPromptUp() => RunningAtTheConsole("consent");

    // Esc on the secure desktop, from a short-lived thread attached to it:
    // SendInput only reaches the desktop its thread is on, and a thread that is
    // gone holds no desktop, so the handle can be closed right after.
    public static void PressEscape(IInputInjector injector)
    {
        var desktop = DeskNativeMethods.OpenInputDesktop(0, false, DeskNativeMethods.GenericAll);
        if (desktop == IntPtr.Zero)
        {
            throw new InvalidOperationException($"OpenInputDesktop failed (Win32 {Marshal.GetLastWin32Error()})");
        }
        try
        {
            if (DeskNativeMethods.DesktopName(desktop) != CtrlAltDelDismisser.SecureDesktopName)
            {
                return; // closed in the meantime
            }

            Exception? failure = null;
            var thread = new Thread(() =>
            {
                if (!DeskNativeMethods.SetThreadDesktop(desktop))
                {
                    failure = new InvalidOperationException(
                        $"SetThreadDesktop failed (Win32 {Marshal.GetLastWin32Error()})");
                    return;
                }
                injector.Inject(new InputEvent(InputEventKind.KeyDown, 0, 0, VkEscape));
                injector.Inject(new InputEvent(InputEventKind.KeyUp, 0, 0, VkEscape));
            })
            {
                IsBackground = true,
                Name = "Ctrl+Alt+Del Esc",
            };
            thread.Start();
            thread.Join();
            if (failure is not null)
            {
                throw failure;
            }
        }
        finally
        {
            DeskNativeMethods.CloseDesktop(desktop);
        }
    }

    private static bool RunningAtTheConsole(string processName)
    {
        var console = WTSGetActiveConsoleSessionId();
        var found = false;
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                found |= (uint)process.SessionId == console;
            }
        }
        return found;
    }

    // 0xFFFFFFFF while no session is attached to the console (mid-switch).
    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}
