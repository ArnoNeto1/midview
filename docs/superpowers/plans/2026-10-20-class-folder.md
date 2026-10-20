# Class Folder ("Pasta da turma") Implementation Plan — part 1: one teacher PC

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The teacher picks the class at the start of a lesson; every lab PC gets a protected "Pasta da turma – <turma>" Desktop folder where Chrome saves downloads, and the teacher's Master keeps that folder the same on every PC, with the copy that counts on the teacher's PC.

**Architecture:** The Master is the hub: a store per class under `Documents\Midview\Turmas` plus a JSON index, and a sync engine that lists each online PC's folder, decides per file (pure planner) and pulls/pushes over new one-shot Session messages. The Agent (SYSTEM) only keeps the lesson state, protects the Desktop folder, writes the per-student Chrome/Edge `DownloadDirectory` policy and closes Chrome; the Session (as the student) does all file I/O and sets files aside when a lesson ends without the Master.

**Tech Stack:** .NET 8, C#, WPF (Master), WinForms (Session), Windows service (Agent), xUnit.

**Spec:** `docs/superpowers/specs/2026-10-20-class-folder-design.md`

**Out of this plan (part 2, a later plan):** "Two teacher PCs in one lab" — the home PC, the remote-control listener, refusing starts from a non-home PC, handing the folders over, the firewall rule. Part 1 must work alone with a single teacher PC.

## Global Constraints

- Build and test only with `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe` (the `dotnet` on PATH has no SDK). The solution has three pre-existing `xUnit1031` warnings; no new warnings.
- Never name another classroom-control product in code, comments, docs or commits.
- No test touches real lab PCs, the real Chrome/Edge policy keys, a real browser, or the teacher's real `%LOCALAPPDATA%\Midview` (use `MasterData.Folder` set to a temp folder).
- Every student-facing and teacher-facing text is Brazilian Portuguese; code, comments and commit messages are English, in the existing style (plain imperative subject, e.g. "Add the class folder design").
- Folder name on the student's Desktop: `"Pasta da turma – " + turma` (U+2013 EN DASH, spaces around it).
- Lesson-start notice, exact: `"Aula do {turma}" + Environment.NewLine + "O que você baixar ou salvar na Pasta da turma – {turma} (na Área de Trabalho) aparece em todos os computadores da sala."`
- Chrome-closed notice: reuse `LabConfigCommandHandler`'s `ChromeClosedNotice` text (make it `internal const` and share it).
- New message numbers start at 65; no new Session message may be one a control connection opens with (`StartControl`, `Heartbeat`, `ScreenStream`).
- Old Agents answer `CommandAck [0]` → shown as "Midview antigo"; old Sessions answer `FrameTile` → `CommandClient.OlderSession`. Neither may break the lesson for other PCs.
- Per-file limit stays `FileTransfer.MaxFileSize` (1 GiB); path depth `FileCollection.MaxDepth` (8); every received path goes through `FileCollection.SafeRelativePath` and an inside-the-folder check on both sides.
- Transfers at most 4 PCs at a time; listings every 5 s per online PC.
- Disk warning on the teacher's PC under 5 GB free (`ClassStore.LowSpaceBytes = 5L * 1024 * 1024 * 1024`).
- Commit after each task; never push (the user approves pushes).

## Review Focus

1. **A class name Windows can't use or two names differing only in case** ("3º A" / "3º a", "Turma: 1/2"): `ClassStore.Create` makes it safe with `FileNames.Safe` and refuses a case-insensitive duplicate (test in Task 7).
2. **A file still being written** (Chrome's `.crdownload`, Word's `~$` lock, a half-saved document): never pulled; only files unchanged across two listings are (tests in Tasks 2 and 10).
3. **A file held open on the PC** (Word) when a push wants to replace it or a pull reads it: that file fails, is retried next round, the rest of the round goes on (tests in Tasks 2 and 10).
4. **The lesson changes while a transfer is in flight** (push for lesson A arrives after the PC moved to lesson B): the Session refuses a push or pull whose lesson id is neither current nor set aside (test in Task 4).
5. **A student with no Desktop folder found / a Desktop redirected elsewhere**: the Agent logs and skips; the Session answers the listing with "sem pasta" instead of throwing; the Master shows ✗ with that reason (tests in Tasks 3 and 6).

---

### Task 1: Protocol — lesson state, wire formats, message numbers

**Files:**
- Create: `src/Midview.Protocol/ClassLesson.cs`, `src/Midview.Protocol/ClassFolderWire.cs`, `src/Midview.Protocol/ClassLessonFile.cs`
- Modify: `src/Midview.Protocol/MessageType.cs` (append after `InviteRefused = 64`)
- Test: `tests/Midview.Protocol.Tests/ClassLessonTests.cs`, `tests/Midview.Protocol.Tests/ClassFolderWireTests.cs`

