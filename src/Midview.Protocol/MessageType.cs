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
    // can't be captured right now, e.g. while it's locked). Each request
    // says how big (ThumbnailRequest).
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

    // Close the student's programs, after warning them (CloseAppsCommand),
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

    // Program blocking (see AppBlockPolicy). SetAppBlock is an Agent command
    // (answered with one CommandAck carrying a CommandResult); GetAppBlock,
    // no payload, is answered with one AppBlockStatus carrying the policy
    // the machine follows. An Agent from before this answers CommandAck.
    SetAppBlock = 29,
    GetAppBlock = 30,
    AppBlockStatus = 31,

    // "What does the student have open?" A Session question, no payload,
    // answered with one OpenPrograms (see OpenProgramsReport). A Session
    // from before this would take it for a control connection, so the
    // Master only asks machines whose Agent answered GetAppBlock.
    ListOpenPrograms = 32,
    OpenPrograms = 33,

    // Lock the student's screen (grey, with a padlock) and keyboard and
    // mouse, for as long as this connection stays open. The Session answers
    // one CommandAck (CommandResult); the Master's Heartbeats then keep the
    // connection up. Closing it unlocks at once, and so does the Session's
    // 15 s receive timeout if the Master stops answering. Never the control
    // slot.
    LockScreen = 34,

    // A file from the teacher with the "Enviar arquivo" options (see
    // FileTransfer.EncodeOptionsHeader); otherwise exactly like SendFile.
    SendFileWithOptions = 35,

    // Send everything in the student's "Entregar ao professor" folder to
    // their Recycle Bin. The only message of a short-lived connection,
    // answered with one CommandAck (CommandResult).
    ClearDeliveryFolder = 36,

    // Pairing a teacher with this machine (see Pairing for the exchange):
    // the only Agent exchange a teacher it doesn't trust yet may start.
    PairRequest = 37,
    PairCommit = 38,
    PairNonce = 39,
    PairReveal = 40,
    PairConfirm = 41,
    PairCancel = 42,
    PairDone = 43,
    PairRefused = 44,

    // An Agent command, from a trusted teacher only: for
    // Pairing.NewTeacherWindow, let one more teacher pair ("Permitir novo
    // professor"). Answered with one CommandAck carrying a CommandResult.
    AllowNewTeacher = 45,

    // Master -> Session: open a website (a WebsiteAddress, as TextMessage)
    // in the student's browser ("Abrir site"). The only message of a
    // short-lived connection, answered with one CommandAck (CommandResult).
    OpenWebsite = 46,

    // Master -> Session: start a program as the student ("Abrir programa"),
    // a ProgramCommand as TextMessage. The only message of a short-lived
    // connection, answered with one CommandAck (CommandResult).
    OpenProgram = 47,

    // "Ver a tela"/"Controlar" as one message per update: a viewer that
    // sends ScreenStream as its first message gets the screen as ScreenFrame
    // messages (see ScreenFrame) instead of FrameTile, and answers each one
    // it has drawn with a FrameAck. A viewer from before this opens with
    // StartControl or a Heartbeat and still gets FrameTile; a Session from
    // before this ignores ScreenStream and sends FrameTile, which the viewer
    // still draws.
    ScreenStream = 48,
    ScreenFrame = 49,
    FrameAck = 50,

    // The lab-configuration panel (see LabConfig and
    // docs/superpowers/specs/2026-10-08-lab-configuration-design.md).
    // SetLabConfig is an Agent command carrying a LabConfig (answered with
    // one CommandAck and a CommandResult); GetLabConfig, no payload, is
    // answered with one LabConfigStatus carrying the LabConfig the Agent has
    // verified is really applied. An Agent from before this answers
    // CommandAck to GetLabConfig, so the Master can show "Midview antigo".
    SetLabConfig = 51,
    GetLabConfig = 52,
    LabConfigStatus = 53,

    // The lab panel's "RESET geral": an Agent command, no payload, that
    // removes every lab policy (the whole Chrome policy subtree and the
    // wallpaper lock, so it also clears what an older hand-run script left)
    // and lifts the Edge block. Answered with one CommandAck and a
    // CommandResult. Kept apart from SetLabConfig so turning one function off
    // only removes that function's keys, while RESET wipes everything.
    ResetLabConfig = 54,

    // The lab panel's "Limpar perfis do Chrome": an Agent command, no
    // payload, that closes the student's Chrome and deletes every user's
    // saved Chrome profile (User Data), leaving the browser blank for the
    // next class. Answered with one CommandAck and a CommandResult.
    ClearChromeProfiles = 55,

    // Uninstalling a program (see ProgramList and the lab-configuration
    // design). GetInstalledPrograms, no payload, is answered with one
    // InstalledPrograms carrying the names of the programs that can be
    // uninstalled on that machine. UninstallPrograms (a ProgramList of names)
    // is an Agent command that silently uninstalls each one it finds,
    // answered with one CommandAck and a CommandResult. An Agent from before
    // this answers CommandAck to GetInstalledPrograms.
    GetInstalledPrograms = 56,
    InstalledPrograms = 57,
    UninstallPrograms = 58,

    // Delete the students' downloads: an Agent command whose 1-byte payload
    // is a DownloadsScope (today only, or everything in each user's Downloads
    // folder). Answered with one CommandAck and a CommandResult.
    ClearDownloads = 59,

    // While a teacher controls a machine, the text copied on either side is
    // sent to the other so it can be pasted there (ClipboardText both ways on
    // the control connection). Text only, size-capped; see ClipboardText.
    ClipboardText = 60,

    // "Dar acesso a um colega" (see TeacherInvite and
    // docs/superpowers/specs/2026-10-14-teacher-invite-design.md).
    // TrustTeacher is an Agent command, from a trusted teacher only: trust
    // another teacher too (a TeacherGrant), with nothing on the student's
    // screen. Answered with one CommandAck carrying a CommandResult; an Agent
    // from before this answers CommandAck [0], which the Master shows as
    // "Midview antigo".
    TrustTeacher = 61,

    // Teacher to teacher, on TeacherInvite.Port, never to a student machine:
    // the colleague's InviteJoin (an InviteJoinRequest) is answered with
    // InviteDone (an InviteResult) or InviteRefused (why, UTF-8, in
    // Portuguese).
    InviteJoin = 62,
    InviteDone = 63,
    InviteRefused = 64,

    // The class folder ("Pasta da turma", see ClassLesson, ClassFolderWire and
    // docs/superpowers/specs/2026-10-20-class-folder-design.md). Agent
    // messages: SetClassLesson is a command carrying a ClassLesson (answered
    // with one CommandAck and a CommandResult); GetClassLesson, no payload, is
    // answered with one ClassLessonStatus carrying the ClassLesson the Agent
    // follows. An Agent from before this answers CommandAck to GetClassLesson.
    SetClassLesson = 65,
    GetClassLesson = 66,
    ClassLessonStatus = 67,

    // Session messages, each the first message of a short-lived connection.
    // ListClassFolder, no payload, is answered with one ClassFolderListing
    // (ClassFolderWire.EncodeListing). PullClassFile (a file reference) is
    // answered with SendFile and FileChunk messages and then one CommandAck,
    // as CollectFiles, or with one CommandAck alone saying why not.
    // PushClassFile (a push header) is answered with one CommandAck accepting
    // or refusing, then takes FileChunk messages until that many bytes have
    // arrived and ends with one CommandAck saying whether it was saved, as
    // SendFile. DeleteClassFile (a file reference), RenameClassFile (a
    // rename) and ClearSetAside (a lesson id: empty what was set aside for
    // that lesson) are each answered with one CommandAck carrying a
    // CommandResult. A Session from before this answers FrameTile, which the
    // Master shows as "Midview antigo".
    ListClassFolder = 68,
    ClassFolderListing = 69,
    PullClassFile = 70,
    PushClassFile = 71,
    DeleteClassFile = 72,
    RenameClassFile = 73,
    ClearSetAside = 74,
}

public enum CloseAppsMode : byte
{
    // Like clicking each window's X: programs with unsaved work ask first.
    Ask = 0,

    // End the programs without asking (the warning before it was the
    // student's time to save) -- unsaved work is lost.
    Force = 1,
}
