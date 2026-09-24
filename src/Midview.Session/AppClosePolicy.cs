namespace Midview.Session;

// One top-level window on the student's desktop, as the close-programs
// feature sees it.
public sealed record TopLevelWindow(
    IntPtr Handle, int ProcessId, string ProcessName, string ClassName, string Title,
    bool IsVisible, bool IsCloaked, bool HasOwner, bool IsToolWindow);

// Decides what "close all programs" touches. Kept apart from the Win32 calls
// so the rules can be tested anywhere.
public static class AppClosePolicy
{
    // The desktop, the taskbar, and the shell's own panels (Start, search,
    // notifications, the lock screen -- top-level CoreWindows; Store apps
    // live in an ApplicationFrameWindow instead): "closing" them would take
    // part of Windows down, not close a program.
    private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
    };

    // Never ended by force. explorer also runs the desktop and taskbar (its
    // File Explorer windows just get the normal close), ApplicationFrameHost
    // hosts every Store app at once (each app's frame gets its own close),
    // and the rest are the shell's panels.
    private static readonly HashSet<string> NeverForceClosed = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ApplicationFrameHost", "StartMenuExperienceHost", "ShellExperienceHost",
        "SearchHost", "SearchApp", "TextInputHost", "LockApp",
    };

    // The same windows Alt+Tab shows: visible, not cloaked (e.g. on another
    // virtual desktop or suspended), unowned (dialogs close with their
    // owner), not tool windows, with a title. Never Midview's own windows
    // (the teacher's message, the chat) or the shell's.
    public static IReadOnlyList<TopLevelWindow> WindowsToClose(IEnumerable<TopLevelWindow> windows, int ownProcessId) =>
        windows.Where(window =>
                window.IsVisible && !window.IsCloaked && !window.HasOwner && !window.IsToolWindow
                && window.Title.Trim().Length > 0
                && window.ProcessId != ownProcessId
                && !IsMidview(window.ProcessName)
                && !ShellWindowClasses.Contains(window.ClassName))
            .ToList();

    public static bool MayForceClose(string processName) =>
        !NeverForceClosed.Contains(processName) && !IsMidview(processName);

    private static bool IsMidview(string processName) =>
        processName.StartsWith("Midview", StringComparison.OrdinalIgnoreCase);
}
