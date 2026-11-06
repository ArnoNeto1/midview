namespace Midview.Agent;

// One of the Ctrl+Alt+Del escapes SwitchUserBlockHandler closes while the
// screen is locked: "Switch user" (RegistrySwitchUserPolicy, HKLM), the
// student's Task Manager, sign out, lock, change password and power
// (StudentCtrlAltDelLockdown, their own hive), or the Ctrl+Alt+Del screen
// itself, which the Desk helper closes while ScreenLockSignal is set. Only the
// Agent's SYSTEM rights can do any of these, which is why the Session can't
// close them itself.
public interface ISwitchUserPolicy
{
    void Block();
    void Unblock();
}
