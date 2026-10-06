using Midview.Protocol;

namespace Midview.Agent;

// The countdown banner on the student's screen while a shutdown or restart
// is on its way (SessionCountdownDisplay; fakes in tests). One at a time:
// a new one replaces the one up. Neither call may throw.
public interface ICountdownDisplay
{
    void Show(PowerCountdown kind, int seconds);

    void Close();
}
