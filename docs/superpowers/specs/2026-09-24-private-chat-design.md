# Midview — Private Chat (Design)

Date: 2026-09-24
Status: Implemented 2026-09-24 (not yet validated live)

## Context

The Treezeview feature Midview reimplements here: a private text
conversation between the teacher and one student. Built on the classroom
console and the teacher-message work
(`docs/superpowers/specs/2026-09-23-teacher-message-design.md`).

## Design

- **Teacher-initiated only.** The Master never listens for connections
  (every connection goes Master → machine), so a student can't start a
  conversation; they can reply for as long as the teacher's side is open.
  Student-initiated "raise hand" would need the Master to listen, and was
  decided against (2026-09-24): it is not planned.
- **Protocol:** a connection whose first message is `ChatOpen` stays open
  for the whole conversation. `ChatText` (UTF-8, same rules as
  `TextMessage`: trimmed, non-empty, ≤ 2000 characters — renamed from
  `TeacherMessage` since it now covers both directions) flows both ways.
  The Master sends a `Heartbeat` every 5 s so a conversation where nobody
  types isn't dropped by the Session's 15 s receive timeout.
- **Session:** like previews and teacher messages, a chat connection is
  identified by its first message and never takes the one-controlling-Master
  slot, so a conversation works alongside full control of the same machine.
  `ServeChat` reads the teacher's lines on the connection's thread; the
  student's lines are sent from the chat window's thread (one reader, one
  writer, plus a lock).
- **Student's window** (`WinFormsChatWindows`): "Conversa com o professor",
  a transcript, an input box, Enter to send; topmost, on its own STA thread.
  Closing it while the conversation is on only hides it — the teacher's next
  line brings it back. When the teacher leaves, a hidden window closes for
  good and a visible one shows "O professor encerrou a conversa" and
  disables input.
- **Teacher's window** (`ChatWindow`, from a tile's "Conversar (chat)..."):
  one per machine (asking again brings the open one forward), transcript,
  input box, Enter to send. `ChatConnection` owns the connection: a receive
  thread, the heartbeat timer, serialized sends, and a single `Closed` event
  however the conversation ended. Closing the window ends the conversation
  on the student's side; if the connection drops, the window says so and
  the teacher reopens it to reconnect.
- `TcpConnect.WithTimeout` (Master) is now shared by `CommandClient`,
  `ThumbnailPoller` and `ChatConnection` (5 s connect timeout).

## Out of scope

Student-initiated conversations and "raise hand" (decided against, not
planned), group chat, chat history kept across conversations, file sharing
in the chat.

## Testing

- `SessionServerTests` (Windows-only): a chat opens a window and shows the
  teacher's lines (heartbeats ignored); the student's line reaches the
  teacher; the teacher leaving ends the conversation and further student
  lines report failure; a chat opens while another Master holds the control
  slot.
- `ChatConnectionTests` (Master, Windows-only): lines reach both sides over
  a real loopback `SessionServer`; `Dispose` ends the conversation on the
  student's side; the student side closing raises `Closed`.
- Checked on Linux by compiling the non-UI sources into a scratch project:
  all of the above except what needs GDI+, plus an idle conversation
  surviving 18 s (past the Session's 15 s receive timeout) thanks to the
  heartbeat.
- The two chat windows themselves are live-tested only.
