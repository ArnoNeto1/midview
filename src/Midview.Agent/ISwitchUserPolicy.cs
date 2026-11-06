namespace Midview.Agent;

// One of the Ctrl+Alt+Del escapes SwitchUserBlockHandler closes while the
// screen is locked: "Switch user" (RegistrySwitchUserPolicy, HKLM) or the
// student's Task Manager and sign out (StudentCtrlAltDelLockdown, their own
// hive). Only the Agent's SYSTEM rights can write either, which is why the
// Session can't close them itself.
public interface ISwitchUserPolicy
{
    void Block();
    void Unblock();
}
