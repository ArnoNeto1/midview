# Midview — File Distribution (Design)

Date: 2026-09-24
Status: Implemented 2026-09-24 (not yet validated live)

## Context

The Treezeview feature Midview reimplements here: the teacher picks a file
in `MidviewMaster` and it's copied to the whole class or to one machine.
Built on the classroom console and the teacher-message work.

## Design

- **Where it lands:** a folder "Arquivos do professor" on the student's
  Desktop (`FileInbox.DefaultDirectory`; the Session runs as the student,
  so it's their own Desktop and they can open, move or delete what arrives).
  The student gets a "Mensagem do professor" notice naming the file and the
  folder. Nothing is ever opened or run automatically.
- **Protocol:** a connection opens with `SendFile` (`FileTransfer` header:
  size as int64 big-endian, then the UTF-8 file name). The Session answers
  with a first `CommandAck` — accepted, or refused with a reason (no inbox,
  invalid header, not enough free disk) — *before* the Master streams
  anything, because a refusal sent mid-stream would likely be lost to a
  connection reset. The file then follows as `FileChunk` messages of up to
  256 KB, until exactly the declared size has arrived, and a second
  `CommandAck` reports the result: 1 plus the name it was saved under, or 0
  plus why not. Maximum size 1 GiB.
- **File names** (`FileInbox.SafeFileName`): only the last path segment is
  kept (`..\..\x.exe`, `C:\Windows\x.exe` → `x.exe`), characters Windows
  forbids — `:` included, which rules out drive letters and NTFS alternate
  streams — become `_`, trailing dots/spaces are dropped, reserved device
  names (`CON`, `NUL`, `COM1`...) get a `_` prefix, empty names become
  `arquivo`, and names are capped at 120 characters keeping the extension.
  An existing file is never overwritten: `notas.pdf`, `notas (2).pdf`...
- **Integrity:** data is written to a temp file in the inbox and only
  renamed to its final name once every byte has arrived and been flushed; a
  transfer that stops, sends more bytes than declared or sends anything
  other than chunks is discarded, leaving nothing behind.
- **Like previews, messages and chat,** a file connection is identified by
  its first message and never takes the one-controlling-Master slot.
- **Master:** "Enviar arquivo para a turma" (toolbar) and "Enviar
  arquivo..." (a tile's right-click menu) open `SendFileWindow`: pick a
  file, send, see "k de N concluídas" and then one line per machine — saved
  as which name, or why it failed. At most 4 machines receive at the same
  time, so the teacher's PC isn't uploading 25 copies of a big file at once.
  `FileSender` does one machine (5 s connect timeout, 30 s per read/write).

## Out of scope

Collecting files back from students, folders (one file at a time), resuming
interrupted transfers, and choosing the destination folder per transfer.

## Testing

- `FileTransferTests` (Protocol): header round trip (accents, empty file),
  size out of range, missing name, invalid UTF-8, result round trip.
- `FileInboxTests` (Session, Windows-only): name sanitization cases
  (traversal, drive paths, streams, forbidden characters, trailing dots,
  reserved names, empty, too long), never overwriting, nothing left behind
  when a transfer doesn't complete.
- `SessionServerTests` (Windows-only): a multi-chunk file is saved and the
  student notified; more bytes than declared is discarded; no inbox is
  refused before anything is streamed.
- `FileSenderTests` (Master, Windows-only): a multi-chunk file arrives
  identical over a real loopback `SessionServer`, and a second copy gets
  "(2)"; a machine that doesn't accept files reports why.
- Checked on Linux by compiling the non-UI sources into a scratch project:
  all of the above, plus a file arriving while another Master holds the
  control slot. `SendFileWindow` is live-tested only.
