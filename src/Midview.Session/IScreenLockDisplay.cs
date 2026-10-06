namespace Midview.Session;

// What covers the student's screen while the teacher has it locked. Both
// must return at once.
public interface IScreenLockDisplay
{
    void Show();
    void Hide();
}
