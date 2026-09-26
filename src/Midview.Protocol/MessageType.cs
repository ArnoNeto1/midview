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

    // A file from the teacher. The connection opens with SendFile (a
    // FileTransfer header: size and name), then carries FileChunk messages
    // until exactly that many bytes have arrived, and ends with one
    // CommandAck (see FileTransfer for its payload).
    SendFile = 17,
    FileChunk = 18,

    // Close the student's programs. One byte of payload (CloseAppsMode),
    // answered with one CommandAck carrying a CommandResult.
    CloseApps = 19,

    // Collect the files in the student's "Entregar ao professor" folder:
    // the Session answers with SendFile/FileChunk messages, one file after
    // another, then one CommandAck (see FileCollection).
    CollectFiles = 20,

    // An Agent command: shut down, restart, or call either off (see
    // PowerCommand). Answered with one CommandAck carrying a CommandResult.
    Power = 21,

    // An Agent question, no payload: the MAC address of the network card
    // this connection arrived through -- the one on the teacher's network,
    // which the Master needs to turn the machine on later (see WakeOnLan).
    // Answered with one WakeAddress: the 6 bytes, or empty if the Agent
    // couldn't tell. An Agent from before this answers CommandAck instead.
    GetWakeAddress = 22,
    WakeAddress = 23,

    // An Agent command: the lab's power timetable (see PowerSchedule), which
    // the Agent keeps and follows by shutting the machine down on its own.
    // Answered with one CommandAck carrying a CommandResult.
    SetPowerSchedule = 24,

    // Updating the student program over the network: an Agent command from
    // a trusted Master only (see NetworkUpdate for the whole exchange).
    UpdateOffer = 25,
    UpdateAccepted = 26,
    GetUpdateStatus = 27,
    UpdateStatus = 28,
}

public enum CloseAppsMode : byte
{
    // Like clicking each window's X: programs with unsaved work ask first.
    Ask = 0,

    // Ask first, then end whatever is still open after a short grace period
    // -- unsaved work in those programs is lost.
    Force = 1,
}
