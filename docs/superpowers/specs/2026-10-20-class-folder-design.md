# Midview — Class Folder, "Pasta da turma" (Design)

Date: 2026-10-20
Status: Implemented 2026-10-20 (not yet validated live)

## Context

Asked for by a lab teacher: students download images and files during a
lesson and then have to sit at the same computer every time to find them
again. Windows' own file sharing would solve it, but a Windows 10/11 PC
accepts at most 20 connections and the lab has 31 machines. The teacher
asked for it **per class, not per student**: they pick the class whenever
a lesson starts, the files are saved **automatically and without a size
limit**, and at the end of the lesson they decide whether to **delete
everything, delete only today's files, or keep them**.

Everything goes over Midview's own TLS connections to each Session, like
sending and collecting files, so the 20-connection limit doesn't apply and
nothing new is needed on the network (no server, no firewall rule, no new
port: the teacher's PC only connects out, as it always does).

## Design

- **Where the files live:** the teacher's PC keeps one ordinary folder per
  class, `Documents\Midview\Turmas\<class>` (`ClassStore`). It's the class's
  real copy: the teacher can open it ("Abrir pasta"), and whatever they put
  in it reaches the machines at the next lesson. Each machine gets a third
  protected Desktop folder, **"Pasta da turma"**
  (`StudentFolderNames.ClassFolder`; the Agent protects it like the other
  two), holding that lesson's copy.
- **Starting a lesson:** when Master opens with no lesson going on (and
  machines in the room), `StartLessonWindow` asks which class is in the
  lab: one kept so far, or a new name (it must be a valid folder name). The
  lesson bar at the bottom of the main window ("Iniciar aula" / "Encerrar
  aula", "Abrir pasta", and "Aula: 7º A · pasta salva às 10:32 (28 de 31
  máquina(s))", each machine in the tooltip) starts and ends it too. The
  lesson (`ClassLesson`: class, id, start, and what each machine held when
  last saved) lives in `%LocalAppData%\Midview\aula-em-andamento.json`, so
  closing Master and opening it again carries on with the same lesson.
- **Keeping in step** (`ClassFolderSyncer`): right away and then every
  minute, every machine not known to be off, 4 at a time. A machine without
  the lesson is **loaded**; one with it is **saved**:
  - **Load** (`LoadClassFolder`): the Master sends the lesson id, the class
    name and every file of the copy (path, size, last-write time). A
    Session holding another lesson, or none, first sends to the student's
    Recycle Bin whatever in its folder isn't exactly one of those files,
    then asks for the files it lacks (`ClassFolderWanted`); each arrives as
    `SendFile` plus `FileChunk`s and is saved with the time it has on the
    teacher's PC. The Session ends with a `ClassFolderListing` of what it
    now holds. The student sees one message: which class, where the folder
    is, and that what they save there is kept on the teacher's PC.
  - **Save** (`CollectClassFolder`): the Master sends what it last saw on
    that machine; the Session answers with its listing and then every file
    that's new or changed (size or last-write time differ). Nothing is
    shown to the student. Nothing is hashed: size and time are the version.
  - **Merging into the copy** (`ClassStore.Accept`/`Remove`): a file the
    copy doesn't have, or still has exactly as that machine last saw it,
    is replaced by the machine's version. If someone else changed it since,
    nothing is lost: the machine's version goes beside it as
    `notas (PC-05).docx` (that machine's later changes update that copy).
    A file a machine deleted (or renamed) leaves the copy, into the
    teacher's Recycle Bin, only if the copy still has exactly the version
    that machine had. A changed file the machine couldn't read (open
    without sharing) is asked for again next minute.
- **"Sem limite":** no limit on the number of files or the total; each file
  is still capped at `FileTransfer.MaxFileSize` (1 GB, as for sending), a
  listing at 100 000 files (`ClassFolderSync.MaxEntries`, to stay inside
  one message), and a save stops with a clear reason when the teacher's
  disk can't take the next file.
- **Ending the lesson** (`EndLessonWindow`, from the bar, or "Sim" when
  closing Master with a lesson open): the rounds stop, every machine is
  saved one last time, and the teacher chooses:
  - **Manter tudo** (default): the copy stays for the class's next lesson;
  - **Apagar só os de hoje**: the files this lesson added to the copy
    (`ClassLesson.Added`) go to the teacher's Recycle Bin; files that were
    already there stay, with today's changes;
  - **Apagar tudo da turma** (confirmed first): everything in the class's
    folder goes to the teacher's Recycle Bin; the folder stays, so the
    class is still in the list.

  In every case each machine's "Pasta da turma" is emptied for the next
  class (`ClearClassFolder`, into the student's Recycle Bin) and the lesson
  file is deleted. Closing Master with "Não" leaves the lesson open: it
  carries on the next time Master opens, and until then the students' work
  stays only on the machines.
- **Nothing on a machine is ever deleted for good:** what leaves a "Pasta
  da turma" (a new lesson replacing an old one, or the end of a lesson)
  goes to the student's Recycle Bin, since the teacher's PC may not have
  saved it (a lesson left open, a machine that was off when it ended).
- **Two teachers in the same room** ("Entrar na sala de um colega"): a
  machine refuses to load another lesson over one saved less than 5 minutes
  ago (`ClassFolder.ActiveLessonWindow`), saying which class is going on;
  so two open lessons can't keep replacing each other's folder. A lesson
  left behind gives way after those 5 minutes.
- **The Master trusts nothing it receives**, as in file collection: every
  path goes through `ClassFolderFiles.TargetPath` (only paths
  `ClassFolderFiles.List` could have produced: safe segments, at most 8
  deep, no temp names) and is checked to stay inside the class folder; a
  file must be one the machine listed as changed, in listing order; files
  are written to `Turmas\.recebendo` and only moved into place once
  complete; nothing received is opened or run.
- **Older Sessions** take the new messages for a control connection and
  answer with their screen; the Master shows "Midview antigo nesta máquina"
  and doesn't ask that machine again until it restarts.

## Out of scope

Per-student folders, live propagation of one student's change to the other
machines during the lesson (they get it at the next lesson), empty folders
(only files are kept), files whose name Windows would refuse or longer than
`FileNames.MaxNameLength`, or whose whole path passes
`FileTransfer.MaxNameBytes` (left out of the listing), and resolving a
conflict copy automatically.

## Testing

- `ClassFolderSyncTests` (Protocol): request, listing and wanted-list round
  trips; cut-short, oversized and impossible values refused; the listing's
  order and what it skips (hidden, temp, too deep); `TargetPath` only for
  paths the listing could give.
- `ClassFolderTests` (Session): a new lesson recycles what was there but
  keeps its own files; the same lesson again deletes nothing; another
  lesson still going on is left alone until it's been quiet 5 minutes; a
  file from the teacher replaces the old one with the teacher's time; an
  incomplete one leaves nothing; paths outside the folder are refused;
  clearing recycles everything and forgets the lesson.
- `ClassFolderSyncerTests` (Master, over real loopback Sessions): loading
  two machines; new and changed files saved, and nothing re-sent when
  nothing changed; a deleted file leaves the copy into the Recycle Bin; two
  machines changing the same file keep both versions; a delete doesn't
  remove someone else's newer version; a machine that lost the lesson is
  loaded again without touching the copy; the lesson carries on after
  Master restarts; ending the lesson takes out only what it added and
  empties the machines; deleting everything keeps the class listed; a
  colleague's lesson isn't loaded over one still
  going on; a hostile machine can't write outside the copy or send a file
  it didn't list; the class list's natural order; conflict copy names.
- Checked on Linux by compiling the non-UI sources and these tests into a
  scratch project (all pass), and by compiling all of Master's and
  Session's code against the WPF and WinForms reference assemblies with
  the fields the XAML would generate. `StartLessonWindow`,
  `EndLessonWindow` and the lesson bar are live-tested only.
