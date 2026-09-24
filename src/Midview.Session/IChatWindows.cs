namespace Midview.Session;

// Opens the student's side of a private chat with the teacher.
public interface IChatWindows
{
    // sendToTeacher delivers one line the student typed and returns false if
    // it couldn't be sent (the teacher already left, the connection dropped).
    IChatWindow Open(Func<string, bool> sendToTeacher);
}

public interface IChatWindow
{
    void ShowTeacherMessage(string text);

    // The teacher closed the conversation or the connection dropped; nothing
    // the student types can reach them any more.
    void ShowConversationEnded();
}
