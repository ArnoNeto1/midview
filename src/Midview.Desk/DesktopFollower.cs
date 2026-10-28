namespace Midview.Desk;

// Notices when the desktop that takes the keyboard and mouse changes: the
// user's desktop, then the UAC secure desktop while a prompt is up, then back.
// The helper's capture and input threads can only work on the desktop they are
// attached to, so it polls this a few times a second and, each time it reports
// a change, attaches them to the new one. Where the handle comes from (the
// real OpenInputDesktop) is up to the caller, so nothing here touches Win32
// and a scripted sequence can test it. Meant for one polling thread.
public sealed class DesktopFollower
{
    private readonly Func<IntPtr> _currentInputDesktop;

    // Null until the first poll, so that one reports a change even if the
    // source gives a zero handle.
    private IntPtr? _lastReported;

    public DesktopFollower(Func<IntPtr> currentInputDesktop)
    {
        _currentInputDesktop = currentInputDesktop;
    }

    // True exactly when the source gives something other than what this last
    // reported, and the first poll always does. desktop is the source's
    // current value either way; only a true result means it is new.
    public bool PollChanged(out IntPtr desktop)
    {
        desktop = _currentInputDesktop();
        if (desktop == _lastReported)
        {
            return false;
        }

        _lastReported = desktop;
        return true;
    }

    // Forget the last desktop reported, so the next poll reports a change even
    // if the desktop has not moved. A new control connection calls this, so its
    // worker attaches and makes its capture at once instead of waiting for a
    // desktop switch that may never come -- the follower is shared across
    // connections, and without this a second control session got a black screen.
    public void Reset() => _lastReported = null;
}
