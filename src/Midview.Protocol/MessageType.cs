namespace Midview.Protocol;

public enum MessageType : byte
{
    Hello = 1,
    Heartbeat = 2,
    StartControl = 3,
    StopControl = 4,
    FrameTile = 5,
    InputEvent = 6,
    BlockInternet = 7,
    UnblockInternet = 8,
    BlockDomain = 9,
    UnblockDomain = 10,
    CommandAck = 11,

    // Classroom-console preview feed. A connection whose first message is
    // RequestThumbnail is a preview connection: each request gets exactly
    // one ThumbnailFrame reply (a JPEG, or an empty payload when the screen
    // can't be captured right now, e.g. while it's locked).
    RequestThumbnail = 12,
    ThumbnailFrame = 13,

    // A text from the teacher, shown in a window on the student's screen.
    // Sent to the Session as the only message of a short-lived connection
    // (UTF-8 payload); answered with one CommandAck.
    ShowMessage = 14,

    // Private chat between the teacher and one student. A connection whose
    // first message is ChatOpen stays open for the whole conversation;
    // ChatText (a UTF-8 line, see TextMessage) then flows both ways, and the
    // Master's periodic Heartbeat keeps the connection from timing out while
    // nobody is typing.
    ChatOpen = 15,
    ChatText = 16,
}
