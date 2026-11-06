namespace Midview.Agent;

// Tells the Desk helper that the teacher has the screen locked, so it closes
// the Ctrl+Alt+Del screen a student opens (CtrlAltDelDismisser): a named event,
// set while locked and clear otherwise. One of the policies
// SwitchUserBlockHandler holds, so it follows the lock exactly, teacher by
// teacher. It lives as long as the Agent: if the Agent ends, the event is gone
// and the helper reads "unlocked", so nothing is ever pressed for a lock nobody
// holds.
public sealed class ScreenLockSignal : ISwitchUserPolicy, IDisposable
{
    private readonly EventWaitHandle _event;

    public ScreenLockSignal(string name)
    {
        _event = new EventWaitHandle(false, EventResetMode.ManualReset, name);
        // A new Agent starts unlocked, even if the event outlived the last one.
        _event.Reset();
    }

    public void Block() => _event.Set();

    public void Unblock() => _event.Reset();

    public void Dispose() => _event.Dispose();
}
