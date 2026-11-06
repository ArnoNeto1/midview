namespace Midview.Desk;

// While the teacher has the screen locked, closes the Ctrl+Alt+Del screen a
// student opens by pressing Esc on it, so they are back on the lock at once.
// Ctrl+Alt+Del itself can't be stopped without a keyboard driver; this only
// takes the screen away again.
//
// That screen is on the Winlogon secure desktop, which this helper (SYSTEM, in
// the console session) can reach and the student's own processes can't. Two
// other things show on that desktop and are left alone: the UAC prompt (Esc
// would refuse it, even one the teacher is answering by remote control) and the
// sign-in screen while nobody is logged on (Esc would keep a student from
// logging on during the lock).
//
// The first Esc waits a moment for the screen to come up; one that was lost is
// sent again after a pause, never on every poll. Every source and the key press
// are injected, so tests script them. Poll never throws: it runs on a loop of
// its own, and a failure is logged once, not on every poll.
internal sealed class CtrlAltDelDismisser
{
    public const string SecureDesktopName = "Winlogon";

    public static readonly TimeSpan FirstPressAfter = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan PressAgainAfter = TimeSpan.FromSeconds(1);

    private readonly Func<bool> _screenLocked;
    private readonly Func<string?> _inputDesktopName;
    private readonly Func<bool> _someoneLoggedOn;
    private readonly Func<bool> _uacPromptUp;
    private readonly Action _pressEscape;
    private readonly Action<string> _log;

    // When the Ctrl+Alt+Del screen was first seen this time, and when Esc was
    // last sent to it; both null while it isn't up.
    private DateTime? _since;
    private DateTime? _lastPress;
    private string? _lastError;

    public CtrlAltDelDismisser(
        Func<bool> screenLocked, Func<string?> inputDesktopName, Func<bool> someoneLoggedOn,
        Func<bool> uacPromptUp, Action pressEscape, Action<string> log)
    {
        _screenLocked = screenLocked;
        _inputDesktopName = inputDesktopName;
        _someoneLoggedOn = someoneLoggedOn;
        _uacPromptUp = uacPromptUp;
        _pressEscape = pressEscape;
        _log = log;
    }

    public void Poll(DateTime now)
    {
        try
        {
            if (!CtrlAltDelScreenUp())
            {
                _since = null;
                _lastPress = null;
                return;
            }

            _since ??= now;
            var due = _lastPress is { } last ? last + PressAgainAfter : _since.Value + FirstPressAfter;
            if (now < due)
            {
                return;
            }

            _lastPress = now;
            _pressEscape();
            _lastError = null;
        }
        catch (Exception ex)
        {
            if (ex.Message != _lastError)
            {
                _lastError = ex.Message;
                _log($"Closing the Ctrl+Alt+Del screen failed: {ex.Message}");
            }
        }
    }

    // Cheapest first: the lock and the desktop name rule most polls out before
    // any process list is read.
    private bool CtrlAltDelScreenUp() =>
        _screenLocked()
        && _inputDesktopName() == SecureDesktopName
        && _someoneLoggedOn()
        && !_uacPromptUp();
}
