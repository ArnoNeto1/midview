# Midview — Close Programs (Design)

Date: 2026-09-24
Status: Implemented 2026-09-24 (not yet validated live)

## Context

The Treezeview feature Midview reimplements here: at the end of an activity
the teacher closes the programs the students have open, on the whole class
or on one machine. Built on the classroom console and the teacher-message
work (`docs/superpowers/specs/2026-09-23-teacher-message-design.md`).

## Design

- **Two modes** (`CloseAppsMode`):
  - **Ask** (the default): like clicking each window's X. Programs with
    unsaved work ask the student first, so nothing is lost.
  - **Force**: ask the same way, wait up to 10 s, then end the programs
    still running. Unsaved work in those programs is lost, so the Master
    asks the teacher to confirm before sending it.
- **Protocol:** `MessageType.CloseApps`, one byte of payload (the mode),
  sent as the only message of a short-lived connection and answered with
  one `CommandAck` carrying a `CommandResult` (1 or 0, then a UTF-8 detail
  such as "pedido de fechamento enviado a 3 janela(s)"). `CommandResult`
  was pulled out of `FileTransfer`, whose acks use the same layout.
- **Why the Session, not the Agent:** the Session runs as the student in
  their own login, so it can only ever touch the student's own programs --
  nothing elevated, nothing of another user, nothing of Windows itself.
  Like previews, messages, chat and files, a `CloseApps` connection is
  identified by its first message and never takes the one-controlling-Master
  slot, so it works while the teacher is controlling the machine.
- **What gets closed** (`AppClosePolicy`, pure rules, tested anywhere): the
  windows Alt+Tab shows -- visible, not cloaked, unowned (dialogs close with
  their owner), not tool windows, titled. Never Midview's own windows (the
  teacher's message, the chat), never the shell: the desktop (`Progman`,
  `WorkerW`), the taskbar (`Shell_TrayWnd`, `Shell_SecondaryTrayWnd`), and
  the shell's panels such as Start, search or notifications (top-level
  `Windows.UI.Core.CoreWindow`s; Store apps live in an
  `ApplicationFrameWindow` and are closed through it).
- **Closing** (`WindowsAppCloser`): `WM_CLOSE` is posted to each of those
  windows; the reply counts the ones Windows accepted it for (it refuses an
  elevated program's window). In Force mode, the processes behind them
  that are still running after the grace period are ended -- but never
  explorer (it also runs the desktop and taskbar), ApplicationFrameHost (it
  hosts every Store app at once), the shell's panel hosts
  (StartMenuExperienceHost, ShellExperienceHost, SearchHost, SearchApp,
  TextInputHost, LockApp) or Midview, and only processes in the Session's
  own login session. Each is remembered by id and start time, so a new
  process that reused the id of one that closed during the wait is never
  ended by mistake.
- **Master:** "Fechar programas da turma" (toolbar) and "Fechar
  programas..." (a tile's right-click menu) open `CloseAppsWindow`: choose
  the mode, confirm if forcing, see "Concluído em k de N" and one line per
  machine with what it did or why it failed. Every machine at once (an
  offline one never holds up the rest). `CommandClient.SendCommandForResult`
  returns the ack's detail; its 20 s timeout covers Force's 10 s wait.

## Out of scope

Choosing which programs to close, closing browser tabs rather than whole
browsers, blocking programs from being opened again, and logging off or
shutting down machines.

## Testing

- `CommandResultTests` (Protocol): round trip with accents, empty payload.
- `AppClosePolicyTests` (Session, Windows-only project; also run on Linux
  in the scratch project): an ordinary program is closed; the desktop,
  taskbar and shell panels never are; Midview's own windows never are;
  windows Alt+Tab doesn't show are left alone; Store apps are closed
  through their frame; explorer, ApplicationFrameHost, the shell's panel
  hosts and Midview are never forced.
- `SessionServerTests` (Windows-only): Ask and Force reach the closer and
  report counts; nothing open is reported as such; no closer and invalid
  requests are refused without closing anything; it runs while another
  Master holds the control slot.
- `CommandClientTests` (Master, Windows-only): Force over a real loopback
  `SessionServer` returns the Session's detail.
- Checked on Linux by compiling the non-UI sources into a scratch project:
  all of the above except what needs GDI+, plus Ask, Force and an invalid
  request while another Master holds control, without releasing its input
  lock. `WindowsAppCloser` and `CloseAppsWindow` are live-tested only.
