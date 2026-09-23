namespace Midview.Session;

public interface IMessageDisplay
{
    // Must return as soon as the message is on screen, without waiting for
    // the student to close it.
    void Show(string text);
}
