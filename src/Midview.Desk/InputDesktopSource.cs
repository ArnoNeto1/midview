namespace Midview.Desk;

// Turns OpenInputDesktop -- which hands back a brand-new handle on every call,
// even for the same desktop -- into a STABLE handle per input desktop, the kind
// DesktopFollower can compare across polls. It keys on the desktop NAME
// ("Default", "Winlogon", ...): a poll whose name matches the current one gets
// the same cached handle back and its duplicate is closed at once; a poll whose
// name changed adopts the new handle as current and holds the old one until the
// worker thread has re-attached to the new desktop (ReleaseSuperseded), because
// CloseDesktop fails while a thread in this process still has the handle as its
// thread desktop -- so the handle a thread is currently on is never closed. A
// failed OpenInputDesktop is reported as IntPtr.Zero (no input desktop), which
// the follower and DeskServer skip.
//
// The three Win32 calls are injected as delegates so this is unit-testable
// without a real desktop; Program.cs wires the real ones. Meant for one polling
// thread, with ReleaseSuperseded called from the same worker's attach callback.
internal sealed class InputDesktopSource
{
    private readonly Func<IntPtr> _openInputDesktop;
    private readonly Func<IntPtr, string?> _desktopName;
    private readonly Action<IntPtr> _closeDesktop;

    // The stable handle for the desktop currently receiving input (kept open),
    // and its name. Zero/null before the first successful poll.
    private IntPtr _current;
    private string? _currentName;

    // Handles of desktops the input has moved off, kept open until the worker
    // thread attaches to the new current. Released all at once on the next
    // successful attach, by when the thread is on _current and so on none of
    // these -- even if an earlier attach was missed.
    private readonly List<IntPtr> _superseded = new();

    public InputDesktopSource(
        Func<IntPtr> openInputDesktop, Func<IntPtr, string?> desktopName, Action<IntPtr> closeDesktop)
    {
        _openInputDesktop = openInputDesktop;
        _desktopName = desktopName;
        _closeDesktop = closeDesktop;
    }

    // The source DesktopFollower polls: the stable handle of the input desktop,
    // or IntPtr.Zero when there is none to open right now.
    public IntPtr Current()
    {
        var opened = _openInputDesktop();
        if (opened == IntPtr.Zero)
        {
            // No input desktop (e.g. mid desktop-switch): report none and keep
            // what we had. DeskServer never calls SetThreadDesktop(Zero).
            return IntPtr.Zero;
        }

        var name = _desktopName(opened);
        if (name is null)
        {
            // Can't read the name, so we can't tell which desktop this is: drop
            // the handle and report none this poll rather than adopt it blindly.
            _closeDesktop(opened);
            return IntPtr.Zero;
        }

        if (name == _currentName)
        {
            // Same desktop, fresh duplicate handle: throw the duplicate away and
            // keep handing out the stable one.
            _closeDesktop(opened);
            return _current;
        }

        // The input desktop changed. Adopt the new handle; hold the old current
        // (if any) until the worker thread has attached to the new one.
        if (_current != IntPtr.Zero)
        {
            _superseded.Add(_current);
        }
        _current = opened;
        _currentName = name;
        return _current;
    }

    // Called by the attach seam after SetThreadDesktop(Current) succeeds: the
    // worker thread is on the current desktop now and so on none of the handles
    // it left behind, which are finally safe to close.
    public void ReleaseSuperseded()
    {
        foreach (var handle in _superseded)
        {
            _closeDesktop(handle);
        }
        _superseded.Clear();
    }
}
