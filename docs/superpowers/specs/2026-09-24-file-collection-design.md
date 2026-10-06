# Midview — File Collection (Design)

Date: 2026-09-24
Status: Implemented 2026-09-24 (not yet validated live)

## Context

The reverse of file distribution
(`docs/superpowers/specs/2026-09-24-file-distribution-design.md`): at the end
of an activity the teacher collects the students' work, from the whole class
or from one machine, into one folder on their own PC.

## Design

- **Only what the student hands in.** Each student has an "Entregar ao
  professor" folder on their Desktop (`DeliveryFolder`, created by the
  Session at login so it's there before the teacher asks). Collection takes
  that folder and its subfolders, never anything else on the machine; the
  teacher can't browse the student's disk. Nothing is removed from it, and
  the student gets a "Mensagem do professor" saying how many files were
  collected, or, if none were, where the folder is.
- **What's listed** (`DeliveryFolder.ListFiles`): files in path order, down
  to 8 path segments; hidden and system files are skipped (Office's
  `~$trabalho.docx` lock files, for one), and so are linked folders
  (junctions, symbolic links), which could point anywhere or loop.
- **Protocol** (`FileCollection`): the Master opens with `CollectFiles`.
  The Session answers with each file in turn: a `SendFile` header
  (`FileTransfer`: size, then the path relative to the folder,
  `/`-separated), then `FileChunk`s adding up to exactly that size. It ends
  with one `CommandAck` carrying a `CommandResult`: success, with the files it
  left out, or why nothing could be collected. The Master never answers in
  between; it stops a collection by closing the connection. Like every other
  classroom feature, it never takes the one-controlling-Master slot.
- **Limits:** 1000 files and 500 MB per machine per collection. The Session
  keeps a normal collection within them: files that would pass them, files it
  can't open (locked without sharing) and paths too long for the header are
  left out and named in the final ack.
- **The Master trusts nothing it receives** (`FileCollector`). A student can
  read their own machine's Session key (see `SessionIdentity`), so a
  rewritten Session could send anything to the teacher's PC:
  - every path is rebuilt from sanitized segments
    (`FileCollection.SafeRelativePath`): `.` and `..` are dropped, each
    segment goes through `FileNames.Safe` (the file-distribution rules, now
    shared in `Midview.Protocol`), and there are at most 8 segments. The
    result is also checked to be inside the destination;
  - nothing is ever overwritten (`notas (2).pdf`, also across collections into
    the same folder);
  - the limits are enforced again, counting every file sent: one past them
    stops that machine's collection;
  - a file is written to a temp file and only gets its name once all of it
    has arrived; more bytes than declared, anything but chunks, or a dropped
    connection discards it;
  - nothing received is ever opened or run.

  A file this PC can't save where it belongs (a path too long for Windows,
  a name clash with a folder) is read off the connection and skipped, so the
  rest still arrives, and is reported. A full disk stops the collection.
- **Where it lands:** `Documentos\Midview\Recolhidos\<yyyy-MM-dd HH-mm-ss>\<machine>\`,
  one folder per collection and per machine (machine names go through
  `FileNames.Safe` too, and two that clean up to the same name get `(2)`). A
  machine with nothing to collect gets no folder.
- **Master UI:** "Recolher arquivos da turma" (toolbar) and "Recolher
  arquivos..." (a tile's right-click menu) open `CollectFilesWindow`:
  "Recolher", "k de N concluídas", then a total and one line per machine
  (files and size, an empty folder, what was left out, or why it failed),
  and "Abrir pasta". At most 4 machines at a time, like sending.

- **Emptying, added 2026-10-05 (asked for during the first live run):**
  two buttons in `CollectFilesWindow`, each confirmed first, each sending
  to a Recycle Bin rather than deleting, so a wrong click can be undone
  (`WindowsRecycleBin`: the shell's delete with undo, no dialogs).
  "Apagar as pastas dos alunos" sends `MessageType.ClearDeliveryFolder` to
  every chosen machine: the Session moves everything in "Entregar ao
  professor" (subfolders whole, linked folders left alone) to the
  student's Recycle Bin, tells the student, and answers "N item(ns) foram
  para a Lixeira do aluno", naming what couldn't go and why.
  "Apagar os recolhidos neste PC" does the same with
  Documents\Midview\Recolhidos on the teacher's PC (`CollectedFiles`). An
  older Session answers a new command with its screen; the Master says
  "Midview antigo nesta máquina: atualize-a para usar isto"
  (`CommandClient.OlderSession`).

## Out of scope

Archiving the students' folders, collecting from any other folder,
per-student (rather than per-machine) organization, and scheduling a
collection.

## Testing

- `FileNamesTests`, `FileCollectionTests` (Protocol): the name rules (moved
  from `FileInboxTests`), never overwriting a file or a folder, and paths that
  try to climb out (`../`, `..\`, drive letters, UNC, reserved names,
  streams), too deep, or empty.
- `DeliveryFolderTests` (Session, Windows-only; also run on Linux in the
  scratch project): subfolders in path order, hidden files skipped, the
  depth limit, stopping just past the file limit, linked folders skipped.
- `SessionServerTests` (Windows-only): every file arrives (multi-chunk and
  empty ones included) and the student is told; what would pass the limits
  is left out and named, including files never listed once the limit is
  reached; an empty folder still answers; no folder is refused; it runs
  while another Master holds control.
- `FileCollectorTests` (Master, Windows-only; also run on Linux): a real
  loopback Session's folder arrives identical with its subfolders;
  collecting again never overwrites; an empty folder creates nothing.
  Against a hostile Session: paths that climb out all land inside;
  more bytes than declared, too many files, too many bytes, a mid-file drop
  and a non-file message are all stopped and leave no partial file; a file
  that can't be saved is skipped while the rest arrives.
- Checked on Linux by compiling the non-UI sources into a scratch project,
  plus a collection while another Master controls the machine that doesn't
  release its input lock. `CollectFilesWindow` is live-tested only.
