namespace Midview.Session;

// The short notice the student gets in a corner of the screen when a class
// lesson starts (ClassFolder.LessonNotice): it never takes the focus and goes
// away on its own after a few seconds. Must return at once.
public interface IClassFolderNotice
{
    void Show(string text);
}
