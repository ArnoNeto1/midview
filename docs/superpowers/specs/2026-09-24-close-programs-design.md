# Midview — Close Programs (Design)

Date: 2026-09-24
Status: Implemented 2026-09-24; changed 2026-10-05 after the first live run
(the student is now warned before anything closes, and Force ends the
programs directly when the warning is up)

## Context

The Treezeview feature Midview reimplements here: at the end of an activity
the teacher closes the programs the students have open, on the whole class
or on one machine. Built on the classroom console and the teacher-message
work (`docs/superpowers/specs/2026-09-23-teacher-message-design.md`).

## Design

- **The student is always warned first.** The first live run (2026-10-05)
  found that both modes closed programs at once, with no word to the
  student, and that Force's "10 seconds" were only a wait for programs
  that hadn't closed, never time for the student. Now the teacher picks the
  warning: "Avisar o aluno e esperar 1 minuto", "… 30 segundos" or "Agora,
  sem esperar". The Session shows the student "O professor vai fechar seus
  programas em 1 minuto. Salve seu trabalho agora." (Force adds ": o que
  não estiver salvo será perdido."), or, right away, "O professor está
  fechando seus programas." / "O professor encerrou seus programas.", in
  the same window as the teacher's messages, which is never closed.
- **Two modes** (`CloseAppsMode`), applied when the warning is up:
  - **Ask** (the default): like clicking each window's X. Programs with
    unsaved work ask the student first, so nothing is lost.
  - **Force**: end the programs right away, without asking -- the warning
    was the time to save. Unsaved work is lost, so the Master asks the
    teacher to confirm before sending it. Windows whose program is never
    ended (below) still get the normal close.
- **Protocol:** `MessageType.CloseApps` with a `CloseAppsCommand` payload:
  the mode (one byte), then the warning in seconds (uint16 big-endian, at
  most 10 minutes, as a shutdown's). The mode alone, what a Master from
  before the warning sends, means now. Sent as the only message of a
  short-lived connection and answered with one `CommandAck` carrying a
  `CommandResult` (1 or 0, then a UTF-8 detail such as "pedido de
  fechamento enviado a 3 janela(s)"). With a warning, the answer comes
  back at once ("aluno avisado; os programas fecham em 1 minuto") and the
  Session closes the programs on its own when the time is up, so the
  teacher's window can be closed meanwhile and no command waits a minute.
  `CommandResult` was pulled out of `FileTransfer`, whose acks use the same
  layout.
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
- **Closing** (`WindowsAppCloser`): in Ask mode `WM_CLOSE` is posted to
  each of those windows; the reply counts the ones Windows accepted it for
  (it refuses an elevated program's window). In Force mode the processes
  behind them are ended at once -- but never explorer (it also runs the
  desktop and taskbar), ApplicationFrameHost (it hosts every Store app at
  once), the shell's panel hosts (StartMenuExperienceHost,
  ShellExperienceHost, SearchHost, SearchApp, TextInputHost, LockApp) or
  Midview, whose windows get `WM_CLOSE` instead, and only processes in the
  Session's own login session.
- **Master:** "Fechar programas da turma" (toolbar) and "Fechar
  programas..." (a tile's right-click menu) open `CloseAppsWindow`: choose
  the mode and the warning, confirm if forcing, see "Concluído em k de N"
  and one line per machine with what it did or why it failed. Every
  machine at once (an offline one never holds up the rest).
  `CommandClient.SendCommandForResult` returns the ack's detail.

## Out of scope

Choosing which programs to close, closing browser tabs rather than whole
browsers, blocking programs from being opened again, and logging off or
shutting down machines.

## Testing

- `CommandResultTests` (Protocol): round trip with accents, empty payload.
- `CloseAppsCommandTests` (Protocol): round trip, the mode alone means
  now, malformed or too-long warnings refused, and how the warning reads
  ("30 segundos", "1 minuto", "1 minuto e 30 segundos").
- `SessionServerTests`, for the warning: the student is told first and the
  programs close only when it's up, in Ask and in Force (with the lost-work
  sentence); with no warning the student is still told; a Master from
  before the warning still gets its programs closed right away.
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
