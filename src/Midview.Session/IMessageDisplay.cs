namespace Midview.Session;

public interface IMessageDisplay
{
    // Must return as soon as the message is on screen, without waiting for
    // the student to close it.
    void Show(string text);

    // Closes every message still open, and any still waiting to show (under
    // the lock screen): the teacher closed the student's programs, and these
    // are this program's own windows, which that never touches.
    void CloseAll();
}
