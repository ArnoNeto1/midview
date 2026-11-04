namespace Midview.Agent;

// Hides or restores "Switch user" on the Ctrl+Alt+Del screen (the HKLM policy
// HideFastUserSwitching). Only the Agent's SYSTEM rights can write it, which
// is why the Session can't block switching user itself.
public interface ISwitchUserPolicy
{
    void Block();
    void Unblock();
}
