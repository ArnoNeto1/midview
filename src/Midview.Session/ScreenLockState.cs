namespace Midview.Session;

// Whether the teacher has this computer's screen locked, for the windows
// that wait for it to be unlocked before showing (the teacher's messages
// and the countdown banner) -- some of them in a second Session process,
// started by the Agent. The Session that shows the lock screen publishes it
// as a named event in the student's logon session; anyone else opens the
// name each time they look, so a Session that restarted is never read
// through a stale handle.
public sealed class ScreenLockState
{
    public static readonly ScreenLockState Default = new(@"Local\Midview.TelaBloqueada");

    private readonly string _name;
    private readonly object _gate = new();
    private EventWaitHandle? _published;

    public ScreenLockState(string name)
    {
        _name = name;
    }

    public bool IsLocked
    {
        get
        {
            lock (_gate)
            {
                if (_published is not null)
                {
                    return _published.WaitOne(0);
                }
            }

            try
            {
                if (EventWaitHandle.TryOpenExisting(_name, out var handle))
                {
                    using (handle)
                    {
                        return handle.WaitOne(0);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Not ours to read: taken as not locked.
            }
            return false;
        }
    }

    // By the Session that shows the lock screen.
    public void Publish(bool locked)
    {
        lock (_gate)
        {
            _published ??= new EventWaitHandle(false, EventResetMode.ManualReset, _name);
            if (locked)
            {
                _published.Set();
            }
            else
            {
                _published.Reset();
            }
        }
    }
}