**Interfaces:**
- Produces:
  - `MessageType`: `SetClassLesson = 65`, `GetClassLesson = 66`, `ClassLessonStatus = 67` (Agent); `ListClassFolder = 68`, `ClassFolderListing = 69`, `PullClassFile = 70`, `PushClassFile = 71`, `DeleteClassFile = 72`, `RenameClassFile = 73`, `ClearSetAside = 74` (Session). Each with a comment in the file's style saying who answers what.
  - `public sealed record ClassLesson(long SavedAt, Guid LessonId, string Turma, long StartedAt, bool Ended)` with `const byte FormatVersion = 1`, `const int MaxTurmaLength = 60`, `static ClassLesson None(long savedAt)` (Ended, `Guid.Empty`, `""`, 0), `bool IsOn => !Ended && LessonId != Guid.Empty`, `string FolderName => "Pasta da turma – " + Turma`, `bool StartedBefore(DateTime localToday)` (start's local date < `localToday.Date`), `byte[] Encode()`, `static bool TryDecode(byte[] payload, out ClassLesson lesson)`.
    Wire: version(1) · SavedAt(int64 BE) · StartedAt(int64 BE, Unix s) · Ended(1 byte, 0/1) · LessonId(16 bytes, `Guid.ToByteArray()`) · Turma(uint16 BE byte length + UTF-8, strict decoding).
  - `public sealed record ClassFolderEntry(Guid LessonId, string Path, long Size, long LastWriteTicks)` — `LessonId` equal to the listing's current lesson means "in the Desktop folder", any other means "set aside for that lesson"; `Path` is '/'-separated relative.
  - `public static class ClassFolderWire` (all `TryDecode*` reject trailing bytes, bad versions, invalid UTF-8, negative sizes, sizes over `FileTransfer.MaxFileSize`, more than `MaxEntries`):
    - `const int MaxEntries = 10_000`
    - `byte[] EncodeListing(Guid currentLessonId, string? problem, IReadOnlyList<ClassFolderEntry> entries)` / `bool TryDecodeListing(byte[], out Guid currentLessonId, out string? problem, out IReadOnlyList<ClassFolderEntry> entries)` — `problem` non-null means the Session couldn't list (e.g. "sem pasta"); entries are then empty.
    - `byte[] EncodeFileRef(Guid lessonId, string path)` / `TryDecodeFileRef(...)` — for `PullClassFile`, `DeleteClassFile`.
    - `byte[] EncodeRename(Guid lessonId, string path, string newPath)` / `TryDecodeRename(...)`.
    - `byte[] EncodePushHeader(Guid lessonId, string path, long size, long lastWriteTicks)` / `TryDecodePushHeader(...)`.
    - `byte[] EncodeLessonId(Guid lessonId)` / `TryDecodeLessonId(...)` — for `ClearSetAside`.
    Strings everywhere: uint16 BE byte length + UTF-8; a path over `FileTransfer.MaxNameBytes` is rejected.
  - `public static class ClassLessonFile`: `static string DefaultPath` (= `%ProgramData%\Midview\class-lesson.json`), `static ClassLesson? Read(string path)` (null when missing, unreadable or corrupt — never throws), `static void Write(string path, ClassLesson lesson)` (temp file, `Flush(true)`, `File.Move` overwrite, as `LabConfigCommandHandler.Save`). JSON fields: `SavedAt, LessonId, Turma, StartedAt, Ended`. Shared: the Agent writes it, the Session reads it (Users can read `%ProgramData%\Midview`).

- [ ] **Step 1: Write the failing tests**

`ClassLessonTests`: `EncodeThenDecode_RoundTrips` (a lesson with `Turma = "3º A"`); `None_IsNotOn`; `FolderName_UsesTheEnDash` (`Assert.Equal("Pasta da turma – 3º A", lesson.FolderName)`); `StartedBefore_IsTrueOnlyForAnEarlierLocalDay`; `TryDecode_RejectsUnknownVersion_TrailingBytes_InvalidUtf8_LongTurma` (Theory); `ClassLessonFile_WriteThenRead_RoundTrips`; `ClassLessonFile_CorruptFile_ReadsNull`.
`ClassFolderWireTests`: round-trip each Encode/TryDecode pair (with a path `"fotos/viagem/mapa.png"` and the `problem` both null and `"sem pasta"`); `TryDecodeListing_RejectsMoreThanMaxEntries`; `TryDecodePushHeader_RejectsSizeOverMaxFileSize`; `TryDecodeFileRef_RejectsTruncatedPayload`.

- [ ] **Step 2: Run them and see them fail**

Run: `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Protocol.Tests --filter "FullyQualifiedName~ClassLesson|FullyQualifiedName~ClassFolderWire"`
Expected: build fails (types missing).

- [ ] **Step 3: Implement `ClassLesson`, `ClassFolderWire`, `ClassLessonFile` and the `MessageType` values above**

Follow `LabConfig`'s Encode/TryDecode style (BinaryPrimitives, big-endian, validate everything).

- [ ] **Step 4: Run the tests and see them pass**

Same command. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Midview.Protocol/ClassLesson.cs src/Midview.Protocol/ClassFolderWire.cs src/Midview.Protocol/ClassLessonFile.cs src/Midview.Protocol/MessageType.cs tests/Midview.Protocol.Tests/ClassLessonTests.cs tests/Midview.Protocol.Tests/ClassFolderWireTests.cs
git commit -m "Add the class lesson state and the class folder messages"
```

---

### Task 2: Session — the class folder's file operations

**Files:**
- Create: `src/Midview.Session/ClassFolder.cs`
- Test: `tests/Midview.Session.Tests/ClassFolderTests.cs`

**Interfaces:**
- Consumes: `ClassLesson`, `ClassFolderEntry` (Task 1); `IncomingFile`-style temp-file write as in `FileInbox`; `IRecycleBin`; `FileCollection.SafeRelativePath`.
- Produces: `public sealed class ClassFolder`
  - `ClassFolder(string desktopDirectory, string setAsideRoot, Func<ClassLesson?> agentLesson, IRecycleBin recycleBin, string sessionStatePath)` — production: Desktop of the student, `%LOCALAPPDATA%\Midview\Pasta da turma guardada`, `() => ClassLessonFile.Read(ClassLessonFile.DefaultPath)`, `WindowsRecycleBin`, `%LOCALAPPDATA%\Midview\class-folder-session.json`.
  - `static readonly string[] SkippedSuffixes = { ".crdownload", ".tmp", ".parcial", ".part" }`; files starting `~$` or `.midview-`, hidden/system files and reparse points (files or folders) are never listed.
  - `(Guid CurrentLessonId, string? Problem, IReadOnlyList<ClassFolderEntry> Entries) List()` — current lesson's Desktop folder (when `agentLesson()` is on) plus every set-aside lesson folder under `setAsideRoot\<lessonId N-format>\`; depth ≤ `FileCollection.MaxDepth`; at most `ClassFolderWire.MaxEntries`; `Problem = "sem pasta"` when the lesson is on but the folder doesn't exist yet or the Desktop is unknown.
  - `string? ResolveExisting(Guid lessonId, string relativePath)` — full path of an existing file inside that lesson's folder (current or set aside), null if the lesson isn't current/set aside, the path escapes, or any segment is a reparse point.
  - `ClassFolderIncoming BeginPush(Guid lessonId, string relativePath)` — only for the current lesson (throws `InvalidOperationException("aula diferente")` otherwise); creates missing subfolders; temp file `.midview-<guid>.parcial` in the target directory.
  - `public sealed class ClassFolderIncoming : IDisposable` with `void Write(ReadOnlySpan<byte>)` and `void Complete(long lastWriteTicks)` (move over the target with overwrite, then `File.SetLastWriteTimeUtc(target, new DateTime(lastWriteTicks, DateTimeKind.Utc))`; an `IOException` from a target held open propagates, temp file deleted).
  - `(bool Ok, string Detail) Delete(Guid lessonId, string relativePath)` — to the recycle bin.
  - `(bool Ok, string Detail) Rename(Guid lessonId, string relativePath, string newRelativePath)` — fails if the new name exists.
  - `(bool Ok, string Detail) ClearSetAside(Guid lessonId)` — recycles that set-aside lesson folder.

- [ ] **Step 1: Write the failing tests** (temp directories; `FakeRecycleBin` recording paths)

`List_ShowsFilesWithSizeAndTicks_AndSubfolders`; `List_SkipsInProgressAndLockAndHiddenFiles` (create `a.crdownload`, `~$doc.docx`, `.midview-x.parcial`, a hidden file → none listed); `List_SkipsAJunctionInsideTheFolder` (`[NotAdministratorFact]` only if creating the junction needs it; use `mklink /J` via `cmd /c`); `List_WithLessonOnButNoFolder_ReportsSemPasta`; `List_IncludesSetAsideLessons_UnderTheirLessonId`; `BeginPush_WritesThroughATempFile_AndKeepsTheTime`; `BeginPush_ForAnotherLesson_Throws`; `BeginPush_WithDotDotPath_StaysInsideTheFolder` (`"../../x.txt"` lands at `<folder>\x.txt`); `Complete_OverAFileHeldOpen_Throws_AndLeavesNoTempFile`; `ResolveExisting_RejectsEscapes`; `Delete_SendsToTheRecycleBin`; `Rename_RefusesAnExistingName`; `ClearSetAside_RecyclesThatLessonOnly`.

- [ ] **Step 2: Run them and see them fail**

Run: `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Session.Tests --filter FullyQualifiedName~ClassFolderTests`
Expected: build fails (`ClassFolder` missing).

- [ ] **Step 3: Implement `ClassFolder` and `ClassFolderIncoming`**

Enumerate with `EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = Hidden | System | ReparsePoint, IgnoreInaccessible = true }` and recurse by hand to cap depth, as `DeliveryFolder.ListFiles` does.

- [ ] **Step 4: Run the tests and see them pass**

Same command. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Midview.Session/ClassFolder.cs tests/Midview.Session.Tests/ClassFolderTests.cs
git commit -m "Let the Session list and write the class folder as the student"
```

