# Class Folder Implementation Plan — part 2: two teacher PCs in one lab

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In a lab with two teacher PCs, the first one to start a lesson becomes the class folders' home PC; any other teacher PC becomes, on its own, a remote control of it with the same "Pasta da turma" window; a teacher PC can take the folders over; with the home's Midview closed the remote can still end the lesson. A lab with one teacher PC keeps working with nothing to set up.

**Architecture:** The Agents record the home (identity, computer name, address) and only accept a lesson start from it; any trusted teacher may end a lesson. The home Master listens on a new TLS port and serves its window's state and commands to colleague Masters the Agents trust. The window talks to an `IClassFolderController`: a local one (part 1's store and sync) on the home, a remote one (the new client) elsewhere. Taking the folders over copies the home's store through the same link.

**Tech Stack:** .NET 8, C#, WPF (Master), Windows service (Agent), Inno Setup (teacher installer), xUnit.

**Spec:** `docs/superpowers/specs/2026-10-20-class-folder-design.md`, section "Two teacher PCs in one lab" (and "Compatibility and safety" for the firewall rule). Part 1's plan, for the code this builds on: `docs/superpowers/plans/2026-10-20-class-folder.md`.

## Global Constraints

- Build and test only with `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe`; only the three pre-existing `xUnit1031` warnings.
- Never name another classroom-control product in code, comments, docs or commits.
- Commits: plain imperative English subject in the existing style, NO `Co-Authored-By` trailer (rule in `C:\Users\Neto\CLAUDE.md`). Never push (the user approves pushes; master history is linear).
- No test touches real lab PCs, real policy keys, real profiles, the real firewall, or the teacher's real `%LOCALAPPDATA%\Midview` / `Documents\Midview` (temp roots; `MasterData.Folder` and `ClassStore.DefaultFilesRoot` set to temp folders and restored).
- Teacher-facing text in Brazilian Portuguese. Exact texts from the spec:
  - home closed: `"O Midview do PC {nome}, que guarda as pastas das turmas, está fechado."`
  - takeover button: `"Guardar as pastas das turmas neste computador"`
  - old home's copy moved to `Documents\Midview\Turmas (passadas para {PC})`
- New message numbers continue after `ClearSetAside = 74`; Agent ones answer old Masters/Agents compatibly (an Agent without the feature answers `CommandAck [0]`).
- New TCP port `ClassFolderRemote.Port = 28903` (Discovery 28901 and `TeacherInvite.Port` 28902 are taken), firewall rule name `MidviewProfessorPastaDaTurma`.
- Files under ~500 lines (project rule): new code in focused files or partials.
- A single teacher PC must behave exactly as part 1 does (no new prompt, no new setting, no remote mode).

## Decisions this plan makes (the spec left them to the builder)

1. **Home identity = certificate thumbprint + computer name.** Two teacher PCs can share one teacher identity (`InviteHost.SameIdentityText`), so the thumbprint alone can't tell them apart. The name is the Master's `Environment.MachineName`, sent in the claim; the thumbprint is taken from the TLS channel (never from the payload).
2. **The claim comes first.** `StartAsync` sends `ClaimClassHome` to each Agent before `SetClassLesson`. An Agent with no home records the claimer; with a home, it accepts only the same identity (refreshing the address) unless the claim says `TakeOver`. The room's majority decides: a Master refused by most Agents is not home and cancels; a Master accepted by most takes the stragglers back with `TakeOver`. Two Masters starting the very first lesson in the same second could still split the room; the next start by either one resolves it by majority.
3. **Ends are open to any trusted teacher**; starts and class changes only to the home. An Agent from part 1 (no home support) keeps accepting everything.
4. **A Master from part 1** (no claim) starting a lesson on a part-2 Agent with no home: the Agent records it as home with an empty name; the next claim with the same thumbprint fills the name.
5. **Trust between Masters:** the home accepts a colleague whose thumbprint equals its own, or that at least one of the room's online Agents confirms it trusts (`IsTeacherTrusted`), cached for 10 minutes.
6. **The remote polls the home's state every 2 s while its window is open**; commands are one-shot connections (Start/End wait up to 5 minutes, with TCP keep-alive as `CommandClient` does for long commands).
7. **Taking the folders over** is allowed only while no lesson is on. Nothing changes on either PC or on the Agents until the whole store has arrived and its sizes check out. The receiving PC's own earlier store, if any, is moved aside to `Turmas (antes de {dd-MM-yyyy HHmm})`.
8. **Which mode a Master is in** (`ClassHomeTracker`): it asks the online Agents `GetClassHome` when the window opens and every 30 s while the program runs; no answer or no home → local (this PC becomes home when it starts a lesson); home == this PC → local; otherwise remote. A Master that stops being home (another PC took over) stands down: its sync loop stops and its saved lesson is cleared; its store stays on disk.
9. **A lesson ended by a remote while the home was closed**: when the home Master starts (and every 30 s), it reads the Agents' lesson; a newer ended lesson with the same id is adopted as an uncollected end (part 1's `EndUncollected`), so set-aside files come in at the next lesson.

