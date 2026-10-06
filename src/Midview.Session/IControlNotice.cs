namespace Midview.Session;

// What tells the student, on their own screen, that the teacher is
// controlling this computer. Both must return at once.
public interface IControlNotice
{
    void Show();
    void Hide();
}