---

### Task 3: Session — following the lesson (notice, set-aside, folder removal)

**Files:**
- Modify: `src/Midview.Session/ClassFolder.cs`
- Test: `tests/Midview.Session.Tests/ClassFolderTests.cs`

**Interfaces:**
- Consumes: Task 2's `ClassFolder`; `IMessageDisplay.Show(string)`.
- Produces: `void Follow(IMessageDisplay? notices)` on `ClassFolder` — called every 5 s from `Program.cs` (Task 4). It remembers in `sessionStatePath` the lesson it last followed (`LessonId`, `Turma`). When `agentLesson()` differs from that:
  1. If the remembered lesson had a Desktop folder with files: move them (keeping relative paths) to `setAsideRoot\<old lessonId N>\`, then delete the now-empty folder. A folder the Agent hasn't released yet (access denied) is left and retried on the next call, the remembered lesson unchanged.
  2. If the new lesson is on: show the lesson-start notice (Global Constraints) once.
  3. Remember the new lesson.
  An empty set-aside folder is removed. Never throws (logs to `Console`).

- [ ] **Step 1: Write the failing tests**

`Follow_NewLesson_ShowsTheNoticeOnce` (two calls → one `Show`, text equal to the constant); `Follow_LessonEnded_MovesFilesAsideAndRemovesTheFolder` (`a.png`, `sub/b.txt` end up under `setAside\<id>\`); `Follow_FolderStillProtected_RetriesLater` (make the folder delete fail through an injected `Action<string> deleteDirectory` seam or a read-only attribute; assert the remembered lesson stays the old one); `Follow_NoDesktop_DoesNotThrow` (desktopDirectory pointing at a missing path → no exception, `List()` reports "sem pasta").

- [ ] **Step 2: Run them and see them fail** — same filter as Task 2. Expected: compile error (`Follow` missing).

- [ ] **Step 3: Implement `Follow`** with `ClassLessonFile`-style JSON for `sessionStatePath`.

- [ ] **Step 4: Run the tests and see them pass**

- [ ] **Step 5: Commit**

```bash
git add src/Midview.Session/ClassFolder.cs tests/Midview.Session.Tests/ClassFolderTests.cs
git commit -m "Set a finished lesson's files aside on the student's PC"
```

---

### Task 4: Session — serving the class folder messages

**Files:**
- Modify: `src/Midview.Session/SessionServer.cs` (constructor gains `ClassFolder? classFolder = null` as the last parameter; new first-message branches next to `CollectFiles`), `src/Midview.Session/Program.cs` (create `ClassFolder`, pass it, start a 5 s `System.Threading.Timer` calling `Follow(messageDisplay)`)
- Test: `tests/Midview.Session.Tests/SessionServerTests.cs` (extend `ServerHarness` with `ClassFolder? classFolder = null`)

**Interfaces:**
- Consumes: Task 1 wire formats, Tasks 2–3 `ClassFolder`.
- Produces, on the Session port (5901), each a one-shot connection:
  - `ListClassFolder` (empty payload) → one `ClassFolderListing` (`EncodeListing` of `List()`).
  - `PullClassFile` (FileRef) → `SendFile` header (`FileTransfer.EncodeHeader(path, size)`) + `FileChunk`s + final `CommandAck`(CommandResult true), or a single `CommandAck`(false, reason) when the file can't be resolved or opened (open with `FileShare.ReadWrite | FileShare.Delete`, as `SendCollectedFiles`).
  - `PushClassFile` (PushHeader) → `CommandAck` accept/refuse (refusal: not current lesson → "aula diferente"; not enough space → reuse the `FileInbox.RefusalFor` wording) → `FileChunk`s → `CommandAck` saved or why not (a target held open → "arquivo em uso").
  - `DeleteClassFile`, `RenameClassFile`, `ClearSetAside` → one `CommandAck` with a CommandResult.
  - With `classFolder` null, each answers `CommandAck`(false, "esta máquina não tem pasta da turma").

- [ ] **Step 1: Write the failing tests** (loopback, through `ServerHarness.ConnectMaster()`)

`ListClassFolder_ReturnsTheListing`; `PullClassFile_StreamsTheFile`; `PushClassFile_SavesWithItsTime`; `PushClassFile_ForAnotherLesson_IsRefused` (Review Focus 4); `PushClassFile_OverAFileHeldOpen_ReportsArquivoEmUso`; `DeleteRenameClear_AnswerWithAResult`; `WithoutClassFolder_AnswersEachWithARefusal`.

- [ ] **Step 2: Run them and see them fail**

Run: `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Session.Tests --filter "FullyQualifiedName~SessionServerTests&FullyQualifiedName~Class"`
Expected: compile error / failures.

- [ ] **Step 3: Implement the branches and the `Program.cs` wiring**

- [ ] **Step 4: Run the whole Session test project**

Run: `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Session.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Midview.Session/SessionServer.cs src/Midview.Session/Program.cs tests/Midview.Session.Tests/SessionServerTests.cs
git commit -m "Serve the class folder to the teacher's Master"
```

---

### Task 5: Agent — the download-folder policy and a third protected folder

**Files:**
- Create: `src/Midview.Agent/DownloadFolderPolicy.cs`
- Modify: `src/Midview.Agent/ProtectedStudentFolders.cs` (`Protect`, `NeedsProtecting`, `Release` take an optional `IReadOnlyList<string>? names = null`, default `StudentFolderNames.All`; `Release` with explicit names leaves the Desktop's Deny in place)
- Test: `tests/Midview.Agent.Tests/DownloadFolderPolicyTests.cs`, `tests/Midview.Agent.Tests/ProtectedStudentFoldersTests.cs`

**Interfaces:**
- Produces: `public static class DownloadFolderPolicy`
  - `const string ChromeKey = @"Policies\Google\Chrome"`, `const string EdgeKey = @"Policies\Microsoft\Edge"`, value name `"DownloadDirectory"` (REG_SZ).
  - `static void Apply(RegistryKey userSoftware, string folder)` — writes both.
  - `static void Remove(RegistryKey userSoftware)` — deletes only the `DownloadDirectory` values.
  - `static string? Current(RegistryKey userSoftware)` — Chrome's value or null.
  - `static RegistryKey? OpenUserSoftware(SecurityIdentifier student)` — `Registry.Users.OpenSubKey($@"{student.Value}\Software", writable: true)`; null when the hive isn't loaded.
- Existing callers of `ProtectedStudentFolders` keep compiling unchanged.

- [ ] **Step 1: Write the failing tests**

`DownloadFolderPolicyTests` against `Registry.CurrentUser.CreateSubKey($@"Software\MidviewTest\{guid}")` (deleted in `Dispose`): `Apply_WritesChromeAndEdge`; `Remove_DeletesOnlyDownloadDirectory` (another value under the Chrome key survives); `Current_IsNullWhenAbsent`.
`ProtectedStudentFoldersTests`: `Protect_WithExtraName_CreatesAndProtectsIt` (`[NotAdministratorFact]`, like the existing protection tests); `Release_WithOneName_KeepsTheDesktopDeny`.

- [ ] **Step 2: Run them and see them fail**

Run: `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Agent.Tests --filter "FullyQualifiedName~DownloadFolderPolicy|FullyQualifiedName~ProtectedStudentFolders"`

- [ ] **Step 3: Implement**

- [ ] **Step 4: Run the tests and see them pass** (the whole `ProtectedStudentFoldersTests` class too)

- [ ] **Step 5: Commit**

```bash
git add src/Midview.Agent/DownloadFolderPolicy.cs src/Midview.Agent/ProtectedStudentFolders.cs tests/Midview.Agent.Tests/DownloadFolderPolicyTests.cs tests/Midview.Agent.Tests/ProtectedStudentFoldersTests.cs
git commit -m "Let the Agent point downloads at a protected Desktop folder"
```

---

### Task 6: Agent — the lesson handler

**Files:**
- Create: `src/Midview.Agent/ClassLessonHandler.cs`
- Modify: `src/Midview.Agent/AgentServer.cs` (constructor gains `ClassLessonHandler? classLesson = null` last; `SetClassLesson` → `CommandAck` CommandResult; `GetClassLesson` → `ClassLessonStatus`, only when the handler exists, so an old-shaped Agent still answers `CommandAck`), `src/Midview.Agent/MidviewAgentWorker.cs` (call `Reconcile()` each poll after `_folderGuard.Check()`), `src/Midview.Agent/Program.cs` (register the handler; `ReconcileAtStartup` like `LabConfigCommandHandler` in `AgentServerWorker`), `src/Midview.Agent/UninstallCleanup.cs` (release the class folder and remove the policy for every profile), `src/Midview.Agent/LabConfigCommandHandler.cs` (`ChromeClosedNotice` → `internal const`)
- Test: `tests/Midview.Agent.Tests/ClassLessonHandlerTests.cs`, `tests/Midview.Agent.Tests/AgentServerTests.cs`

**Interfaces:**
- Consumes: Task 1 (`ClassLesson`, `ClassLessonFile`), Task 5 (`DownloadFolderPolicy`, `ProtectedStudentFolders` with names), `AppBlockEnforcer.EndNow(string, string?)`.
- Produces: `public sealed class ClassLessonHandler`
  - `ClassLessonHandler(string statePath, Func<(string Desktop, SecurityIdentifier Student)?> loggedOnStudent, Func<SecurityIdentifier, RegistryKey?> openUserSoftware, AppBlockEnforcer enforcer, SecurityIdentifier folderOwner, Func<DateTime> localNow)` — production: `ClassLessonFile.DefaultPath`, `UserSessionProcessLauncher.LoggedOnStudent`, `DownloadFolderPolicy.OpenUserSoftware`, the shared enforcer, LocalSystem SID, `() => DateTime.Now`.
  - `ClassLesson Current { get; }`
  - `(bool Succeeded, string Detail) Handle(byte[] payload)` — decode; newest `SavedAt` wins (older → `(true, "mantida a aula mais nova: …")`); save with `ClassLessonFile.Write`; then `Reconcile()`; then `enforcer.EndNow("chrome.exe", ChromeClosedNotice)` and `EndNow("msedge.exe", null)` whenever the folder changed. Detail: `"aula do {turma}"` or `"aula encerrada"`.
  - `void Reconcile()` — never throws; idempotent: (a) a lesson on that `StartedBefore(localNow())` becomes `Ended` (saved, same `SavedAt` + 1); (b) with a logged-on student: lesson on → protect `FolderName` (names = `[FolderName]`) and `DownloadFolderPolicy.Apply` with `Path.Combine(desktop, FolderName)` if `Current(...)` differs, closing Chrome when it did; lesson off → `Remove` the policy if present and `Release` every Desktop folder whose name starts with `"Pasta da turma – "`; (c) logs each new outcome once (as `StudentFolderGuard.Report`).
  - `void ReconcileAtStartup()` = `Reconcile()` without closing Chrome.

- [ ] **Step 1: Write the failing tests** (temp Desktop, HKCU test subtree as the user's Software key, `FakeInspector` counting ended processes, a mutable clock)

`Handle_StartsALesson_ProtectsTheFolder_WritesThePolicy_ClosesChrome`; `Handle_OlderSavedAt_IsKept`; `Handle_End_RemovesThePolicy_AndReleasesTheFolder`; `Handle_ChangeOfClass_MovesThePolicy_AndReleasesTheOldFolder`; `Reconcile_NextDay_EndsTheLessonKeepingFiles` (clock +1 day; state saved `Ended`; nothing deleted); `Reconcile_NoStudentLoggedOn_DoesNothing`; `Reconcile_HiveNotLoaded_LogsAndSkips` (`openUserSoftware` returns null — Review Focus 5); `CorruptStateFile_StartsWithNoLesson`.
`AgentServerTests`: `SetThenGetClassLesson_RoundTrips`; `WithoutHandler_GetClassLesson_AnswersCommandAck` (the old-Agent path the Master relies on).

- [ ] **Step 2: Run them and see them fail**

Run: `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Agent.Tests --filter "FullyQualifiedName~ClassLesson|FullyQualifiedName~AgentServerTests"`

- [ ] **Step 3: Implement the handler and the wiring**

- [ ] **Step 4: Run the whole Agent test project**

Run: `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Agent.Tests`
Expected: all pass (administrator-only tests skip as before).

- [ ] **Step 5: Commit**

```bash
git add src/Midview.Agent tests/Midview.Agent.Tests
git commit -m "Keep the class lesson on each student PC"
```

---

### Task 7: Master — the class store and its index

**Files:**
- Create: `src/Midview.Master/ClassStore.cs`
- Test: `tests/Midview.Master.Tests/ClassStoreTests.cs`

**Interfaces:**
- Consumes: `FileNames.Safe`, `IRecycleBin`, `MasterData.Folder`.
- Produces:
  - `public readonly record struct FileVersion(long Size, long LastWriteTicks)`
  - `public sealed record ClassSummary(string Turma, int Files, long Bytes, DateOnly? LastLesson)`
  - `public sealed class ClassStore`
    - `ClassStore(string filesRoot, string indexRoot, IRecycleBin recycleBin, Func<DateTime> localNow)` — production: `Documents\Midview\Turmas`, `MasterData.Folder\turmas`.
    - `const long LowSpaceBytes = 5L * 1024 * 1024 * 1024`; `long FreeBytes()`.
    - `IReadOnlyList<ClassSummary> List()`; `(bool Ok, string Detail, string Turma) Create(string requestedName)` (safe name; refuses empty, longer than `ClassLesson.MaxTurmaLength`, or an existing name ignoring case — Review Focus 1); `(bool Ok, string Detail) Rename(string turma, string newName)`; `(bool Ok, string Detail) Delete(string turma)` (folder and index to the recycle bin / deleted).
    - `IReadOnlyDictionary<string, FileVersion> Files(string turma)` — scans the folder (skips hidden/system/reparse points), keys '/'-separated, case-insensitive.
    - Index per class (`indexRoot\<turma>.json`): `ArrivedOn` per path, `Deleted` paths (tombstones), and `Machines[sessionThumbprint][path] = FileVersion` last synced with that PC. Methods: `DateOnly? ArrivedOn(string turma, string path)`, `bool IsKnown(string turma, string path)`, `FileVersion? LastSynced(string turma, string machineKey, string path)`, `void RecordSynced(string turma, string machineKey, string path, FileVersion version)`, `void ForgetMachine(string turma, string machineKey)`.
    - `string PathOf(string turma, string relativePath)` (inside check; throws on escape).
    - `FileStream BeginIncoming(string turma)` → temp file in the class folder; `string CommitIncoming(string turma, string tempPath, string relativePath, FileVersion version, bool replace)` — replace: old copy to the recycle bin first; new name: `FileNames.MoveToUniqueName`-style free name in the same subfolder, returned; sets the time; records `ArrivedOn = today` for a new path.
    - `void RefreshTombstones(string turma)` — paths known in the index but missing on disk become `Deleted` (the teacher deleted them in Explorer).
    - `(int Recycled, IReadOnlyList<string> Problems) EndLesson(string turma, EndChoice choice)` with `public enum EndChoice { Keep, DeleteToday, DeleteAll }` — `DeleteToday` by `ArrivedOn == today`; clears tombstones and per-PC records in every case.
  - Corrupt index → moved to `.corrupt`, starts empty (files on disk are still listed and get `ArrivedOn` = today when first indexed).

- [ ] **Step 1: Write the failing tests** (temp folders, `FakeRecycleBin`, fixed clock)

`Create_MakesTheNameSafe_AndRefusesACaseOnlyDuplicate`; `Create_RefusesTooLong`; `Rename_And_Delete`; `Files_SkipsHiddenAndLinks`; `CommitIncoming_New_RecordsArrivedToday`; `CommitIncoming_SameNameNotReplace_GetsAFreeName` (`images.jpg` → `images (2).jpg`); `CommitIncoming_Replace_RecyclesTheOldCopy`; `PathOf_RejectsEscape`; `RefreshTombstones_MarksTeacherDeletions`; `EndLesson_DeleteToday_KeepsOlderFiles`; `EndLesson_DeleteAll_RecyclesEverything`; `EndLesson_Keep_ClearsPerMachineRecords`; `CorruptIndex_IsSetAside`.

- [ ] **Step 2: Run them and see them fail**

Run: `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Master.Tests --filter FullyQualifiedName~ClassStoreTests`

- [ ] **Step 3: Implement `ClassStore`** (index saved atomically after each change, like `KnownMachinesFile`)

- [ ] **Step 4: Run the tests and see them pass**

- [ ] **Step 5: Commit**

```bash
git add src/Midview.Master/ClassStore.cs tests/Midview.Master.Tests/ClassStoreTests.cs
git commit -m "Keep each class's files and index on the teacher's PC"
```

---

### Task 8: Master — the per-file decision

**Files:**
- Create: `src/Midview.Master/ClassSyncPlanner.cs`
- Test: `tests/Midview.Master.Tests/ClassSyncPlannerTests.cs`

**Interfaces:**
- Consumes: `FileVersion` (Task 7).
- Produces:
  - `public abstract record SyncAction(string Path)` with `Pull(string Path, bool Replace)`, `PullAsNew(string Path)` (store under a free name, then rename on the PC to it), `Push(string Path)`, `DeleteOnPc(string Path)`, `RecordInSync(string Path, FileVersion Version)`.
  - `public static IReadOnlyList<SyncAction> ClassSyncPlanner.Plan(IReadOnlyDictionary<string, FileVersion> store, Func<string, bool> isTombstone, Func<string, FileVersion?> lastSynced, IReadOnlyDictionary<string, FileVersion> pcNow, IReadOnlyDictionary<string, FileVersion>? pcBefore)` — all dictionaries case-insensitive; `pcBefore` is the previous listing (null on the first round: nothing is pulled yet, only pushes and in-sync records).
  Rules (spec "Synchronisation" and "Changes and conflicts"), per path:
  - PC file not stable (absent from `pcBefore` or different there) → no pull this round.
  - PC == store → `RecordInSync`.
  - PC ≠ store, `lastSynced` == PC → `Push` (store moved on).
  - PC ≠ store, `lastSynced` exists and ≠ PC → `Pull(Replace: true)` (an edit; the later one wins).
  - PC ≠ store, no `lastSynced` → `PullAsNew` (a different file with the same name).
  - On PC, not in store, `isTombstone` and `lastSynced` == PC → `DeleteOnPc`; otherwise → `Pull(Replace: false)`.
  - In store, not on PC → `Push`.

- [ ] **Step 1: Write the failing tests** — one per rule above, named after it (`InSync_IsRecorded`, `StoreMovedOn_IsPushed`, `PcEdited_IsPulledReplacing`, `SameNameDifferentFile_IsPulledAsNew`, `NewOnPc_IsPulled`, `TeacherDeleted_IsDeletedOnPc`, `TeacherDeletedButPcEdited_IsPulled`, `StudentDeleted_IsPushedBack`, `UnstableFile_IsNotPulled`, `FirstRound_PullsNothing`, `PathsCompareIgnoringCase`).

- [ ] **Step 2: Run them and see them fail**

Run: `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Master.Tests --filter FullyQualifiedName~ClassSyncPlannerTests`

- [ ] **Step 3: Implement `Plan`** (pure; no I/O)

- [ ] **Step 4: Run the tests and see them pass**

- [ ] **Step 5: Commit**

```bash
git add src/Midview.Master/ClassSyncPlanner.cs tests/Midview.Master.Tests/ClassSyncPlannerTests.cs
git commit -m "Decide what each class file needs on each PC"
```

---

### Task 9: Master — talking to the Agent and the Session

**Files:**
- Create: `src/Midview.Master/ClassFolderClient.cs`
- Test: `tests/Midview.Master.Tests/ClassFolderClientTests.cs`

**Interfaces:**
- Consumes: Task 1 wire formats; `CommandClient`, `TcpConnect.WithTimeout`, `SecureChannel`; `ConnectionClient.AgentPort` / `SessionPort`.
- Produces: `public static class ClassFolderClient` (every method takes `KnownMachine machine, X509Certificate2 masterCertificate` and a trailing `int port` defaulting to the Agent or Session port):
  - `(bool Succeeded, string Detail) SetLesson(..., ClassLesson lesson)` — an old Agent's `(false, "")` is returned as `(false, "Midview antigo")`.
  - `sealed record ListResult(Guid CurrentLessonId, string? Problem, IReadOnlyList<ClassFolderEntry> Entries, bool OldSession)`; `ListResult List(...)` — `FrameTile` reply → `OldSession = true`.
  - `(bool Succeeded, string Detail) Pull(..., Guid lessonId, string path, Stream destination)` — checks the streamed size equals the header and never exceeds `FileTransfer.MaxFileSize`.
  - `(bool Succeeded, string Detail) Push(..., Guid lessonId, string path, string sourceFile, long lastWriteTicks)` — 30 s per read/write like `FileSender`.
  - `Delete(...)`, `Rename(...)`, `ClearSetAside(...)` → `(bool Succeeded, string Detail)`.

- [ ] **Step 1: Write the failing tests** — loopback against a real `SessionServer` with a temp `ClassFolder` (pattern of `FileCollectorTests`) and a real `AgentServer` with a `ClassLessonHandler` (pattern of `LabConfigClientTests.StartAgent`): `SetLesson_ThenAgentHasIt`; `SetLesson_OldAgent_SaysMidviewAntigo` (AgentServer built without the handler); `List_Pull_Push_Delete_Rename_ClearSetAside_RoundTrip`; `List_OldSession_IsFlagged` (a scripted fake Session answering `FrameTile`); `Pull_LongerThanHeader_IsRejected` (hostile scripted Session).

- [ ] **Step 2: Run them and see them fail**

Run: `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Master.Tests --filter FullyQualifiedName~ClassFolderClientTests`

- [ ] **Step 3: Implement `ClassFolderClient`**

- [ ] **Step 4: Run the tests and see them pass**

- [ ] **Step 5: Commit**

```bash
git add src/Midview.Master/ClassFolderClient.cs tests/Midview.Master.Tests/ClassFolderClientTests.cs
git commit -m "Add the Master's class folder client"
```

---

### Task 10: Master — the lesson and the sync engine

**Files:**
- Create: `src/Midview.Master/ClassLessonFileMaster.cs` (the Master's saved lesson), `src/Midview.Master/ClassFolderSync.cs`
- Modify: `src/Midview.Master/ThumbnailPoller.cs` (optional `Func<ClassLesson?>? classLesson = null`; `SendClassLessonIfChanged()` exactly like `SendAppBlockIfChanged`, by `SavedAt`)
- Test: `tests/Midview.Master.Tests/ClassFolderSyncTests.cs`, `tests/Midview.Master.Tests/ThumbnailPollerTests.cs`

**Interfaces:**
- Consumes: Tasks 7–9.
- Produces:
  - `ClassLessonFileMaster`: `static string Path => System.IO.Path.Combine(MasterData.Folder, "class-lesson.json")`, `Read()`/`Write(ClassLesson)` reusing `ClassLessonFile.Read/Write`.
  - `public enum PcSyncState { InSync, Copying, Offline, Failed }`; `public sealed record PcStatus(KnownMachine Machine, PcSyncState State, string Detail)`.
  - `public sealed class ClassFolderSync : IDisposable`
    - `ClassFolderSync(ClassStore store, X509Certificate2 masterCertificate, Func<IReadOnlyList<KnownMachine>> machines, Func<KnownMachine, bool> isOnline, Func<DateTime> localNow, int agentPort = ConnectionClient.AgentPort, int sessionPort = ConnectionClient.SessionPort, TimeSpan? interval = null)` — interval default 5 s.
    - `ClassLesson Lesson { get; }` (loaded from `ClassLessonFileMaster`; on construction a lesson that `StartedBefore(localNow())` is ended with `EndChoice.Keep` — spec "A lesson nobody ended").
    - `event Action<PcStatus>? StatusChanged`; `event Action<ClassLesson>? LessonChanged`.
    - `Task<IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)>> StartAsync(string turma)` — new `ClassLesson(SavedAt: now Unix s, LessonId: Guid.NewGuid(), turma, StartedAt: now, Ended: false)`; saved; `SetLesson` to every machine at once (`Task.WhenAll`), per-PC results returned.
    - `Task<IReadOnlyList<(KnownMachine, bool, string)>> EndAsync(EndChoice choice)` — one last round with every online PC, `store.EndLesson`, then `ClassLesson.None(now)`-style ended lesson (same `LessonId`, `Ended = true`) sent to every Agent and saved.
    - `Task RunRoundAsync(KnownMachine machine, CancellationToken)` — List → (`OldSession`/`Problem` → `Failed` with detail) → set-aside entries of other lessons, as below; current entries → `ClassSyncPlanner.Plan` against the store with this PC's previous listing → execute actions (each failure only marks that file to retry; Review Focus 3); `RecordSynced` after each success; `RenameOnPc` after `PullAsNew`. Status `Copying` while transfers run, `InSync` when the plan was only `RecordInSync`.
    - Background loop while `Lesson.IsOn`: every interval, for each online machine, at most 4 transfer rounds at once (`SemaphoreSlim(4)`).
  - Lessons known to the store (add to `ClassStore`, stored in `indexRoot\lessons.json`): `void RememberLesson(Guid lessonId, string turma)`, `string? TurmaOfLesson(Guid lessonId)`, `void MarkCollected(Guid lessonId)`, `bool IsCollected(Guid lessonId)`. `EndAsync` marks the lesson collected after its last round; the next-day auto-end does not.
  - Set-aside entries (a PC's files from a lesson that is no longer current — the Session always sets them aside, Task 3): for a collected lesson the Master only sends `ClearSetAside` (they are already in the store, or the teacher chose to delete them — spec: a PC offline at the end loses its leftovers to its own Recycle Bin, never to the class); for a lesson that isn't collected (ended by the day changing, or unknown to this Master) it pulls each file into `TurmaOfLesson` as `Pull(Replace: false)` and then sends `ClearSetAside`. A lesson whose class is unknown or deleted is cleared without pulling. Set-aside handling runs in every round of any later lesson.

- [ ] **Step 1: Write the failing tests** (three loopback Sessions with temp `ClassFolder`s whose `agentLesson` returns the test's lesson; real `ClassStore` in temp folders; `isOnline = _ => true`)

`AFileSavedOnOnePc_ReachesTheOthers` (write `mapa.png` in PC1's folder, run two rounds over all, assert it exists with the same bytes and time on PC2 and PC3 and in the store); `ADownloadInProgress_IsNotCopied` (a `.crdownload` file never appears elsewhere; Review Focus 2); `TwoPcsSameNameDifferentFiles_BothKept` (`images.jpg` on PC1 and PC2 → store has `images.jpg` and `images (2).jpg`, every PC has both); `AnEdit_ReplacesEverywhere`; `StudentDeletes_FileComesBack`; `TeacherDeletesInTheStore_FileGoesFromThePcs`; `AFileHeldOpenOnAPc_IsRetried_OthersGoOn`; `EndAsync_DeleteToday_And_Keep`; `SetAsideFiles_OfAnUncollectedLesson_ArePulledIntoTheirClass_ThenCleared`; `SetAsideFiles_OfACollectedLesson_AreOnlyCleared` (after `EndAsync(DeleteAll)`, a PC that was offline comes back with files: nothing returns to the class); `ALessonFromYesterday_IsEndedKeepingFilesOnStart`; `AnOldSession_IsFailedWithMidviewAntigo`.
`ThumbnailPollerTests`: `ResendsTheClassLesson_WhenTheAgentComesBack` (pattern of the existing app-block resend test with `FakeAgent`).

- [ ] **Step 2: Run them and see them fail**

Run: `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Master.Tests --filter "FullyQualifiedName~ClassFolderSyncTests|FullyQualifiedName~ThumbnailPollerTests"`

- [ ] **Step 3: Implement `ClassLessonFileMaster`, `ClassFolderSync`, the `ClassStore` lesson map and the poller resend**

- [ ] **Step 4: Run the whole Master test project**

Run: `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Master.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Midview.Master tests/Midview.Master.Tests
git commit -m "Keep the class folder the same on every PC through the teacher's Master"
```

---

### Task 11: Master — the "Pasta da turma" button and window

**Files:**
- Create: `src/Midview.Master/ClassFolderWindow.xaml`, `src/Midview.Master/ClassFolderWindow.xaml.cs`, `src/Midview.Master/ClassFolderText.cs`
- Modify: `src/Midview.Master/ToolbarButton.cs` and its template in `src/Midview.Master/App.xaml` (next to `BadgeDisc`): a `Caption` string dependency property shown as a small pill under the label when non-empty, collapsed when empty (as the `Badge` trigger does), `src/Midview.Master/MainWindow.xaml` (new `ToolbarButton x:Name="ClassFolderButton" Content="Pasta da turma"` after "Recolher arquivos", `OpensOptions="True"`, ToolTip "Escolhe a turma da aula: o que os alunos baixarem ou salvarem na pasta da turma aparece em todos os computadores."), `src/Midview.Master/MainWindow.xaml.cs` (own one `ClassStore` + `ClassFolderSync` for the window's life; `ClassFolderButton.Caption` follows `LessonChanged`; `OnClassFolderClicked` → `ShowOnce("pasta-da-turma", Array.Empty<KnownMachine>(), ...)`; pass `classLesson: () => sync.Lesson` to each `ThumbnailPoller`; `isOnline` from the tile's `Presence == TilePresence.Online`)
- Test: `tests/Midview.Master.Tests/ClassFolderTextTests.cs`

**Interfaces:**
- Consumes: Tasks 7 and 10.
- Produces: `public static class ClassFolderText` — the window's texts as pure functions, tested:
  - `string Summary(ClassSummary s)` → `"38 arquivos · última aula 14/10"`, `"1 arquivo · última aula 14/10"`, `"vazia"`.
  - `string Header(ClassLesson lesson, int files, long bytes, long freeBytes)` → `"Aula começou às 13:30 · 214 arquivos (1,2 GB) · disco deste PC com 180 GB livres"` (pt-BR number format, "GB"/"MB").
  - `string? LowSpaceWarning(long freeBytes)` → non-null under `ClassStore.LowSpaceBytes`: `"O disco deste computador está ficando cheio ({x} livres). Apague turmas antigas para continuar guardando arquivos."`
  - `string State(PcStatus s)` → `"✓ LAB-01"`, `"⟳ LAB-03"`, `"○ LAB-06"`, `"✗ LAB-09: Midview antigo"`.
  - `string EndChoiceHint(EndChoice c, string turma, int todayFiles, int allFiles)` → `"Voltam para todos os PCs na próxima aula do 3º A."`, `"Os 23 arquivos desta aula saem; os das aulas anteriores ficam."`, `"Os 214 arquivos saem do seu PC e de todos os PCs. Vão para a Lixeira deste PC."`
- Window layout per the approved sketch (spec "What the teacher sees"): before a lesson — intro text, class list with `Summary`, "+ Nova turma" (asks the name in a small input row), "Renomear" and "Excluir" (confirmation naming how many files go; both disabled for the class whose lesson is on), "Fechar", "Começar a aula do <turma>"; during — `Header`, `LowSpaceWarning` when present, the PCs as a wrapping list of `State`, the three radio choices with `EndChoiceHint` (Keep checked), "Trocar de turma…" and "Encerrar a aula". Results of start/end per PC appear as `"✓/✗ Nome: detalhe"` lines like the other windows.

- [ ] **Step 1: Write the failing tests** for every `ClassFolderText` function with the exact strings above (Theory rows for singular/plural and sizes).

- [ ] **Step 2: Run them and see them fail**

Run: `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Master.Tests --filter FullyQualifiedName~ClassFolderTextTests`

- [ ] **Step 3: Implement `ClassFolderText`, `Caption`, the window and the `MainWindow` wiring**

- [ ] **Step 4: Run all tests**

Run: `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test Midview.sln`
Expected: all pass, only the three known `xUnit1031` warnings.

- [ ] **Step 5: Check the real window against fake students**

Use the harness from memory `master-ui-check-harness` (copy in the side-session notes folder `tilecheck-harness`): `MasterData.Folder` in a temp folder, fake `known-machines.json`, `SessionServer`s on 127.0.0.2–4 each with a temp `ClassFolder` whose `agentLesson` reads a temp `class-lesson.json` the harness writes when the fake Agent receives `SetClassLesson` (or start real `AgentServer`s with `ClassLessonHandler` on a test registry subtree). Screenshot: the button with and without the caption, the window before and during a lesson, a file dropped in one fake student's folder showing up in the others. Never instantiate `App`.

- [ ] **Step 6: Commit**

```bash
git add src/Midview.Master tests/Midview.Master.Tests
git commit -m "Add the Pasta da turma button and window"
```
