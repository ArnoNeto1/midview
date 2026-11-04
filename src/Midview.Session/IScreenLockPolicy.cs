namespace Midview.Session;

// Turns off the ways a student escapes a locked screen through
// Ctrl+Alt+Del: the Task Manager (which could kill the lock or launch
// programs) and signing out. Applied while the screen is locked and removed
// when it unlocks.
public interface IScreenLockPolicy
{
    void Block();
    void Unblock();
}