## Review Focus

1. **Two teacher PCs sharing one identity** (same thumbprint, different names): only the claimed name is home; the other is a remote; a remote with the home's own thumbprint is trusted. Tests in Tasks 2 and 4.
2. **The home's address changes** (DHCP) between lessons: every claim by the home refreshes the address on the Agents, and the remote re-reads the home every 30 s. Test in Tasks 2 and 3.
3. **Both windows click at once** (home teacher starts 3º A while the remote ends): commands run one at a time on the home (part 1's lesson gate); both windows show the final state. Test in Task 4.
4. **The remote can reach the Agents but not the home** (home closed, or its firewall rule missing because the teacher PC wasn't set up again): the remote shows the closed text plus a hint about the firewall, and only "Encerrar a aula" (keeping files) works. Test in Task 5.
5. **A takeover interrupted midway** (network drop, old home closed during the copy): the new PC discards its staging copy and nothing changes anywhere; the old home stays home. Test in Task 6.

---

### Task 1: Protocol — the home, its claim, and the Master-to-Master messages

**Files:**
- Create: `src/Midview.Protocol/ClassHome.cs`, `src/Midview.Protocol/ClassFolderRemote.cs`
- Modify: `src/Midview.Protocol/MessageType.cs` (append after `ClearSetAside = 74`)
- Test: `tests/Midview.Protocol.Tests/ClassHomeTests.cs`, `tests/Midview.Protocol.Tests/ClassFolderRemoteTests.cs`

**Interfaces:**
- Produces:
  - `MessageType`: `ClaimClassHome = 75`, `GetClassHome = 76`, `ClassHomeStatus = 77`, `IsTeacherTrusted = 78` (Agent); `RemoteClassFolderState = 79`, `RemoteClassFolderStateReply = 80`, `RemoteClassFolderCommand = 81`, `RemoteClassFolderCommandReply = 82`, `RemoteHandOver = 83`, `RemoteHandOverDone = 84` (Master to Master, on `ClassFolderRemote.Port`, never to a student PC). Comments in the file's style saying who answers what.
  - `public sealed record ClassHome(string Thumbprint, string Name, string Address, int Port)` — `static ClassHome? None`, `bool IsSameIdentity(string thumbprint, string name)` (thumbprint ordinal-ignore-case; name ordinal-ignore-case, and an empty recorded name matches any name), `byte[] Encode()` / `static bool TryDecode(byte[], out ClassHome? home)` (empty payload = no home). Wire: version 1 · thumbprint (uint16 len + ASCII hex, `TeacherInvite.IsThumbprint`) · name (uint16 len + UTF-8, ≤ 64 chars) · address (uint16 len + UTF-8, an `IPAddress.TryParse`-able string) · port (uint16).
  - `public sealed record ClassHomeClaim(string Name, int Port, bool TakeOver)` — `Encode`/`TryDecode` (version 1 · flags byte (bit 0 TakeOver) · port uint16 · name). `const string OtherHomeText = "As pastas das turmas ficam no PC "` — an Agent's refusal detail is `OtherHomeText + name` (the Master recognises it with `StartsWith`).
  - `public static class ClassFolderRemote` — `const int Port = 28903`; `const int MaxJsonBytes = 4 * 1024 * 1024`; JSON DTOs (System.Text.Json, camelCase, validated on decode, `null` on anything malformed):
    - `RemoteState(string HomeName, ClassLessonDto Lesson, IReadOnlyList<ClassSummaryDto> Classes, LessonFilesDto? LessonFiles, IReadOnlyList<PcLineDto> Pcs, IReadOnlyList<string> LastEndProblems, long FreeBytes, long RecycleBinBytes)`
    - `RemoteCommand(string Kind, string? Turma, string? NewName, string? Choice)` — `Kind` ∈ `"start" | "end" | "create" | "rename" | "delete"`; `Choice` ∈ `"keep" | "today" | "all"`.
    - `RemoteReply(bool Ok, string Detail, IReadOnlyList<ResultLineDto> Lines, string? Turma)`
    - DTOs mirror `ClassLesson`, `ClassSummary`, the window's lesson-file numbers (count, bytes, today count, today bytes), `PcStatus` by machine NAME (`PcLineDto(string Name, string State, string Detail)`) and per-PC results (`ResultLineDto(string Name, bool Ok, string Detail)`).
    - `byte[] EncodeJson<T>(T value)`, `T? DecodeJson<T>(byte[] payload)` (refuses > `MaxJsonBytes`).

- [ ] **Step 1: Write the failing tests** — `ClassHomeTests`: round trips; `None` encodes empty; `IsSameIdentity` (same thumbprint other case → true; same thumbprint other name → false; empty recorded name → true); decode refuses a bad thumbprint, a name over 64 chars, an unparsable address, trailing bytes. `ClassHomeClaim` round trip with and without `TakeOver`. `ClassFolderRemoteTests`: each DTO round-trips; `DecodeJson` returns null for malformed JSON, an unknown `Kind`/`Choice`, and a payload over `MaxJsonBytes`.
- [ ] **Step 2: Run them and see them fail** — `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Protocol.Tests --filter "FullyQualifiedName~ClassHome|FullyQualifiedName~ClassFolderRemote"` → build fails.
- [ ] **Step 3: Implement** following `ClassLesson`'s Encode/TryDecode style and `TeacherGrant`'s JSON style.
- [ ] **Step 4: Run them and see them pass** (same command).
- [ ] **Step 5: Commit** — `git add src/Midview.Protocol tests/Midview.Protocol.Tests` · `git commit -m "Add the class folders' home and the messages between teacher PCs"`

---

### Task 2: Agent — keeping the home and gating lesson starts

**Files:**
- Create: `src/Midview.Agent/ClassHomeState.cs`
- Modify: `src/Midview.Agent/ClassLessonHandler.cs` (`Handle` gains the sender's thumbprint), `src/Midview.Agent/AgentServer.cs` (dispatch `ClaimClassHome`, `GetClassHome`, `IsTeacherTrusted`; pass `channel.RemoteThumbprint` and the client's remote IP to the handlers), `src/Midview.Agent/Program.cs` (register `ClassHomeState`)
- Test: `tests/Midview.Agent.Tests/ClassHomeStateTests.cs`, `tests/Midview.Agent.Tests/ClassLessonHandlerTests.cs`, `tests/Midview.Agent.Tests/AgentServerTests.cs`

**Interfaces:**
- Consumes: Task 1 (`ClassHome`, `ClassHomeClaim`).
- Produces:
  - `public sealed class ClassHomeState` — `ClassHomeState(string statePath)` (production `%ProgramData%\Midview\class-home.json`, atomic save, corrupt file → `.corrupt` and no home, like `ClassLessonFile`); `ClassHome? Current`; `(bool Ok, string Detail) Claim(string senderThumbprint, string senderAddress, ClassHomeClaim claim)`:
    - no home → record `(sender, claim.Name, address, claim.Port)`, Ok "este PC guarda as pastas";
    - home and `IsSameIdentity(sender, claim.Name)` → refresh name (if it was empty), address and port, Ok;
    - home, other identity, `TakeOver` → record the sender, Ok "as pastas passaram para este PC";
    - otherwise → `(false, ClassHomeClaim.OtherHomeText + home.Name)` (or `"outro PC de professor"` when the name is empty).
  - `void RecordStarter(string senderThumbprint, string senderAddress)` — decision 4: only when no home is recorded, records `(sender, "", address, ClassFolderRemote.Port)`.
  - `bool MayStart(string senderThumbprint)` — true when no home or the sender's thumbprint is the home's (name was checked at the claim).
  - `ClassLessonHandler.Handle(byte[] payload, string senderThumbprint, string senderAddress)` — a lesson that is on (start or class change) from a sender `MayStart` refuses → `(false, OtherHomeText + name)` without touching anything; with no home → `RecordStarter` then as before; an ended lesson → as before, from anyone. Keep a `Handle(byte[] payload)` overload only if tests need it; production calls the 3-argument one.
  - `AgentServer`: `ClaimClassHome` → `CommandAck` CommandResult (only when the handler exists, else the site-block default `CommandAck [0]`); `GetClassHome` → `ClassHomeStatus` (`ClassHome.Encode()` or empty); `IsTeacherTrusted` (payload: thumbprint, ASCII) → `CommandAck` CommandResult(`_masterTrustStore.IsTrusted(thumbprint)`, "").

- [ ] **Step 1: Write the failing tests** — `ClassHomeStateTests`: first claim records; same identity refreshes the address (Review Focus 2); other identity refused naming the home; `TakeOver` replaces; same thumbprint, other name refused (Review Focus 1); empty recorded name filled by the next same-thumbprint claim; corrupt file → no home. `ClassLessonHandlerTests`: a start from a non-home is refused and changes nothing (no state file write, no folder, no policy); an end from a non-home is accepted; a start with no home records the starter. `AgentServerTests`: claim/get round trip over the `Exchange` helper; `IsTeacherTrusted` true/false; without the handler `GetClassHome` answers `CommandAck`.
- [ ] **Step 2: Run them and see them fail** — `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Agent.Tests --filter "FullyQualifiedName~ClassHome|FullyQualifiedName~ClassLessonHandler|FullyQualifiedName~AgentServerTests"`.
- [ ] **Step 3: Implement**; the sender's address is `((IPEndPoint)client.Client.RemoteEndPoint!).Address` mapped to IPv4 when it is IPv4-mapped.
- [ ] **Step 4: Run the whole Agent test project** — all pass (administrator-only tests skip as before).
- [ ] **Step 5: Commit** — `git commit -m "Let the Agents keep the class folders' home and take lesson starts only from it"`

---

### Task 3: Master — which PC is home (`ClassHomeTracker`) and the engine's claim

**Files:**
- Create: `src/Midview.Master/ClassHomeTracker.cs`
- Modify: `src/Midview.Master/ClassFolderClient.cs` (add `ClaimHome`, `GetHome`, `GetLesson`, `IsTeacherTrusted`), `src/Midview.Master/ClassFolderSync.cs` (claim before starting; `StandDown`; adopt an end made by a remote)
- Test: `tests/Midview.Master.Tests/ClassHomeTrackerTests.cs`, `tests/Midview.Master.Tests/ClassFolderSyncTests.Home.cs`

**Interfaces:**
- Consumes: Tasks 1–2; part 1's `ClassFolderSync`, `ClassFolderClient`, `ClassSyncRig` (test rig).
- Produces:
  - `ClassFolderClient.ClaimHome(machine, cert, ClassHomeClaim claim, port = AgentPort) → (bool Ok, string Detail, bool OldAgent)`; `GetHome(...) → (ClassHome? Home, bool OldAgent)`; `GetLesson(...) → (ClassLesson? Lesson, bool OldAgent)`; `IsTeacherTrusted(machine, cert, string thumbprint, port) → bool?` (null for an old Agent).
  - `public enum ClassFolderMode { Local, Remote }`; `public sealed class ClassHomeTracker : IDisposable` — `ClassHomeTracker(X509Certificate2 masterCertificate, string machineName, Func<IReadOnlyList<KnownMachine>> machines, Func<KnownMachine, bool> isOnline, TimeSpan? interval = null, int agentPort = ConnectionClient.AgentPort)` (interval default 30 s); `ClassHome? Home`; `ClassFolderMode Mode`; `event Action? Changed`; `Task RefreshAsync()` (asks every online Agent at once; ignores old Agents; the home is the one most Agents report, ties broken by the newest answer; `Mode = Remote` only when a home exists and is not this PC's identity).
  - `ClassFolderSync`:
    - constructor gains `string machineName` and `int homePort = ClassFolderRemote.Port`;
    - `StartAsync` sends `ClaimHome(new ClassHomeClaim(machineName, homePort, TakeOver: false))` to each online Agent before `SetLesson`; an old Agent counts as accepted. Then:
      - if more than half of the Agents that answered refused naming another home (`OtherHomeText`), another PC is home: cancel the start (no lesson saved, nothing sent) and return those refusals as the per-PC lines;
      - otherwise this PC is home: re-claim the refusing minority with `TakeOver: true` (a PC that was off at this PC's first lesson and was later claimed by another Master is brought back) and continue with `SetLesson` on every PC as part 1 does;
    - `public void StandDown()` — stops the loop, clears the saved lesson (`ClassLessonFileMaster.Write(ClassLesson.None(...))`), raises `LessonChanged`; the store is untouched;
    - `public Task AdoptAgentsEndAsync()` — reads `GetLesson` from the online Agents; if the newest one has this lesson's id, is ended and newer than ours → `EndUncollected` as part 1 does (no last rounds), adopt it, raise `LessonChanged`. Called at construction (in the background) and every 30 s while a lesson is on.

- [ ] **Step 1: Write the failing tests** (real `AgentServer`s over loopback, as `ClassSyncRig`): tracker — no home → Local; this PC home → Local; another identity home → Remote; old Agents ignored; majority wins. Sync — `StartAsync` claims then starts; a second Master (other cert/name) is refused naming the first and saves no lesson; a straggler (one of three Agents recorded the second Master as home while the first was home on the other two) is taken back by the first Master's next start; `StandDown` clears the lesson and keeps the store; a remote-made end (ended lesson sent straight to the Agents) is adopted as uncollected, and the next lesson's round brings that lesson's set-aside files in.
- [ ] **Step 2: Run them and see them fail** — `... test tests/Midview.Master.Tests --filter "FullyQualifiedName~ClassHomeTracker|FullyQualifiedName~ClassFolderSyncTests"`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole Master test project** — all pass, part 1's sync tests unchanged.
- [ ] **Step 5: Commit** — `git commit -m "Claim the class folders before starting a lesson, and stand down when another PC keeps them"`

---

### Task 4: Master — the window's controller, local and served

**Files:**
- Create: `src/Midview.Master/IClassFolderController.cs`, `src/Midview.Master/LocalClassFolderController.cs`, `src/Midview.Master/ClassFolderHost.cs`
- Modify: `src/Midview.Master/ClassFolderWindow*.cs` (use the controller instead of `ClassStore` + `ClassFolderSync`)
- Test: `tests/Midview.Master.Tests/LocalClassFolderControllerTests.cs`, `tests/Midview.Master.Tests/ClassFolderHostTests.cs`

**Interfaces:**
- Consumes: part 1's `ClassStore`, `ClassFolderSync`; Task 1's DTOs; Task 3's `IsTeacherTrusted`.
- Produces:
  - `public interface IClassFolderController` — `ClassLesson Lesson`; `IReadOnlyList<PcLineDto> Pcs` (names, state, detail — the home's view); `IReadOnlyList<string> LastEndProblems`; `string? HomeName` (null when this PC is home); `bool HomeClosed`; `event Action? Changed`; `Task<RemoteState> ReadAsync(bool lessonFiles)`; `Task<(bool Ok, string Detail, string Turma)> CreateAsync(string name)`; `Task<(bool Ok, string Detail)> RenameAsync(string turma, string newName)`; `Task<(bool Ok, string Detail)> DeleteAsync(string turma)`; `Task<IReadOnlyList<ResultLineDto>> StartAsync(string turma)`; `Task<IReadOnlyList<ResultLineDto>> EndAsync(EndChoice choice)`.
  - `LocalClassFolderController(ClassStore store, ClassFolderSync sync, Func<IReadOnlyList<KnownMachine>> machines, Func<KnownMachine, bool> isOnline)` — exactly part 1's behaviour, moved out of the window (the window's disk reads, `ReadLessonFiles`, the PC list built from roster + statuses).
  - `public sealed class ClassFolderHost : IDisposable` — `static ClassFolderHost Open(X509Certificate2 cert, IClassFolderController local, Func<string, Task<bool>> isTrusted, int port = ClassFolderRemote.Port)`; accepts TLS from any client certificate, then admits only a thumbprint equal to its own or `isTrusted(thumbprint)` (decision 5; production: ask online Agents via `IsTeacherTrusted`, cache 10 min); serves `RemoteClassFolderState` → `RemoteClassFolderStateReply` (`local.ReadAsync`), `RemoteClassFolderCommand` → `RemoteClassFolderCommandReply` (runs the command on `local`, serially with the home's own clicks); refuses anything else; read timeout 10 s, command execution up to 5 min; one connection per request; never throws out of its accept loop; `SocketException` on bind → logged, host stays null (another program owns the port).
  - Window: the constructor takes `IClassFolderController`; everything else (texts, layout, behaviour) unchanged for the local case.

- [ ] **Step 1: Write the failing tests** — local controller: the same flows part 1's window used (create/rename/delete/start/end/read) produce the same results through the interface (reuse part 1's rig). Host: a trusted colleague (other cert, `isTrusted` → true) reads the state and runs start/end; an untrusted one is refused; a client with the host's own thumbprint is admitted (Review Focus 1); two commands at once (host click + remote) run one after the other and both report (Review Focus 3); a malformed JSON command gets an error reply, not a dropped connection.
- [ ] **Step 2: Run them and see them fail** — `... --filter "FullyQualifiedName~LocalClassFolderController|FullyQualifiedName~ClassFolderHost"`.
- [ ] **Step 3: Implement**; move the window's store/sync calls into `LocalClassFolderController` without changing behaviour (part 1's `ClassFolderTextTests` and the window's flows stay green).
- [ ] **Step 4: Run the whole Master test project.**
- [ ] **Step 5: Commit** — `git commit -m "Serve the Pasta da turma window to colleague teacher PCs"`

---

### Task 5: Master — the remote controller and the home-closed fallback

**Files:**
- Create: `src/Midview.Master/RemoteClassFolderController.cs`
- Modify: `src/Midview.Master/ClassFolderText.cs` (remote texts), `src/Midview.Master/ClassFolderWindow*.cs` (remote banner, closed view)
- Test: `tests/Midview.Master.Tests/RemoteClassFolderControllerTests.cs`, `tests/Midview.Master.Tests/ClassFolderTextTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 3, 4.
- Produces:
  - `RemoteClassFolderController(X509Certificate2 cert, Func<ClassHome?> home, Func<IReadOnlyList<KnownMachine>> machines, Func<KnownMachine, bool> isOnline, TimeSpan? pollInterval = null)` (poll default 2 s, only while the window is open — `Start()`/`Stop()`); pins the home's thumbprint from `ClassHome`; `HomeName` = the home's name; on a connection failure → `HomeClosed = true`, `Changed`.
  - Closed fallback: `EndAsync(EndChoice.Keep)` while `HomeClosed` reads the newest lesson from the online Agents (`GetLesson`) and sends the ended lesson (same id, `Ended = true`, `SavedAt = max(now, theirs + 1)`) to every Agent directly, returning per-PC lines; any other command while closed → `(false, ClassFolderText.HomeClosed(name))`.
  - `ClassFolderText`: `HomeClosed(string name)` = `"O Midview do PC {name}, que guarda as pastas das turmas, está fechado."`; `HomeClosedHint` = `"Se ele está aberto, o Midview desse PC precisa ser instalado de novo para aceitar este computador (regra do firewall)."`; `RemoteBanner(string name)` = `"As pastas das turmas ficam no PC {name}. O que você fizer aqui acontece nele."`.
  - Window (remote): the banner above the content; the same list/lesson views fed by the controller; in the closed state only the closed text, the hint, and "Encerrar a aula" (end choices hidden: it always keeps the files).

- [ ] **Step 1: Write the failing tests** — two Masters over loopback (home = real `ClassFolderHost` + local controller with temp roots; remote = `RemoteClassFolderController` with another cert, `isTrusted` → true): the remote sees the home's classes, lesson and PC lines; starts and ends a lesson that the home's window then shows; home host disposed → `HomeClosed`, the remote's `EndAsync(Keep)` ends the lesson on the Agents directly, and the home, reopened, adopts it (Task 3) — Review Focus 4. Texts: exact strings above.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole Master test project.**
- [ ] **Step 5: Commit** — `git commit -m "Let another teacher PC run the class folder through the home PC"`

---

### Task 6: Master — taking the folders over

**Files:**
- Create: `src/Midview.Master/ClassFolderHandOver.cs`
- Modify: `src/Midview.Master/ClassFolderHost.cs` (serve `RemoteHandOver` / `RemoteHandOverDone`), `src/Midview.Master/RemoteClassFolderController.cs` (`TakeOverAsync`), `src/Midview.Master/ClassStore*.cs` (export/import the store, move it aside), `src/Midview.Master/ClassFolderWindow*.cs` (the button and its confirmations), `src/Midview.Master/ClassFolderText.cs`
- Test: `tests/Midview.Master.Tests/ClassFolderHandOverTests.cs`

**Interfaces:**
- Consumes: Tasks 1–5; part 1's `ClassStore` (files root, index root, lessons map).
- Produces:
  - Host side: `RemoteHandOver` (empty payload) → refused `CommandAck`(false, `"Encerre a aula antes de passar as pastas."`) while a lesson is on; otherwise streams every store file as `SendFile` header (path `"files/<turma>/<relative>"`, `"index/<turma>.json"` or `"lessons.json"`) + `FileChunk`s, then a final `CommandAck`(true, "<n> arquivos"). `RemoteHandOverDone` → the old home stands down (`ClassFolderSync.StandDown`), moves its files root to `Documents\Midview\Turmas (passadas para {PC})` (with " (2)"… if taken) and its index root and lessons map alongside, answers `CommandAck`.
  - `ClassFolderHandOver.TakeOverAsync(...)`: receive into a staging folder next to the files root (every path through `FileCollection.SafeRelativePath` + inside check; sizes match headers; total ≤ free space − 1 GB); only when complete: move this PC's own earlier store aside to `Turmas (antes de {dd-MM-yyyy HHmm})`, move staging into place, send `ClaimHome(TakeOver: true)` to every online Agent, then `RemoteHandOverDone` to the old home; any failure before the swap → delete staging, change nothing (Review Focus 5).
  - Unreachable home (`HomeClosed`): after the confirmation `"O PC {nome} não responde. Guardar as pastas neste computador assim mesmo? As turmas que estão nele não vêm junto; os arquivos que ainda estão nos PCs dos alunos vêm na próxima aula."`, claim with `TakeOver: true` and start with this PC's own store (or none).
  - Window: the button `"Guardar as pastas das turmas neste computador"` in remote mode (disabled while a lesson is on, with a tooltip saying why); a confirmation naming the home PC and the number of classes/files; progress and the result line.
- [ ] **Step 1: Write the failing tests** — over loopback with two temp stores: takeover copies every class's files, indexes and lessons map; the old home's store is moved aside and it stands down; the Agents report the new home; a takeover during a lesson is refused; a drop in the middle (host disposed after N chunks) leaves both stores and the Agents unchanged and no staging folder; a hostile host (path escape, size mismatch) is refused; unreachable-home takeover claims with `TakeOver` and keeps the PC's own store.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole Master test project.**
- [ ] **Step 5: Commit** — `git commit -m "Let a teacher PC take the class folders over"`

---

### Task 7: Wiring, firewall rule, and the two-PC check

**Files:**
- Modify: `src/Midview.Master/MainWindow.ClassFolder.cs` (own one `ClassHomeTracker`, one `ClassFolderHost` (opened when this PC is home or no home exists; closed in remote mode), one controller chosen by `Mode`; `ThumbnailPoller`'s `classLesson` returns the lesson only in local mode; the toolbar caption shows the home's lesson in remote mode too; `StandDown` when the tracker reports another home), `installer/Midview.iss` (rule `MidviewProfessorPastaDaTurma`, TCP `28903`, same restrictions as the invite rules, created and deleted together with them), `docs/superpowers/specs/2026-10-20-class-folder-design.md` (a short "Revision (as built)" listing this plan's decisions)
- Test: `tests/Midview.Master.Tests/` (tracker/host wiring as far as testable without the window)

- [ ] **Step 1: Write the failing tests** for the pure wiring helpers you extract (e.g. which controller and whether to open the host for each `Mode`/home combination; the caption text in each mode).
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement the wiring and the installer rule** (follow `CriarRegrasDoConvite` / `ApagarRegrasDoConvite`; the Master never opens the firewall itself).
- [ ] **Step 4: Run all tests** — `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test Midview.sln`; only the three known warnings.
- [ ] **Step 5: Two-PC harness check** — with the harness from memory `master-ui-check-harness` (copy under the scratchpad; never instantiate `App`; temp `MasterData.Folder` and Turmas root): the home `MainWindow` with its host on a loopback port, plus a second `ClassFolderWindow` built with a `RemoteClassFolderController` (another certificate) pointed at it, against fake students with real `AgentServer`s. Screenshots: the remote window (banner, list, lesson running), the home window updating after the remote starts a lesson, the closed view after disposing the home host, the takeover confirmation. Save under `C:\Users\Neto\AppData\Roaming\Claude\side-session-notes\local_c1b56ff1-e97e-4802-bc8f-f2b1d9768915\class-folder-shots\part2\`. Delete temp roots; leave no process running.
- [ ] **Step 6: Commit** — `git commit -m "Wire the second teacher PC's Pasta da turma and open its firewall port in the setup"`

---

## Self-review notes

- Spec coverage: home recorded on the Agents ✓ (T2); first start becomes home ✓ (T2/T3); other PC becomes remote on its own ✓ (T3/T7); same window, everything sent to the home ✓ (T4/T5); TLS listener next to 28902, pinned, trust via the room's Agents ✓ (T4); closed home → text + only "Encerrar a aula", set-aside picked up later ✓ (T5 + T3's adoption); takeover with copy, Agents told, old copy moved aside, unreachable variant ✓ (T6); Agents refuse a start from a non-home ✓ (T2); firewall rule in the setup, remote says so when missing ✓ (T5 hint, T7); every other Midview function unchanged on the support PC ✓ (nothing else touched); two Masters over loopback tests ✓ (T3–T6).
- Single teacher PC: no tracker answer or home == this PC → local mode, same window, host open but unused; no new prompt.
