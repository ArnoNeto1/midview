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
  plus why not (both acks carry a `CommandResult`). Maximum size 1 GiB.
- **File names** (`FileNames.Safe` in `Midview.Protocol`, named
  `FileInbox.SafeFileName` until file collection reused it): only the last path segment is
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
- **Fixed after the first live run (2026-10-05):** every file was refused
  with "Access to the path 'C:\Windows\system32\Arquivos do professor' is
  denied". The Agent started Session with the student's token but with its
  own (SYSTEM's) environment and working folder, and with
  USERPROFILE pointing at the system profile Windows answers the student's
  Desktop -- and LocalAppData -- with an empty path (reproduced by running a
  program with that USERPROFILE), so the folder resolved inside system32.
  `UserSessionProcessLauncher` now passes the student's own environment
  block (`CreateEnvironmentBlock` on their token) and Session's own folder
  as the working folder. The same fix makes "Entregar ao professor"
  (file collection) and `session-perf.log` land in the student's profile.

- **Options, added 2026-10-05 (as in Veyon, asked for during the first live
  run):** "Substituir arquivo com o mesmo nome" replaces a file of the
  same name instead of saving "nome (2)"; an old copy open in a program
  can't be replaced, so the new one is saved beside it and the teacher is
  told. "Depois de enviar" can open the "Arquivos do professor" folder or
  the file itself, with the program Windows opens it with, on the
  student's screen (`IFileOpener`), in which case the notice is left out:
  what opens tells the student by itself. Programs (.exe, .bat, .msi,
  .lnk, scripts...; `FileOpenPolicy`) are never opened, only saved, so a
  wrong click can't run something on the whole class; the window says so
  when such a file is chosen. On the wire: `MessageType.SendFileWithOptions`
  (two option bytes before the usual header), used only when an option is
  chosen; an older Session takes it for a control connection, so the
  Master sends the file again the old way and adds "Midview antigo nesta
  máquina: sem as opções". The ack's detail names what happened:
  "notas.pdf (substituiu o anterior); aberto no aluno".

- **Both Desktop folders protected, added 2026-10-05:** the student can't
  delete, rename or hide "Arquivos do professor" or "Entregar ao
  professor" (`ProtectedStudentFolders`, in the Agent). Every time the
  Agent starts Session it finds the student's Desktop through their token,
  creates the folders if missing, makes SYSTEM their owner and gives them
  their own permissions: SYSTEM and Administrators full; the student may
  list, create, and change or delete what's inside, but not delete,
  rename, hide the folder or change its permissions. The Desktop gets one
  Deny for the student, "delete subfolders and files", the parent right
  that would otherwise delete or rename any item regardless of its own
  permissions; their other Desktop items still go through the delete right
  each one has. As SYSTEM working in folders the student controls, it
  never follows a link: each folder is opened as itself, a junction or
  symbolic link is refused, and permissions are set through that open
  handle, so what was checked is what changes; nothing inside the folders
  is touched. Uninstalling gives them back (the Desktop's Deny goes, the
  folders inherit the Desktop's permissions again, contents kept), for
  every profile on the machine. Checked with a non-administrator account
  standing in for the student: deleting and renaming the folders is
  refused, while files inside and the rest of the Desktop work as before.
  An administrator account can always undo it; student accounts shouldn't
  be administrators. Making the teacher's files read-only is a later step:
  Session saves them as the student, so it can't be done while Session
  writes them.

## Out of scope

Collecting files back from students (its own design:
`2026-09-24-file-collection-design.md`), folders (one file at a time), resuming
interrupted transfers, and choosing the destination folder per transfer.

## Testing

- `FileTransferTests` (Protocol): header round trip (accents, empty file),
  size out of range, missing name, invalid UTF-8. The ack's result layout
  moved to `CommandResult` (shared with close programs) and is tested in
  `CommandResultTests`.
- `FileNamesTests` (Protocol; in `FileInboxTests` until the rules moved):
  name sanitization cases (traversal, drive paths, streams, forbidden
  characters, trailing dots, reserved names, empty, too long).
- `FileInboxTests` (Session, Windows-only): never overwriting, nothing left
  behind when a transfer doesn't complete.
- `SessionServerTests` (Windows-only): a multi-chunk file is saved and the
  student notified; more bytes than declared is discarded; no inbox is
  refused before anything is streamed.
- `FileSenderTests` (Master, Windows-only): a multi-chunk file arrives
  identical over a real loopback `SessionServer`, and a second copy gets
  "(2)"; a machine that doesn't accept files reports why.
- Checked on Linux by compiling the non-UI sources into a scratch project:
  all of the above, plus a file arriving while another Master holds the
  control slot. `SendFileWindow` is live-tested only.
