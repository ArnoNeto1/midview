# Midview — Class Chat (Design)

Date: 2026-10-12
Status: Implemented 2026-10-12 (not yet validated live)

## Context

The private chat (`docs/superpowers/specs/2026-09-24-private-chat-design.md`)
talks to one machine; "group chat" was left out of it. The class chat, "Bate-papo
da turma", is that conversation with the whole room at once. Design approved
by the owner on 2026-10-12.

## Design

- **No new protocol, nothing new on the students' side.** The class chat is
  the private chat opened on every machine: one `ChatConnection` per machine
  (`ChatOpen`, `ChatText`, heartbeat), so it works with students already on
  the current version. Each student gets the usual "Conversa com o
  professor" window; a machine already in a private chat gets a second
  window, one per conversation.
- **Master:** a "Bate-papo" button in the toolbar's "Turma" group, next to
  "Mensagem", for every machine in the room list. It opens through
  `ShowOnce`, like the other class-wide windows, and says so in the status
  bar when the room list is empty. Its key doesn't include the machines
  (like "Agendar"): one class chat at a time, so asking again after the
  room list changed brings the open one back instead of popping a second
  chat up on every student's screen. It has no options arrow: like "Ligar",
  it acts right away, opening the chat on every student's screen.
- **`ClassChat`** holds the fan-out, behind `IChatConnection` (which
  `ChatConnection` implements) so it's tested with fakes: it connects to all
  machines in parallel, sends the teacher's lines to every machine still in
  the conversation (in parallel, each line after the one before it, so lines
  arrive in the order they were typed), tags each reply with its machine's
  name ("Aluno-03: terminei") for the teacher alone — replies are never
  passed on to the other students — and drops a machine whose connection
  closes or whose send fails.
- **Teacher's window** (`ClassChatWindow`, "Bate-papo da turma"): the look of
  `ChatWindow`, with a status line: "Conectando a 20 máquinas...", then
  "Conectado a 18 de 20 máquinas.", followed by whoever is still awaited,
  couldn't connect (with the reason, as in the screen-lock status) or left.
  With nobody left it says to close and reopen to reconnect. The teacher can
  type as soon as one machine is in. A line that can't be encoded (text
  pasted with half an emoji) is refused with a note instead of being sent.
  Closing the window ends the conversation on every machine, including
  ones still connecting, and machines whose turn to connect hadn't come yet
  aren't opened at all.
- `ChatConnection.Start` arms the heartbeat before starting the receive
  thread, so a connection the student's side drops at once can't make it
  arm an already disposed timer.
- The classroom window's default width goes from 1360 to 1440 px: the
  toolbar now needs about 1410 px for one row (measured), and wraps a whole
  group onto a second row on a narrower window or screen.

## Out of scope

Students seeing each other's replies, students starting a conversation,
machines that join the room list while the chat is open (reopen it to
include them), history kept across conversations.

## Testing

- `ClassChatTests` (Master): with fake connections — connecting to every
  machine, the status while connecting (with the first machine slow, so the
  others must not wait for it), a line reaching every machine and reaching
  them at once, a reply tagged with its machine and not sent to the others,
  a machine that can't connect (with its reason), one that leaves and one a
  line can't reach being dropped, the status with nobody left, names in the
  room's order, lines kept in order, a line that can't be encoded failing
  without stopping the next ones, `Dispose` closing every machine and one
  still connecting, and opening nothing once done. One test runs over two real loopback `SessionServer`s, so the
  unchanged student side is covered too.
- The window and the toolbar button are live-tested only.
