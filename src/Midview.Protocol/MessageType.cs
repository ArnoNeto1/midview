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
    // A phone's agent (port 28900) takes both from a trusted teacher: it
    // answers ShowMessage with a CommandResult (its first byte the Session's
    // 1 or 0), and ChatOpen with one CommandResult before the conversation
    // (the Session answers nothing). An agent from before them answers "Ainda
    // não funciona em celular.".
    ChatOpen = 15,
    ChatText = 16,

    // A file from the teacher. The connection opens with SendFile (a
    // FileTransfer header: size and name), then carries FileChunk messages
    // until exactly that many bytes have arrived, and ends with one
    // CommandAck (see FileTransfer for its payload).
    // A phone's agent (port 28900) takes it too, from a trusted teacher, and
    // so CollectFiles, SendFileWithOptions and ClearDeliveryFolder, for its
    // Download/Midview: the one folder the teacher's files land in and are
    // collected from. An agent from before them answers "Ainda não funciona
    // em celular.".
    SendFile = 17,
    FileChunk = 18,

    // Close the student's programs, after warning them (CloseAppsCommand),
    // answered with one CommandAck carrying a CommandResult. A phone's agent
    // (port 28900) takes it too, from a trusted teacher: it warns the student
    // for the time in it, then closes all the student's apps, whatever the
    // mode. An agent from before that answers "Ainda não funciona em celular.".
    CloseApps = 19,

    // Collect the files in the student's "Entregar ao professor" folder:
    // the Session answers with SendFile/FileChunk messages, one file after
    // another, then one CommandAck (see FileCollection).
    CollectFiles = 20,

    // An Agent command: shut down, restart, or call either off (see
    // PowerCommand). Answered with one CommandAck carrying a CommandResult.
    // A phone's agent (port 28900) takes it too, from a trusted teacher: it
    // warns the student for the same time, then restarts, or for a shutdown
    // turns its screen off and locks (Android lets no app turn a phone off);
    // the Windows updates' flag means nothing there. An agent from before
    // that answers "Ainda não funciona em celular.".
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
    // professor"), the one who has the code in the payload (see
    // NewTeacherInvite). Answered with one CommandAck carrying a
    // CommandResult. An Agent from before the code ignores the payload and
    // lets anyone pair; its answer lacks NewTeacherInvite.WithCodeSuffix.
    // One from after it refuses an empty payload (a Master from before it).
    AllowNewTeacher = 45,

    // Master -> Session: open a website (a WebsiteAddress, as TextMessage)
    // in the student's browser ("Abrir site"). The only message of a
    // short-lived connection, answered with one CommandAck (CommandResult).
    // A phone's agent (port 28900) takes it too, from a trusted teacher, and
    // opens the site in Chrome, or once the teacher's lock screen goes.
    OpenWebsite = 46,

    // Master -> Session: start a program as the student ("Abrir programa"),
    // a ProgramCommand as TextMessage. The only message of a short-lived
    // connection, answered with one CommandAck (CommandResult). A phone's
    // agent (port 28900) takes the package of one of its apps (from
    // PhoneApps) instead, from a trusted teacher, and opens it as OpenWebsite.
    // An agent from before them answers "Ainda não funciona em celular.".
    OpenProgram = 47,

    // "Ver a tela"/"Controlar" as one message per update: a viewer that
    // sends ScreenStream as its first message gets the screen as ScreenFrame
    // messages (see ScreenFrame) instead of FrameTile, and answers each one
    // it has drawn with a FrameAck. A viewer from before this opens with
    // StartControl or a Heartbeat and still gets FrameTile; a Session from
    // before this ignores ScreenStream and sends FrameTile, which the viewer
    // still draws.
    // A phone's agent (port 5901) takes "Controlar" on this connection too,
    // from a trusted teacher, and answers StartControl (which a PC answers
    // with nothing) with one CommandAck carrying a CommandResult: 1, or 0 and
    // why not (its accessibility service off). Its InputEvents are in pixels
    // of the last ScreenFrame, the characters typed as InputEventKind.Text;
    // StopControl goes back to watching. An agent from before it closes the
    // connection on StartControl.
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
    // this answers CommandAck to GetInstalledPrograms. A phone's agent takes
    // UninstallPrograms with a ProgramList of packages (from PhoneApps), from
    // a trusted teacher, and removes them as the Device Owner; it lists its
    // apps with GetPhoneApps, not GetInstalledPrograms.
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
    // A phone's agent takes these on its commands port, and the Session
    // messages on its screen port, as a PC; one from before them answers
    // "Ainda não funciona em celular." to these and hangs up on those, which
    // the Master shows as "Midview antigo". A phone keeps no class folders'
    // home: it answers the home's messages (below) that way too, and the
    // Master asks only its PCs about the home.
    SetClassLesson = 65,
    GetClassLesson = 66,
    ClassLessonStatus = 67,

    // Session messages, each the first message of a short-lived connection.
    // ListClassFolder, no payload, is answered with ClassFolderListing
    // messages (ClassFolderWire.EncodeListingParts): the whole listing, in
    // parts of at most ClassFolderWire.MaxEntries files, each saying whether
    // more follow, so any number of files is listed; the Master reads up to
    // the last and goes by nothing less. PullClassFile (a file reference) is
    // answered with SendFile and FileChunk messages and then one CommandAck,
    // as CollectFiles, or with one CommandAck alone saying why not.
    // PushClassFile (a push header, with the state of the file it may
    // replace: none, or a version) is answered with one CommandAck accepting
    // or refusing, then takes FileChunk messages until that many bytes have
    // arrived and ends with one CommandAck saying whether it was saved, as
    // SendFile; the file on the PC no longer in that state is refused ("mudou
    // neste PC"). DeleteClassFile (a file reference), RenameClassFile (a
    // rename) and ClearSetAside (a lesson and the set-aside files of it the
    // Master has dealt with: those go to the Recycle Bin) are each answered
    // with one CommandAck carrying a CommandResult. A Session from before this answers FrameTile, which the
    // Master shows as "Midview antigo".
    ListClassFolder = 68,
    ClassFolderListing = 69,
    PullClassFile = 70,
    PushClassFile = 71,
    DeleteClassFile = 72,
    RenameClassFile = 73,
    ClearSetAside = 74,

    // Two teacher PCs in one lab (see ClassHome, ClassFolderRemote and
    // docs/superpowers/specs/2026-10-20-class-folder-design.md): the first PC to
    // start a lesson is the home of the class folders, and the Agents record it.
    // Agent messages. ClaimClassHome is a command carrying a ClassHomeClaim, sent
    // before SetClassLesson (answered with one CommandAck and a CommandResult;
    // refused, with ClassHomeClaim.OtherHomeText and the home's name as the
    // detail, when another PC is the home and the claim isn't a take-over with
    // a newer generation than that home's).
    // GetClassHome, no payload, is answered with one ClassHomeStatus carrying the
    // ClassHome the Agent keeps (empty when it has none). IsTeacherTrusted, a
    // teacher's thumbprint (ASCII hex), asks an Agent whether it trusts that
    // teacher, so that the home can serve a colleague's PC: answered with one
    // CommandAck carrying a CommandResult, true when it does. An Agent from
    // before this answers CommandAck [0] to ClaimClassHome, GetClassHome and
    // IsTeacherTrusted (which reads as "no").
    ClaimClassHome = 75,
    GetClassHome = 76,
    ClassHomeStatus = 77,
    IsTeacherTrusted = 78,

    // Teacher to teacher, on ClassFolderRemote.Port, never to a student machine:
    // a colleague's PC works the home's "Pasta da turma" window. Payloads are
    // JSON (ClassFolderRemote.EncodeJson). RemoteClassFolderState, no payload,
    // is answered with RemoteClassFolderStateReply (a RemoteState);
    // RemoteClassFolderCommand (a RemoteCommand), with
    // RemoteClassFolderCommandReply (a RemoteReply); RemoteHandOver (its
    // computer name, UTF-8), a PC asking the home for the class folders to
    // take them over, with RemoteHandOverDone once the home has sent all of
    // them. The home refuses with one CommandAck
    // carrying a CommandResult (false): ClassFolderRemote.NotTrustedText to a PC
    // it doesn't serve, UnknownRequestText to any other message, and, to
    // RemoteClassFolderState when it can't read the class folders just now, a
    // detail starting with UnreadableText. RemoteTakeBack (the generation a
    // take-over claimed with and gave up, ClassFolderRemote.EncodeTakeBack),
    // from a PC whose take-over can't be told done (an Agent that may have
    // taken it doesn't answer), asks the home to claim the room back with a
    // newer generation; answered with one CommandAck carrying a CommandResult.
    RemoteClassFolderState = 79,
    RemoteClassFolderStateReply = 80,
    RemoteClassFolderCommand = 81,
    RemoteClassFolderCommandReply = 82,
    RemoteHandOver = 83,
    RemoteHandOverDone = 84,
    RemoteTakeBack = 85,

    // Installing a program (see ProgramInstall): InstallProgram (an
    // InstallRequest) is an Agent command answered with InstallAccepted, no
    // payload, or one CommandAck carrying a CommandResult refusal. After an
    // accept come FileChunk messages up to the requested size; the Agent
    // answers InstallStarted, no payload, once it has the whole file, then
    // one CommandAck carrying a CommandResult when the installer ends. An
    // Agent from before this answers CommandAck [0] to InstallProgram. A
    // phone's agent takes the same exchange for an .apk (ProgramInstall.MaxApkSize,
    // no arguments) and installs it silently as the Device Owner; one from
    // before it answers "Ainda não funciona em celular.".
    InstallProgram = 86,
    InstallAccepted = 87,
    InstallStarted = 88,

    // Master -> Session: lock the student's keyboard and mouse, as StartControl
    // does, for as long as this connection stays open, and stream nothing. The
    // Master holds it while it controls the machine through Midview.Desk, which
    // can't lock the input itself (see
    // docs/superpowers/specs/2026-10-27-remote-control-through-uac-design.md).
    // The Session answers nothing. The Master's Heartbeats keep the connection
    // up; closing it ends the hold at once, and so does the Session's 15 s
    // receive timeout if the Master stops answering. The student gets the
    // keyboard and mouse back with the last hold of any kind (another
    // controlling connection, a screen lock). Never the control slot.
    HoldInputLock = 89,

    // Master -> helper (Midview.Desk) only: the payload is the admin password's
    // UTF-8 bytes (see CredentialFill). The helper types it into the live UAC
    // prompt and answers nothing. It is only ever sent over the 5902
    // Agent-authenticated helper channel, never the Session. An older helper
    // never receives it, because the Master only offers the fill on the helper
    // endpoint.
    FillAdminPassword = 90,

    // Master -> Agent: close the Ctrl+Alt+Del escapes from a locked screen for
    // as long as this connection stays open: "Switch user" (the HKLM policy
    // HideFastUserSwitching) and, from 0.0.44, the student's Task Manager, sign
    // out, lock, change password and power (DisableTaskMgr, NoLogoff,
    // DisableLockWorkstation, DisableChangePassword and NoClose in their own hive). All are policy
    // keys only the Agent's SYSTEM rights can write (a standard student can
    // only read their own), so the Master holds this alongside the screen
    // lock. The Agent answers one CommandAck; closing the connection (or its
    // 10 s receive timeout) frees the block, ref-counted across teachers. None
    // needs a reboot to take effect on the next Ctrl+Alt+Del.
    HoldSwitchUserBlock = 91,

    // Master -> Android agent (port 28900): ask the device to show its screen-capture
    // consent dialog, so the screen can then be streamed (ScreenStream, ScreenFrame).
    RequestScreenConsent = 92,

    // Master -> Android agent (port 28900): the phone-apps block. Set a PhoneAppPolicy
    // (answered with a CommandAck), ask for the one in force (answered with
    // PhoneAppBlockStatus, a PhoneAppPolicy payload), and ask for the apps the student
    // can open (answered with PhoneApps, a PhoneAppList payload; with each app's
    // flags when GetPhoneApps carries PhoneAppList.WithFlagsRequest).
    SetPhoneAppBlock = 93,
    GetPhoneAppBlock = 94,
    PhoneAppBlockStatus = 95,
    GetPhoneApps = 96,
    PhoneApps = 97,

    // Master -> Android agent (port 28900), trusted teacher only: "Otimizar"
    // the phone (hide ad apps, ad-free DNS). No payload; answered with one
    // PhoneOptimizeReport. Phone-only: an older agent answers CommandAck.
    OptimizePhone = 98,
    PhoneOptimizeReport = 99,

    // Master -> Android agent (port 28900), trusted teacher only: the teacher's
    // lock screen PIN (a PhoneScreenPassword payload: 4 ASCII digits, or empty
    // to remove it). The agent sets it and keeps it, putting it back whenever
    // the student changes or removes it. Answered with a CommandAck
    // (CommandResult); an older agent answers "Ainda não funciona em celular.".
    SetPhoneScreenPassword = 100,

    // Master -> Android agent, from a trusted teacher only: erase the phone
    // and set it up again ("Apagar e preparar de novo"), no payload. Answered
    // with one CommandAck carrying a CommandResult. An agent from before this
    // answers CommandAck with a refusal, which the Master shows as a phone that
    // cannot be erased from here yet.
    WipePhone = 101,

    // An Agent command (PC and phone), from a trusted teacher only, no
    // payload: "Esquecer os outros professores". The machine keeps trusting
    // the sender alone, and closes a "Permitir novo professor" window.
    // Answered with one CommandAck carrying a CommandResult. An Agent from
    // before this answers CommandAck [0], a phone's "Ainda não funciona em
    // celular.".
    ForgetOtherTeachers = 102,

    // Master -> Android agent, from a trusted teacher only: "Liberar Wi-Fi e
    // modo avião" (a PhoneNetworkFree payload: the minutes, 1 to 60, the
    // student may change the Wi-Fi and airplane mode, or 0 to lock them again
    // now). The agent locks them again by itself when the time is up.
    // Answered with a CommandAck (CommandResult); an agent from before this
    // answers "Ainda não funciona em celular.".
    FreePhoneNetwork = 103,

    // Master -> Android agent, from a trusted teacher only: the phones' admin
    // password, as a salted PBKDF2 hash (a PhoneAdminPassword payload; empty
    // to remove it). With it typed on the phone ("Ajustes da escola" in the
    // Midview notification) the Wi-Fi and airplane mode are freed for 10
    // minutes. The password itself never leaves the Master. Answered as
    // FreePhoneNetwork.
    SetPhoneAdminPassword = 104,

    // Master -> Android agent, from a trusted teacher only: "Papel de parede
    // dos celulares e tablets" (a PhoneWallpaper payload: which screens, then
    // a JPEG or PNG; no picture puts the phone's own wallpaper back). One
    // message: the picture is at most PhoneWallpaper.MaxImageBytes, well
    // under MessageFramer.MaxMessageLength. Answered with a CommandAck
    // (CommandResult); an agent from before this answers "Ainda não funciona
    // em celular.".
    SetPhoneWallpaper = 105,

    // Master -> Android agent, from a trusted teacher only: "Desfazer e tirar
    // o Midview" (Telas, "Tirar o Midview dos celulares e tablets..."). The
    // payload is the word the teacher typed to confirm (PhoneLeave: "REMOVER",
    // UTF-8); any other payload is refused. The agent lifts every school lock
    // and setting it applied, keeping the student's files, and answers with
    // one CommandAck (CommandResult) saying what it undid; then it forgets the
    // teachers, stops being the Device Owner and asks Android to uninstall it.
    // "Restaurar de fábrica" is WipePhone. An agent from before this answers
    // "Ainda não funciona em celular.".
    RemoveMidviewFromPhone = 106,

    // Master -> Android agent on the screen connection, after StartControl:
    // "Travar o toque do aluno" (StudentTouchLock), one byte, 1 locks the
    // student's touch while the teacher controls and 0 frees it; sent again
    // whenever the teacher changes it. Sent to phones and tablets only. An
    // agent from before it ignores it, as it ignores any unknown type there.
    LockStudentTouch = 107,
}

public enum CloseAppsMode : byte
{
    // Like clicking each window's X: programs with unsaved work ask first.
    Ask = 0,

    // End the programs without asking (the warning before it was the
    // student's time to save) -- unsaved work is lost.
    Force = 1,
}
