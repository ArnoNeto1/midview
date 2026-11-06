namespace Midview.Protocol;

// The name of the event the Agent sets while the teacher has the screen locked
// (ScreenLockSignal) and the Desk helper reads (CtrlAltDelScreen): in the Global
// namespace, because the Agent runs in session 0 and the helper in the
// console's. Only a SYSTEM-level process may create a Global object, so a
// student can't make one up.
public static class ScreenLockEvent
{
    public const string Name = @"Global\MidviewScreenLocked";
}
