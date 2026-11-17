namespace Midview.Master;

// The step that asks to take the Google account out ("conta-google"): the
// cable opens the phone's own accounts screen, so the teacher only taps the
// account and "Remover conta". Android lets no computer remove a Google
// account over the cable, so that stays the teacher's.
public sealed partial class PhoneCable
{
    internal const string AccountsAction = "android.settings.SYNC_SETTINGS";

    // The accounts screen, on the screen of the one allowed phone on the
    // cable (woken and unlocked first, as for the install). False when there
    // is no such phone or it did not open: the step's text says where it is.
    public bool OpenAccounts()
    {
        var (problem, serial) = Devices();
        _serial = serial;
        if (problem is not null)
        {
            return false;
        }
        Shell(Short, "input", "keyevent", "KEYCODE_WAKEUP");
        Shell(Short, "wm", "dismiss-keyguard");
        var started = Shell(Short, "am", "start", "-a", AccountsAction);
        return !started.TimedOut
               && started.Text.Contains("Starting", StringComparison.Ordinal)
               && !StartFailed.IsMatch(started.Text);
    }
}
