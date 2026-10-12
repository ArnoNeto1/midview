# Midview — Teacher Message (Design)

Date: 2026-09-23
Status: Implemented 2026-09-23 (not yet validated live)

## Context

One of the Treezeview features Midview reimplements: the teacher types a
short text in `MidviewMaster` and it appears on the students' screens —
for the whole class or for one machine. Built on the classroom console
(`docs/superpowers/specs/2026-09-22-classroom-console-design.md`).

## Design

- **Protocol:** `MessageType.ShowMessage`, a UTF-8 text payload, sent to
  the Session as the only message of a short-lived connection and answered
  with one `CommandAck` (1 = shown, 0 = refused). `TextMessage` in
  `Midview.Protocol` (named `TeacherMessage` until the private chat reused
  it) holds the rules both sides share: text is trimmed,
  must be non-empty valid UTF-8, and at most 2000 characters (the Master's
  text box enforces the same limit).
- **Why the Session, not the Agent:** showing a window needs the student's
  desktop, which only the Session has. Like thumbnail previews, a
  `ShowMessage` connection is identified by its first message and never
  takes the one-controlling-Master slot, so a message reaches a machine
  even while the teacher is controlling it.
- **Session display:** `WinFormsMessageDisplay` shows each message in its
  own topmost window ("Mensagem do professor", the text in a read-only
  scrolling box, an OK button) on a short-lived STA thread, since Session
  is a console app with no UI thread of its own. `Show` returns as soon as
  the window is up; the ack doesn't wait for the student to close it.
  Several messages can be open at once.
- **Master:** "Enviar mensagem para a turma" in the grid's toolbar, and
  "Enviar mensagem..." in a tile's right-click menu, open
  `SendMessageWindow`. It sends to every target in parallel (an offline
  machine never holds up the rest) and reports "Enviada para N de M",
  listing each machine it didn't reach and why. Text that can't be encoded
  (an unpaired UTF-16 surrogate: half of an emoji, pasted) isn't sent: the
  window says so and selects the character, keeping the text to fix
  (`TypedText`; until 2026-10-12 it closed the Master).
- **`CommandClient`** (renamed from `AgentCommandClient`, now used for the
  Agent's site-blocking commands and for messages) gained a 5 s connect
  timeout, so a switched-off machine fails in seconds instead of Windows'
  ~21 s TCP timeout.

## Out of scope

Replies from students (that's private chat, its own sub-project),
scheduled or recurring messages, and message history.

## Testing

- `TextMessageTests` (Protocol): encoding round trip with accents,
  trimming, empty/whitespace, length limit, invalid UTF-8.
- `SessionServerTests` (Windows-only): a message is shown and acked;
  whitespace-only is refused without showing anything; a message still
  gets through while another Master holds the control slot.
- `CommandClientTests` (Master, Windows-only): a message to a real
  loopback `SessionServer` is shown and acked; nothing listening fails
  quickly instead of hanging.
- `TypedTextTests` (Master): the first unpaired surrogate is found exactly
  where `TextMessage.Encode` would throw, and whole emoji pass.
- The message window itself and `SendMessageWindow` are live-tested only,
  like the rest of the UI.
