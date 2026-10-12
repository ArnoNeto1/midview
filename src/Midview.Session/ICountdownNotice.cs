namespace Midview.Session;

// A warning that counts down on the student's screen (CountdownBanner):
// message while the time runs, doneMessage once it's up, then gone after
// linger. Must return at once.
public interface ICountdownNotice
{
    void Show(string message, string doneMessage, TimeSpan duration, TimeSpan linger);
}
