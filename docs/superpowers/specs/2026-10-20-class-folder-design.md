# Class folder ("Pasta da turma")

## Context

The owner teaches the school's computer lab: 25 fixed PCs and 6 notebooks
handed out when a class has more than 25 students. Students download
images and files during a lesson, and today a file stays on the PC that
downloaded it, so a student has to sit at the same PC every lesson to find
their work again. A Windows network share would fix that, but Windows 10
and 11 accept at most 20 computers connected to a share at once, and the
lab has 31.

The owner wants every lab PC to see the same folder for the class in the
room, with no limit on the number of PCs, and the teacher in charge of it:

> "Vamos fazer por turma e não por aluno, eu escolho a turma sempre que
> inicia a aula, e guarda os arquivos automaticamente, sem limite, no final
> da aula eu decido se apaga tudo ou só de hoje ou mantenho os arquivos."

Midview already moves files between the teacher's PC and the students'
over its own connection ("Enviar arquivo", "Recolher arquivos"), which no
Windows share limit applies to. This feature uses the teacher's PC as the
hub: every file a student saves in the class folder goes to the teacher's
PC and from there to every other lab PC.

Considered and set aside during brainstorming:

- **The student picks the folder on each download** (Chrome's "Ask where to
  save each file", `PromptForDownloadLocation`): students would pick the
  wrong class, and the dialog always opens in Downloads (see "Verified
  Chrome behaviour").
- **A Linux + Samba file server** (an old notebook): no 20-PC limit, but
  needs new hardware set up and kept running. The owner prefers Midview to
  do it.

## Goals

- The teacher picks the class ("turma") at the start of each lesson, in the
  Master. It applies to every lab PC.
- Whatever a student downloads in Chrome lands in the class folder, without
  being asked anything.
- Anything saved in the class folder on any PC (a download, a Word document)
  appears on every other lab PC. A student can sit at any PC and find the
  class's files.
- No limit on the number of PCs.
- At the end of the lesson the teacher chooses: keep the files, delete only
  today's, or delete everything for that class. Kept files come back to
  every PC at that class's next lesson.
- The copy that counts lives on the teacher's PC.

## Non-goals

- Per-student folders or accounts. It is per class.
- Working while the teacher's Master is closed: files saved meanwhile stay
  on the student's PC and spread when the Master opens again (see "A lesson
  nobody ended" for a lesson that outlives its day).
- A student deleting a file does not delete it anywhere else (only the
  teacher deletes, so one student can't erase a classmate's work).
- Keeping every class's files on every PC: a student PC holds only the
  class of the current lesson.
- Other browsers than Chrome (and Edge, see below). Any program can still
  save into the folder by hand.
- Filtering file types. Like a network share, whatever is saved spreads.
- Live editing of the same file on two PCs at once (see "Changes and
  conflicts").

## What the teacher sees (Master)

Sketches were shown and approved during brainstorming.

- **A new toolbar button "Pasta da turma"** in the **Turma** group, after
  "Recolher arquivos" (`OpensOptions`). While a lesson is on, the button
  shows the class's name under its label (a small badge, e.g. "3º A"), so
  the teacher sees which class is in force without opening anything. The
  owner chose a toolbar button over a strip above the grid.
- **The "Pasta da turma" window, before a lesson:** "Escolha a turma desta
  aula. Tudo o que os alunos baixarem ou salvarem na pasta da turma aparece
  em todos os computadores." A list of the classes, each with its number of
  files and the date of its last lesson ("38 arquivos · última aula
  14/10", "vazia"). Buttons: "+ Nova turma", "Renomear", "Excluir",
  "Fechar", and "Começar a aula do <turma>".
- **The same window during a lesson:** "Pasta da turma: 3º A", when the
  lesson started, the number and size of the class's files, and the free
  space on the teacher's disk. A compact list of the lab's PCs with their
  state: ✓ em dia, ⟳ copiando, ○ desligado (recebe ao ligar), ✗ with the
  reason (e.g. "Midview antigo"). Then "Encerrar a aula" with three
  choices, each with a one-line explanation:
  - **Manter os arquivos** — they come back to every PC at the next lesson
    of this class. (Default.)
  - **Apagar só os de hoje** — the files that arrived today go; earlier
    lessons' files stay.
  - **Apagar tudo do <turma>** — every file of the class goes, from the
    teacher's PC and every PC.
  Buttons "Trocar de turma…" (ends this lesson with the chosen choice and
  goes back to the list) and "Encerrar a aula".
- Deleting on the teacher's PC sends files to its Recycle Bin, as
  "Recolher arquivos" already does with collected files.
- The Master warns in this window when the teacher's disk is filling (my
  call: under 5 GB free, or a file wouldn't fit).

## What the student sees

- **A third folder on the Desktop**, next to "Arquivos do professor" and
  "Entregar ao professor": **"Pasta da turma – 3º A"**. It appears when the
  lesson starts and goes when it ends. The student can't delete or rename
  it, exactly like the other two (same protection, by the Agent).
- **A short notice** in the corner when the lesson starts, gone on its own
  after a few seconds: "Aula do 3º A — O que você baixar ou salvar na Pasta
  da turma – 3º A (na Área de Trabalho) aparece em todos os computadores da
  sala."
- **Chrome saves there directly**, without asking. If Chrome was open when
  the lesson started (or the class changed, or the lesson ended), it is
  closed with the notice the Painel do laboratório already uses ("O
  professor atualizou as configurações; o Chrome foi reiniciado."). This is
  required, not cosmetic: see "Verified Chrome behaviour".
- Files saved on other PCs appear in the folder within seconds while the
  Master is open.

## Verified Chrome behaviour

Tested on the owner's PC with Chrome in guest mode (the lab forces guest
mode), with the policies under `HKCU` and removed afterwards, and confirmed
in Chromium's source (`download_target_determiner.cc`,
`download_dir_policy_handler.cc`):

- Guest windows honour the download policies.
- A **mandatory** `DownloadDirectory` sends every download straight into
  that folder and turns off "ask where to save" entirely.
- An **already-open** Chrome keeps saving to the old folder after
  `DownloadDirectory` changes, even after `RefreshPolicy`/`gpupdate`; it
  takes the new folder when Chrome is started again. So every change of
  the folder (lesson start, class change, lesson end) closes the student's
  Chrome.

## How it works

### Roles

- **Master (teacher's PC): the hub and the copy that counts.** It keeps
  each class's files under `Documents\Midview\Turmas\<turma>\` (next to the
  existing `Documents\Midview\Recolhidos`), plus an index per class (below).
  The Master is the client of every connection to the students, as today;
  it drives the whole synchronisation. With two teacher PCs in the room,
  only one of them, the folders' home PC, does this (see "Two teacher PCs in
  one lab").
- **Agent (SYSTEM) on each student PC:** creates and protects the Desktop
  folder for the logged-on student, points Chrome's download folder at it,
  closes Chrome with the notice, removes all of that when the lesson ends,
  and keeps the desired state so it is reapplied after a reboot or a new
  login. It never reads or writes the files inside the folder.
- **Session (the student) on each student PC:** all file reading and
  writing inside the folder, as the student, exactly like `FileInbox` and
  `DeliveryFolder` today. A student can't use it to reach anything they
  couldn't reach themselves, so no SYSTEM code ever touches student-made
  files or links (see "Security").

### Lesson state

- The home Master saves the current lesson (class, lesson id, start time)
  in `MasterData.Folder`, so closing and reopening it carries on the same
  lesson.
- Starting a lesson, changing class or ending it sends the new state to
  every lab PC's Agent (all at once, like the Painel) and records a
  per-PC result in the window. A PC that was off or unreachable is brought
  up to date when it is next seen online (the `ThumbnailPoller` already
  resends Agent state when a machine comes back; this joins that).
- The Agent stores the state like `lab-config.json` (atomic save, corrupt
  file set aside, newest wins by the Master's timestamp, reconciled at
  startup), and reapplies it at every student login, when it already
  protects the Desktop folders.

### The student's folder and Chrome

- Folder: `<Desktop>\Pasta da turma – <turma>`, created and protected by
  the Agent through `ProtectedStudentFolders` (same owner, permissions and
  never-follow-a-link rules as the other two folders; the folder guard
  that puts them back within one poll covers it too). When the lesson ends
  the folder is released and its contents go to that student's Recycle Bin
  (done by the Session, as the student) — the copy that counts is on the
  teacher's PC.
- Chrome: mandatory `DownloadDirectory` set to that exact folder. My call:
  in the logged-on student's own policy hive
  (`HKEY_USERS\<SID>\Software\Policies\Google\Chrome`), written by the
  Agent, because the folder is on that student's Desktop, which may not be
  under the profile folder (`${user_home}` can't express it). Students
  can't write their own `Software\Policies` key. Removed at lesson end and
  at uninstall. Also set `Microsoft\Edge` the same way, which costs nothing
  and covers Edge when the Painel doesn't block it.
- Chrome is closed in the student's session with the existing notice on
  every change of the folder, reusing the Painel's path
  (`AppBlockEnforcer.EndNow` + `IProcessInspector.Notify`).
- "Bloquear downloads" in the Painel still wins: with it on, nothing
  downloads at all.

### Synchronisation (Master-driven)

The Master runs a sync loop per online lab PC while a lesson is on. Each
round:

1. Ask the Session for its folder's listing: relative path, size, last
   write time for each file (subfolders included down to
   `FileCollection.MaxDepth`; hidden and system files, links, and files
   still being written — Chrome's `.crdownload`, `~$` Office lock files,
   Midview's own `.parcial` temp files — are left out). The reply also
   carries the lesson id the Session believes is on, so a stale PC is
   caught.
2. Compare with the class's store and index on the Master and decide per
   file:
   - **On the PC, not in the index:** new → pull it into the store, add it
     to the index (with the date it arrived).
   - **On the PC and in the store, different:** a change or a different
     file with the same name → see "Changes and conflicts".
   - **In the store, and missing on the PC or still at the version the PC
     last got while the store has moved on:** push it (a student deleting
     a file gets it back; a PC that just came online gets the whole
     class; another PC's edit reaches this one).
   - **In the index but gone from the store:** the teacher deleted it on
     the Master → delete it on the PC (to the student's Recycle Bin).
3. Pull with a message like "Recolher arquivos" but for named paths; push
   with a message like "Enviar arquivo" but into the class folder, with
   the relative path and the file's last write time, written to a temp
   file and moved into place as the student.

A file is only pulled once its size and time are unchanged between two
listings, so a download in progress or a document being saved isn't
copied half-written. Transfers run at most 4 PCs at a time (as Enviar and
Recolher do); listings are cheap and run for every online PC every few
seconds (my call: about every 5 s, alongside the 3 s thumbnail poll).

### Changes and conflicts

The owner agreed "the newer version wins" for a file a student changes.
Two different files that happen to share a name are not a change, and the
lab will hit that constantly: with no "save as" prompt, downloads keep the
site's name (`images.jpg`, `download.jpg`), and students' documents are
often `trabalho.docx`. So (my call, refining the owner's rule so no work is
lost):

- The Master remembers, per PC and per path, which version of the file
  (size and time) it last pushed to or pulled from that PC.
- **A change:** the PC's file differs from the version that PC last had of
  that path → it is an edit of the class's file → it replaces the store's
  copy (the replaced copy goes to the teacher's Recycle Bin first) and
  spreads.
- **A different file with the same name:** the PC has a file at a path it
  never got from the store, while the store already has a different file
  there → both are kept: the PC's file enters the store under a free name
  in the usual Windows form (`images (2).jpg`), the Session renames its
  local copy to match, and both spread.
- Deciding by "what this PC last had" rather than by comparing clocks also
  keeps a PC whose clock runs behind from losing its edits.
- Two students changing the same class file on two PCs between two rounds
  is not merged: the later one to reach the Master wins, and the other goes
  to the teacher's Recycle Bin. Acceptable for downloads and individual
  work; listed under Non-goals.
- A file open on a PC (Word holding it) that can't be replaced yet is
  retried on the next round; it never fails the round for other files.

### Ending a lesson

1. One last sync round with every online PC, so nothing saved in the last
   seconds is lost.
2. Apply the teacher's choice to the class's store and index on the
   teacher's PC: keep; delete files that arrived today (by the index's
   arrival date, not the file's own date); or delete all. Deleted files go
   to the teacher's Recycle Bin.
3. Tell every Agent the lesson is over: Chrome closed with the notice, the
   policy removed, the folder released; the Session sends the folder's
   contents to the student's Recycle Bin.
4. A PC that was offline at the end is told when next seen; its leftover
   files go to its own Recycle Bin, not to the class (my call: they are
   still recoverable on that PC, and pulling them in after the teacher
   chose "apagar" would surprise).

### A lesson nobody ended

Decided with the owner: **a lesson ends on its own at the end of its day,
keeping the files** ("Manter os arquivos"). The next day the teacher picks
the class again. Otherwise a forgotten "Encerrar" would send the next
morning's class's downloads into yesterday's class.

- Each Agent ends a lesson that started on an earlier calendar day by
  itself (checked at startup, at login and periodically), even with no
  Master around: Chrome's policy removed, the folder taken off the Desktop.
- Files saved during that lesson that hadn't reached the teacher's PC yet
  are not lost: the Session sets them aside (outside the Desktop, with the
  lesson they belong to), and the teacher's Master pulls them into that
  class's store the next time it sees the PC, then clears them.
- When the Master opens on a later day with yesterday's lesson still
  recorded, it ends it the same way ("manter") without asking.
- "Apagar só os de hoje" stays "files that arrived today" (by the arrival
  date in the index), as the owner put it.

### Two teacher PCs in one lab

Midview will be offered to other teachers, who will normally have a single
teacher PC; that case must work with nothing to set up. The owner's own lab
has two teacher PCs: the room's PC, which whoever is teaching uses, and the
owner's PC, a full teacher PC used to support the lesson ("eu só vou usar
como suporte, auxiliando ele"). The owner wants the same functions on both
without the two getting in each other's way. Decided with the owner:

- **The class files live on one PC only, the folders' home PC ("dono
  das pastas").** Two separate copies would drift apart and one would bring
  back what the other deleted.
- **The first teacher PC to start a lesson becomes the home PC.** It is
  recorded on every student PC's Agent (the home Master's certificate
  thumbprint, its computer name and how to reach it) and outlives lessons.
  With a single teacher PC, that PC is the home PC from its first lesson
  and nothing else changes.
- **Any other teacher PC becomes, on its own, a remote control of the home
  PC.** It learns which PC that is from the Agents and shows the same
  "Pasta da turma" window with the home PC's state (classes, lesson, each
  PC's state, the free space on the home PC's disk). Everything done there
  — start, change class, end, add/rename/delete a class — is sent to the
  home Master, which does it as if clicked there; both windows update.
- **This needs the home Master to listen** for its colleagues: a new TLS
  listener on the home Master (a fixed port next to `TeacherInvite.Port`
  28902), the remote pinning the home PC's thumbprint from the Agents. The
  home Master only accepts a teacher PC the lab's Agents already trust (a
  colleague given access with "Dar acesso a um colega"); how it checks
  that is left to the builder, but nothing unauthenticated may control a
  lesson.
- **Home PC's Midview closed:** the remote shows "O Midview do PC <nome>,
  que guarda as pastas das turmas, está fechado." and offers only "Encerrar
  a aula" (keeping the files), sent straight to the Agents. Files not yet
  on the home PC are set aside on the student PCs, exactly as for "A lesson
  nobody ended", and reach the home PC when it next sees each PC.
- **"Guardar as pastas das turmas neste computador":** in the window of a
  teacher PC that isn't the home PC, for when the wrong PC became the home
  PC. With the home PC reachable, its classes and indexes are copied over,
  the Agents are told the new home PC, and the old one moves its copy aside
  to `Documents\Midview\Turmas (passadas para <PC>)` rather than deleting
  it (my call). With the home PC unreachable (that PC is gone), the window
  says so and, after a confirmation, takes over with no classes; files set
  aside on student PCs still arrive.
- An Agent refuses a lesson start from a teacher PC that isn't the
  recorded home PC (the answer names it), so two PCs starting at once
  can't split the room; the refused PC turns into a remote.
- Every other Midview function on the owner's support PC is unchanged: it
  stays a full teacher PC.

#### Revision (as built)

Plan `docs/superpowers/plans/2026-10-21-class-folder-two-teacher-pcs.md`
settled what this section left open; the reviews changed some of it:

- **Which PC is the home: generations, not a majority.** The home is a
  teacher PC's certificate and computer name, plus its generation: when it
  became the home (UTC ticks on its own clock). The Agents keep the newest
  claim (never past the last tick a clock can show; no student PC's clock
  counts); a take-over must bring a newer one. The same PC is known under a
  new name (same certificate and generation) or a new certificate (same name).
  Masters ask every 30 s and when the window opens: newest, then last name.
- **Becoming the home.** The first start in a lab makes its PC the home. A
  PC that saw another as the home becomes it only with "Guardar as pastas
  das turmas neste computador", as does one whose generation is older than
  a home it saw. Two PCs claiming at once settle it before any lesson goes
  out: the newer generation (then the last name) takes the other's PCs back;
  the other, or a PC holding none, starts nothing. A start that finds another
  home's lesson on a PC starts nothing while that lesson is the room's newest
  (or can't be read); an older one only leaves that PC out. A lesson a PC
  refuses for a home outranking this one is ended on the PCs that took it.
- **Stragglers and refreshes.** A PC off during a take-over still names the
  old home: the home's next start, and its Master once per connection, claim
  it back (a lesson that came with the copied classes is ended there first),
  and claim the others too, refreshing the home's address, port and
  certificate. Never a PC in a lesson of a home with another certificate.
- **Ends.** An Agent takes an end only of the lesson it follows, or with no
  lesson on. A PC still in a lesson its home ran and moved on from (or, for a
  home that never saw another, any lesson it doesn't know) is handed that
  end first. A lesson held while another PC is, or may be, the home (seen,
  named, or a PC's lesson newer) ends only on the PCs still in it, a second
  after it; an Agent takes its home's start of that second, or the one
  before, over such an end.
- **Set-aside files.** Those of a lesson the Master doesn't know stay on the
  PC (usually the other home's) until 60 days after it first saw them, then
  go to the student's Recycle Bin; a known lesson's come in, or are cleared
  if it was collected or its class deleted. A take-over merges both lessons
  maps; one from a home that doesn't answer learns the room's lessons.
- **Busy Agents.** An Agent waits at most 10 s for its lesson lock, then says
  "busy" and applies nothing later; the Master asks again (at once, or in 10 s).
- **Colleagues.** The home serves its window to a PC with its certificate or
  one an online Agent trusts ("Dar acesso a um colega"): a yes is kept 10 min,
  a no 30 s, a yes last to go; one round per teacher and per address at once.
- **The remote window.** The home is closed after three polls in a row found
  nobody (off, a new address or no firewall rule, the window says). Then only
  "Encerrar a aula" works, keeping the files, sent straight to the Agents; the
  home adopts it when it opens. Texts about its disk say "do PC <nome>".
- **Taking the folders over.** Only with no lesson on, on either PC; none
  starts on the old home from its copy until that settles. The copy is
  checked in a staging folder (paths, sizes, free space) before anything
  changes; "Cancelar" works until it goes in place. This PC's own classes go
  to `Turmas (antes de <data>)`, the old home's to `Turmas (passadas para
  <PC>)`; an old home with a lesson on lets go when it ends. A lost claim
  answer is waited out; one that can't be told has the old home take the room
  back, and only if that fails does the copy stay until the Agents decide.
  Files the store doesn't list stay in the old home's copy.
- **Switching modes.** A Master switches between working its own folders and
  the home's only when two readings in a row say so (at once when the Agents
  name it, or it handed the folders over). It
  never stands down while a lesson of its own is on that the room follows;
  once it ends (or a start backs out) it switches, its end handed a few
  minutes more to the PCs still in it. One the room left behind is ended,
  keeping the files. Standing down with no hand-over leaves its `Turmas` as
  it is, and its window says so. Nothing changes while a take-over isn't
  over: the old home's listener (open only while it is the home, or no PC
  is) stays open until the taker says how it went, or a few minutes pass.
  The Master doesn't close while a take-over runs (unless Windows shuts
  down), asks while a copy only awaits the taker, and says where asides are.
- **Firewall.** The teacher setup adds and removes the rule
  `MidviewProfessorPastaDaTurma` (TCP 28903, this program only, local network
  only) with the invite's rules. The Master never opens the firewall.
- **Known limits.** Two Windows accounts sharing a copied `master-identity.pfx`
  can't be told apart; teacher PCs whose clocks differ by 2 seconds or more
  fall outside the same-second rule; a hostile trusted teacher can push the
  generations to the ceiling (`class-home.json` on the Agents recovers). A
  start whose claims all found the Agents busy, and whose lesson every PC then
  refuses, leaves a lesson on here with no PC in it until it is ended. If the
  other teacher PC goes off with its lesson on, this PC's starts are refused
  until the PCs end that lesson with the day. Closing a Master anyway while
  its copy awaits the taker can leave that take-over undecided: the copy
  stays (nothing deleted; the window says where) until the room decides.

### Classes

- The list of classes is the folders under `Documents\Midview\Turmas`.
  "+ Nova turma" asks for a name (made safe for a folder name, like
  `FileNames.Safe`); "Renomear" renames the folder (not during that class's
  lesson); "Excluir" sends the class folder to the Recycle Bin after a
  confirmation that says how many files go.
- A class's index (paths, size, time, arrival date) lives with the Master's
  data, not inside the class folder, so the teacher browsing or deleting in
  Explorer can't corrupt it. A file the teacher adds in Explorer is new to
  the index and goes to the PCs like any other.

## Limits

- No limit on PCs or on the number of files, beyond what the protocol
  already has: 1 GiB per file (`FileTransfer`), depth 8.
- Each student PC gets every file of the current class: a large video is
  copied to all 31 PCs. The Session refuses a push that would leave its
  disk nearly full (reusing `FileInbox.RefusalFor`), and the PC shows ✗
  with the reason in the Master's list.

## Compatibility and safety

- New message types, numbered from 65. An Agent from before this answers
  `CommandAck` and is shown as "Midview antigo"; a Session from before
  answers `FrameTile`, mapped to `CommandClient.OlderSession` and shown the
  same way. Neither breaks the lesson for the other PCs. No new message may
  be one a Session control connection can open with.
- **Security.** Only the Session, running as the student, reads or writes
  inside the folder. The Agent's only filesystem work is the protection it
  already does on Desktop folders (opened as themselves, never through a
  link). Paths coming from either side are made safe and checked to stay
  inside the class folder or store (`FileCollection.SafeRelativePath`, the
  Master's `IsInside`), on receipt as well as on send. Linked folders are
  skipped when listing. The Master never runs or opens a received file.
- Uninstall releases the folder like the other two and removes the
  download-folder policy.
- The home Master's listener needs the teacher installer's firewall rule
  ("Firewall" above); a Master updated without it still works as the home
  for its own lessons, and only remote control from another PC fails.

## Testing

- `Midview.Protocol.Tests`: encode/decode round-trips and rejection of
  malformed payloads for every new message (listing, pull, push, lesson
  state).
- `Midview.Master.Tests`: the per-file decision (new, changed, missing on
  PC, deleted by the teacher, still being written) as a pure function over
  two listings and an index; the store and index (arrival dates, "só os de
  hoje", "tudo", corrupt index); sync rounds against loopback Sessions
  (real `SessionServer` on port 0) with three PCs; ending a lesson with
  each choice; old Agent and old Session paths; two Masters over loopback
  (the home and a remote, two starts at once, the home's Midview closed,
  handing the folders over).
- `Midview.Session.Tests`: listing skips hidden, linked and in-progress
  files; a push writes through a temp file and keeps the time; a push with
  `..` or an absolute path is refused; a file held open is retried, not
  fatal.
- `Midview.Agent.Tests`: lesson state save/corruption/newest-wins/
  reconcile; the folder's protection added and released (temp Desktop, as
  the existing folder tests); the download policy written and removed
  under a redirected registry root (a throwaway `HKCU` subtree, never the
  real policy keys); Chrome closed through the fake `IProcessInspector`.
- No test touches the real lab PCs, the real Chrome policy keys or a real
  browser. The Master window is checked with the fake-students harness.

## Left to the builder

- Message names and payload layouts; how the listing is chunked for a
  class with many files.
- Whether pulls and pushes reuse the `SendFile`/`FileChunk` messages with
  a new header or get their own.
- The exact sync interval and back-off for PCs that keep failing.
- The badge's look on the toolbar button and the window's exact layout,
  within the sketches.
- How the index is stored (one JSON per class is fine).
