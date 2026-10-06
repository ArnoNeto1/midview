namespace Midview.Agent;

// The pairing code on the student's screen (SessionPairingCodeDisplay;
// fakes in tests).
public interface IPairingCodeDisplay
{
    // Whether someone is logged on at the machine, so a code would appear.
    bool SomeoneLoggedOn { get; }

    // The code stays up until the returned handle is disposed; null if it
    // couldn't be shown.
    IDisposable? Show(int code, string teacherName);
}
