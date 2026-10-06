using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Midview.Session;

// Closes the programs on the student's desktop. Session runs as the student,
// so it can only ever touch the student's own programs -- nothing elevated,
// nothing belonging to another user or to Windows itself.
public sealed class WindowsAppCloser : IAppCloser
{
    public (int Asked, int Forced) CloseAll(bool force)
    {
        using var self = Process.GetCurrentProcess();
        var targets = AppClosePolicy.WindowsToClose(EnumerateTopLevelWindows(), self.Id);

        // Forcing ends a program outright, so its windows aren't asked; the
        // ones whose program is never ended (explorer, the Store apps' frame
        // host) still get the normal close.
        var toEnd = force
            ? targets.Where(window => AppClosePolicy.MayForceClose(window.ProcessName)).ToList()
            : new List<TopLevelWindow>();
        var toAsk = targets.Except(toEnd).ToList();

        // Counts only the windows the request reached: Windows refuses it for
        // an elevated program's window, and a window may close meanwhile.
        var asked = toAsk.Count(window => PostMessage(window.Handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero));

        var forced = 0;
        foreach (var processId in toEnd.Select(window => window.ProcessId).Distinct())
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                // Only ever in this same login session, as a last safety net.
                if (!process.HasExited && process.SessionId == self.SessionId)
                {
                    process.Kill();
                    forced++;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                           or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Already gone, or not ours to end (e.g. elevated).
            }
        }
        return (asked, forced);
    }

    internal static List<TopLevelWindow> EnumerateTopLevelWindows()
    {
        var windows = new List<TopLevelWindow>();
        var processNames = new Dictionary<int, string>();
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var processId);
            if (!processNames.TryGetValue((int)processId, out var processName))
            {
                processName = ProcessNameOf((int)processId);
                processNames[(int)processId] = processName;
            }

            windows.Add(new TopLevelWindow(
                handle, (int)processId, processName, ClassNameOf(handle), TitleOf(handle),
                IsVisible: IsWindowVisible(handle),
                IsCloaked: DwmGetWindowAttribute(handle, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0,
                HasOwner: GetWindow(handle, GW_OWNER) != IntPtr.Zero,
                IsToolWindow: (GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0));
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static string ProcessNameOf(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    private static string TitleOf(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length == 0)
        {
            return string.Empty;
        }
        var text = new StringBuilder(length + 1);
        GetWindowText(handle, text, text.Capacity);
        return text.ToString();
    }

    private static string ClassNameOf(IntPtr handle)
    {
        var text = new StringBuilder(256);
        GetClassName(handle, text, text.Capacity);
        return text.ToString();
    }

    private const uint WM_CLOSE = 0x0010;
    private const uint GW_OWNER = 4;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const int DWMWA_CLOAKED = 14;

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr handle, uint command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int size);
}
