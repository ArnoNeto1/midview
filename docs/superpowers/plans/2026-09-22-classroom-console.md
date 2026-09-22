# Midview Classroom Console Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `MidviewMaster`'s single-machine manual-entry window with a classroom console: a persisted list of the teacher's machines, shown as a grid of thumbnails refreshed every few seconds, where clicking one opens today's `ViewControlWindow` full-control experience unchanged.

**Architecture:** `MidviewSession` gets a persisted identity (created by the Agent, which runs as SYSTEM, since Session runs as the student) and a second, lightweight way to be talked to. A connection that sends `RequestThumbnail` gets back one downscaled JPEG (`ThumbnailFrame`). It never gets the ~15 fps tile stream, and never touches the input lock. `SessionServer` now serves each connection on its own thread, so a preview connection and a full-control connection to the same machine can run side by side. `MidviewMaster` keeps a `KnownMachine` roster in `%LOCALAPPDATA%`, runs one `MachinePreviewPoller` (own thread, own long-lived `PreviewClient`) per saved machine, and shows the results in a new grid `MainWindow`. The old manual-entry window becomes `AddMachineWindow`, which is where a machine is proven to work before it can be saved.

**Tech Stack:** C# / .NET 8 (`net8.0-windows`), WPF (Master), System.Drawing (Session), xUnit. No new third-party dependencies.

**Spec:** `C:\Users\Neto\Desktop\Midview\docs\superpowers\specs\2026-09-22-classroom-console-design.md`. **Task 1 amends it.** Read it after Task 1 lands, not before: the original text has two factual errors about today's code (see "Where this plan departs from the spec" below).

## Global Constraints

- Windows only, LAN only, `net8.0-windows`: same as the earlier sub-projects. No GPL/copyleft code or libraries, and no new NuGet packages.
- **`dotnet` in every command below means `C:\Users\Neto\AppData\Local\Microsoft\dotnet\dotnet.exe`** (SDK 8.0.425). The `dotnet` first on PATH (`C:\Program Files\dotnet`) has no SDK and fails with "No .NET SDKs were found". In PowerShell: `$dotnet = "C:\Users\Neto\AppData\Local\Microsoft\dotnet\dotnet.exe"` then `& $dotnet test ...`. Run everything from the repo root, `C:\Users\Neto\Desktop\Midview`.
- Baseline before Task 1: `dotnet test Midview.sln` passes **65** tests (Protocol 14, Agent 42, Session 9). After Task 8 it passes **80** (Protocol 17, Agent 42, Session 15, Master 6).
- **Full control must behave exactly as today once a connection has sent `StartControl`**: same tile stream, cadence, heartbeat watchdog and input handling (spec non-goal). Task 5 moves that code, and the only thing it changes is *when* it starts.
- The preview path never touches `InputLockManager` or `FramePipeline` (spec).
- Thumbnails fit within **320×180**, keeping the screen's aspect ratio. Poll cadence is **3 s** (the spec suggests 3–5 s).
- Roster file: `%LOCALAPPDATA%\Midview\known-machines.json`, a plain per-user file with no special ACL (spec). Session identity: `%ProgramData%\Midview\session-identity.pfx` (spec).
- New user-visible strings are in Brazilian Portuguese, matching `ViewControlWindow`'s toolbar ("Tela Cheia", "Desconectar"). Strings moved from today's `MainWindow` keep their current (English) text: this plan doesn't translate existing UI.
- `Midview.Session` has `UseWindowsForms` plus `ImplicitUsings`, so `System.Windows.Forms` is imported everywhere in it. There, write `Midview.Protocol.Message` and `System.Threading.Timer` fully qualified, as the existing code already does, or you'll hit ambiguity errors.
- `Midview.Master` is WPF, whose implicit usings **exclude `System.IO`**. Add `using System.IO;` wherever you use `File`, `Path`, `MemoryStream` or `InvalidDataException`.
- Commit messages follow the existing style: an imperative subject, then a body explaining *why*, ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Live testing happens on Aluno-25 (a student machine) plus the teacher's PC, never input-locking the machine you're working from. Same reasoning as the earlier sub-projects.

## Where this plan departs from the spec (read first)

These come from reading the code on `master` (at `11c60b3`), and every one was checked in a throwaway prototype where all 80 tests pass:

1. **`SessionServer` is not concurrent today.** The spec says preview and control connections are "handled by `SessionServer.Run`'s existing accept-loop-per-client model exactly like any other pair of concurrent clients today". In fact `Run` calls `HandleClient` inline, so one client is served at a time. A long-lived preview connection would therefore block full control forever. Worse, every connection starts the ~15 fps capture thread the moment it's accepted, so a preview connection would receive the full tile stream. And every connection's `finally` calls `StopLock()`, so a preview connection closing would unlock input that a separate control connection still holds. Task 5 fixes all three, and its tests fail against today's code for exactly these reasons.
2. **Session can't write to `%ProgramData%\Midview`, and as a standard user can't even read it today.** The Agent launches Session with the logged-in student's own token (`WTSQueryUserToken` then `CreateProcessAsUser`). `AgentDataDirectory.EnsureSecure` gives only SYSTEM and Administrators access, and applies that ACL only when it first creates the directory. The site-blocking spec's review notes say this was "Fixed" (Users read-only, enforced every start, `agent-identity.pfx` protected separately), but that code change never landed. Commit `cd23c00` still lists it as a known follow-up. Task 3 makes that change as the note describes. **The Agent, not Session, creates `session-identity.pfx`**, before it first launches Session, and logs its thumbprint to `agent.log`. Accepted trade-off: any local user can read `session-identity.pfx`, private key included. That's inherent to Session running as the student (anything Session can read, the student can). The worst a student can do with it is impersonate their own machine's Session to the teacher.
3. **New test project `tests/Midview.Master.Tests`.** The spec asks for `KnownMachinesFile` tests, but Master had no test project. With one in place, `MachinePreviewPoller` also gets two cheap loopback tests, although the spec planned live-only validation for the polling. They cover the risky part: decoding the image off the UI thread, and turning every failure into "offline" instead of a crash. The grid UI itself stays live-validated.
4. **Small additions the spec doesn't mention.** Right-click a tile for "Detalhes e bloqueio de sites..." (opens `AddMachineWindow` prefilled with that machine). This is how "site-blocking scoped to the selected machine" is realized. Right-click also offers "Remover da lista", without which the roster is append-only and a typo'd entry stays forever. Saving a host address that's already in the list replaces that entry instead of duplicating it. An unreadable roster file is moved aside to `.corrupt` rather than crashing Master or being silently overwritten. Thumbnails keep their aspect ratio within 320×180. The app exits when the grid window closes (`ShutdownMode="OnMainWindowClose"`).

## File map

| File | Status | Responsibility |
|---|---|---|
| `docs/superpowers/specs/2026-09-22-classroom-console-design.md` | modify | Correct the two factual errors; record the additions |
| `docs/superpowers/specs/2026-09-22-site-blocking-design.md` | modify | Make the data-directory note accurate |
| `src/Midview.Protocol/SessionIdentity.cs` | create | Load-or-create Session's persisted certificate (shared by Agent and Session) |
| `src/Midview.Protocol/MessageType.cs` | modify | `RequestThumbnail = 12`, `ThumbnailFrame = 13` |
| `src/Midview.Agent/AgentDataDirectory.cs` | modify | ACL on every start with Users read-only; `RestrictToAdministrators` |
| `src/Midview.Agent/AgentLog.cs` | modify | Never throws, doesn't reapply the ACL per line |
| `src/Midview.Agent/Program.cs` | modify | Secure the directory and create `session-identity.pfx` before the hosted services start |
| `src/Midview.Agent/AgentServerWorker.cs` | modify | Protect `agent-identity.pfx` |
| `src/Midview.Session/Program.cs` | modify | Load the persisted identity |
| `src/Midview.Session/ThumbnailRenderer.cs` | create | Downscale one frame to fit a box, JPEG-encode it |
| `src/Midview.Session/SessionServer.cs` | modify | Thread per connection; control feed only on `StartControl`; `RequestThumbnail` |
| `src/Midview.Master/KnownMachine.cs` | create | Roster entry record |
| `src/Midview.Master/KnownMachinesFile.cs` | create | Roster JSON load/save |
| `src/Midview.Master/PreviewClient.cs` | create | Long-lived, timeout-bounded thumbnail connection |
| `src/Midview.Master/MachinePreviewPoller.cs` | create | One machine's poll loop on its own thread |
| `src/Midview.Master/MidviewPorts.cs` | create | Session/Agent port constants (used by two windows now) |
| `src/Midview.Master/ConnectionErrors.cs` | create | Shared exception-to-text helper (was private in `MainWindow`) |
| `src/Midview.Master/MachineTileViewModel.cs` | create | One grid tile's bindable state; owns its poller |
| `src/Midview.Master/AddMachineWindow.xaml(.cs)` | **git mv** from `MainWindow.xaml(.cs)`, then modify | Manual Connect, site blocking, and "Salvar na lista da turma" |
| `src/Midview.Master/MainWindow.xaml(.cs)` | create (new content) | The classroom grid |
| `src/Midview.Master/App.xaml` | modify | `ShutdownMode="OnMainWindowClose"` |
| `tests/Midview.Protocol.Tests/SessionIdentityTests.cs` | create | |
| `tests/Midview.Session.Tests/ThumbnailRendererTests.cs` | create | |
| `tests/Midview.Session.Tests/SessionServerThumbnailTests.cs` | create | Loopback tests, including the concurrency cases |
| `tests/Midview.Master.Tests/*` | create | New project: roster file tests, poller tests |

---

### Task 1: Correct the spec before building from it

**Files:**
- Modify: `docs/superpowers/specs/2026-09-22-classroom-console-design.md`
- Modify: `docs/superpowers/specs/2026-09-22-site-blocking-design.md`

**Interfaces:** none (docs only). Every later task argues from the spec as amended here.

- [ ] **Step 1: Replace the second paragraph of "Prerequisite fix: persisted Session identity"** (the paragraph starting "Session now persists its identity the same way", ending "location that already exists by the time it runs.") with:

```markdown
Session now persists its identity at
`%ProgramData%\Midview\session-identity.pfx` (`SessionIdentity.LoadOrCreate`
in `Midview.Protocol`, mirroring `AgentIdentity`/`MasterIdentity`), with a
stable thumbprint for the lifetime of the machine. Session runs as the
logged-in student (the Agent launches it with that user's token), who can
read the Agent's data directory but not write to it — so the Agent,
running as SYSTEM, creates the file at startup, before `SessionSupervisor`
first launches Session, and logs its thumbprint to `agent.log`. Session
only ever loads it (it creates the file itself only when run by hand,
elevated, on a machine without the Agent).

This depends on the data-directory ACL change described in the
site-blocking spec's review notes, which had not actually been applied:
`AgentDataDirectory.EnsureSecure` still set a SYSTEM/Administrators-only
ACL, and only when creating the directory. It now reapplies the ACL on
every Agent start with Users read-only (which also lets Session read
`trusted-masters.json` in a standard student login), and
`agent-identity.pfx` gets its own SYSTEM/Administrators-only ACL.
Accepted trade-off: any local user can read `session-identity.pfx`,
private key included. That's inherent to Session running as the student
(anything Session can read, the student can), and the worst it allows is
a student impersonating their own machine's Session to the teacher.
```

- [ ] **Step 2: In "Error handling", replace the second bullet** (starting "Master's per-machine polling connections must not interfere") with:

```markdown
- Master's per-machine polling connections must not interfere with a
  separately-opened full-control `ViewControlWindow` to the same machine
  — they're independent `SecureChannel` connections to the same
  `SessionServer`. That took three changes to `SessionServer`, which
  used to serve one client at a time (a long-lived preview connection
  would have blocked full control indefinitely): each connection now
  gets its own thread; the ~15 fps capture thread and the heartbeat
  watchdog start on `StartControl` instead of on connect; and only a
  connection that is itself controlling releases the input lock when it
  ends. `RequestThumbnail` is ignored on a connection that has a capture
  thread (that thread is the channel's one writer). Running a preview
  and full control at once means that machine captures a small extra
  low-res frame every few seconds on top of full control's own feed — a
  deliberately accepted minor inefficiency (YAGNI) rather than adding
  coordination logic to pause the preview poll while full control is
  open, unless it proves to actually matter in practice.
```

- [ ] **Step 3: In "Main window", append this paragraph** after the last one (ending "disconnected from the grid."):

```markdown
Right-clicking a tile offers "Detalhes e bloqueio de sites..." (opens the
Add/test view prefilled with that machine, which is what scopes the
site-blocking controls to it) and "Remover da lista". Saving a machine
whose host address is already in the list replaces that entry. An
unreadable `known-machines.json` is moved aside to
`known-machines.json.corrupt` (and reported in the status bar) instead
of crashing Master or being silently overwritten by the next save.
```

- [ ] **Step 4: In "Testing", replace the last bullet** (starting "The grid UI and per-machine polling loops") with:

```markdown
- `KnownMachinesFile` tests live in a new `tests/Midview.Master.Tests`
  project (Master had none). With it in place, `MachinePreviewPoller`
  also gets two loopback tests — a thumbnail from a reachable Session,
  `null` rather than an exception when nothing is listening. The grid UI
  itself (`MainWindow`, `AddMachineWindow`, `MachineTileViewModel`) is
  validated by live testing, consistent with `ViewControlWindow`.
```

- [ ] **Step 5: In `docs/superpowers/specs/2026-09-22-site-blocking-design.md`**, in "Review notes", change the first line of the data-directory bullet from

```markdown
- **Fixed: data-directory permissions.** `AgentDataDirectory.EnsureSecure`
```

to

```markdown
- **Fixed: data-directory permissions** (the code change itself landed
  with the classroom console, after this note was written). `AgentDataDirectory.EnsureSecure`
```

- [ ] **Step 6: Commit**

```bash
git add docs/superpowers/specs/2026-09-22-classroom-console-design.md docs/superpowers/specs/2026-09-22-site-blocking-design.md
git commit -m "Correct the classroom console spec against the actual code" -m "SessionServer serves one client at a time and starts full control's capture thread on every connection, and Session (running as the student) can't write to the Agent's data directory. The spec assumed otherwise on both counts; this records how the plan handles each, plus the small UI additions it makes." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Protocol — `SessionIdentity`

**Files:**
- Create: `src/Midview.Protocol/SessionIdentity.cs`
- Test: `tests/Midview.Protocol.Tests/SessionIdentityTests.cs`

**Interfaces:**
- Consumes: `SelfSignedCertificate.Create(string subjectName) -> X509Certificate2` (existing, `Midview.Protocol`).
- Produces: `SessionIdentity.DefaultCertificatePath` (`string`, `%ProgramData%\Midview\session-identity.pfx`) and `SessionIdentity.LoadOrCreate(string certificatePath) -> X509Certificate2`. Task 3 calls both, from the Agent's `Program.cs` and from Session's `Program.cs`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Midview.Protocol.Tests/SessionIdentityTests.cs
using Midview.Protocol;
using Xunit;

public class SessionIdentityTests
{
    [Fact]
    public void LoadOrCreate_NoExistingFile_CreatesAndPersistsCertificate()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"midview-test-{Guid.NewGuid()}.pfx");
        try
        {
            var certificate = SessionIdentity.LoadOrCreate(tempPath);

            Assert.True(File.Exists(tempPath));
            Assert.NotEmpty(certificate.Thumbprint);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void LoadOrCreate_ExistingFile_ReturnsSameCertificateOnSecondCall()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"midview-test-{Guid.NewGuid()}.pfx");
        try
        {
            var first = SessionIdentity.LoadOrCreate(tempPath);
            var second = SessionIdentity.LoadOrCreate(tempPath);

            Assert.Equal(first.Thumbprint, second.Thumbprint);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void LoadOrCreate_ExistingFile_LoadsThePrivateKey()
    {
        // Session serves TLS with this certificate, which needs the private
        // key -- not just the public certificate -- after a reload.
        var tempPath = Path.Combine(Path.GetTempPath(), $"midview-test-{Guid.NewGuid()}.pfx");
        try
        {
            SessionIdentity.LoadOrCreate(tempPath);
            var reloaded = SessionIdentity.LoadOrCreate(tempPath);

            Assert.True(reloaded.HasPrivateKey);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Midview.Protocol.Tests`
Expected: FAIL to compile, with `error CS0103: The name 'SessionIdentity' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

```csharp
// src/Midview.Protocol/SessionIdentity.cs
using System.Security.Cryptography.X509Certificates;

namespace Midview.Protocol;

// Persisted, so the Session thumbprint a teacher saves in MidviewMaster's
// classroom list stays valid across reboots (a fresh per-launch certificate
// would silently break every saved entry the next time the machine starts).
//
// Lives in the Agent's data directory. The Agent (running as SYSTEM)
// creates it before it first launches Session, because Session runs as the
// logged-in student, who can read that directory but not write to it. Both
// of them load it, which is why it's here in Midview.Protocol.
public static class SessionIdentity
{
    public static string DefaultCertificatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Midview", "session-identity.pfx");

    public static X509Certificate2 LoadOrCreate(string certificatePath)
    {
        if (File.Exists(certificatePath))
        {
            return new X509Certificate2(certificatePath, (string?)null, X509KeyStorageFlags.Exportable);
        }

        var certificate = SelfSignedCertificate.Create(Environment.MachineName);
        var directory = Path.GetDirectoryName(certificatePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx));
        return certificate;
    }
}
```

Why `Exportable` and not `AgentIdentity`'s `MachineKeySet | Exportable`: Session loads this as a standard user in their own profile, the same situation as `MasterIdentity`. `MachineKeySet` was the fix for a *SYSTEM* service (commit `3632626`). The subject name stays `Environment.MachineName`, exactly as Session's per-run certificate had it.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Midview.Protocol.Tests`
Expected: PASS, 17/17 (14 existing + 3 new).

- [ ] **Step 5: Commit**

```bash
git add src/Midview.Protocol/SessionIdentity.cs tests/Midview.Protocol.Tests/SessionIdentityTests.cs
git commit -m "Add a persisted identity for MidviewSession" -m "A classroom list that stores each machine's Session thumbprint breaks if that thumbprint changes on every launch. SessionIdentity mirrors AgentIdentity/MasterIdentity, and lives in Midview.Protocol because the Agent creates the file and Session loads it." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Agent — readable data directory, Session identity created before Session starts

**Files:**
- Modify: `src/Midview.Agent/AgentDataDirectory.cs` (full replacement below)
- Modify: `src/Midview.Agent/AgentLog.cs` (full replacement below)
- Modify: `src/Midview.Agent/Program.cs:1-8`
- Modify: `src/Midview.Agent/AgentServerWorker.cs:17-20`
- Modify: `src/Midview.Session/Program.cs:6`

**Interfaces:**
- Consumes: `SessionIdentity.LoadOrCreate` / `SessionIdentity.DefaultCertificatePath` (Task 2).
- Produces: `AgentDataDirectory.DirectoryPath` (`string`), `AgentDataDirectory.EnsureSecure() -> string` (same signature as before, new behavior), and `AgentDataDirectory.RestrictToAdministrators(string filePath)`. Nothing later in this plan calls these. Task 9 verifies them live.

No automated tests. These changes set real Windows ACLs on `%ProgramData%` and need SYSTEM or elevation, which is the same reason `NetshFirewallController` and `HostsFileManager.ApplyToFile` have none. Task 9 checks them with `icacls` and a standard-user login on Aluno-25.

- [ ] **Step 1: Replace `src/Midview.Agent/AgentDataDirectory.cs`**

```csharp
using System.Security.AccessControl;
using System.Security.Principal;

namespace Midview.Agent;

public static class AgentDataDirectory
{
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Midview");

    // Applied on every Agent start, not only when the directory is first
    // created: machines that ran an older Agent still have it locked to
    // SYSTEM/Administrators only, which stops MidviewSession -- running as
    // the logged-in student -- from reading trusted-masters.json and
    // session-identity.pfx. Users get read-only access: only tampering
    // with the trust list is a concern here, not anyone reading it. The one
    // real secret, the Agent's own key, gets RestrictToAdministrators.
    public static string EnsureSecure()
    {
        var directoryInfo = Directory.CreateDirectory(DirectoryPath);

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(InheritableRule(WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl));
        security.AddAccessRule(InheritableRule(WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl));
        security.AddAccessRule(InheritableRule(WellKnownSidType.BuiltinUsersSid, FileSystemRights.ReadAndExecute));
        directoryInfo.SetAccessControl(security);

        return DirectoryPath;
    }

    // Cuts one file off from the directory's inherited Users read access.
    public static void RestrictToAdministrators(string filePath)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(filePath).SetAccessControl(security);
    }

    private static FileSystemAccessRule InheritableRule(WellKnownSidType sidType, FileSystemRights rights) =>
        new(new SecurityIdentifier(sidType, null), rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow);
}
```

Why the explicit file ACL is needed: setting the directory's DACL propagates its inheritable entries (now including Users read) to every existing child that inherits, which includes `agent-identity.pfx`. A *protected* file DACL is left alone by that propagation, so once `RestrictToAdministrators` has run it stays SYSTEM/Administrators-only across later starts.

- [ ] **Step 2: Replace `src/Midview.Agent/AgentLog.cs`**

```csharp
namespace Midview.Agent;

public static class AgentLog
{
    // Never throws: it's called from catch blocks and startup paths that must
    // keep going, so a failed log write (directory not created yet, file
    // locked by a reader) is dropped instead of becoming a second failure.
    // Doesn't touch the directory's ACL either -- EnsureSecure now reapplies
    // it in full, which is startup work, not something to repeat per line.
    public static void Warn(string message)
    {
        try
        {
            var logPath = Path.Combine(AgentDataDirectory.DirectoryPath, "agent.log");
            File.AppendAllText(logPath, $"{DateTimeOffset.UtcNow:O} WARN {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // See above: nowhere left to report a logging failure.
        }
    }
}
```

- [ ] **Step 3: In `src/Midview.Agent/Program.cs`**, add `using Midview.Protocol;` after `using Midview.Agent;`. Then, right after the `var sessionExecutablePath = ...;` line and before `var host = ...`, insert:

```csharp

// Before either hosted service starts: SessionSupervisor launches Session
// immediately, and Session -- running as the logged-in student -- can read
// this directory but never create anything in it, so its persisted identity
// has to exist first. Its thumbprint is logged because the auto-launched
// Session has no console to print it to, and agent.log is readable by any user.
try
{
    AgentDataDirectory.EnsureSecure();
    using var sessionCertificate = SessionIdentity.LoadOrCreate(SessionIdentity.DefaultCertificatePath);
    AgentLog.Warn($"Session certificate thumbprint: {sessionCertificate.Thumbprint}");
}
catch (Exception ex)
{
    AgentLog.Warn($"Failed to prepare the data directory or the Session identity: {ex.Message}");
}
```

This has to live here rather than in `AgentServerWorker`. `MidviewAgentWorker` is registered first, and its `ExecuteAsync` calls `EnsureRunning()` synchronously during host startup, so Session would be launched *before* `AgentServerWorker` ran at all.

- [ ] **Step 4: In `src/Midview.Agent/AgentServerWorker.cs`**, replace

```csharp
            var programDataDir = AgentDataDirectory.EnsureSecure();

            var certificate = AgentIdentity.LoadOrCreate(AgentIdentity.DefaultCertificatePath);
```

with

```csharp
            // Already secured by Program.cs, before either hosted service started.
            var programDataDir = AgentDataDirectory.DirectoryPath;

            var certificate = AgentIdentity.LoadOrCreate(AgentIdentity.DefaultCertificatePath);
            AgentDataDirectory.RestrictToAdministrators(AgentIdentity.DefaultCertificatePath);
```

(The existing `AgentLog.Warn($"Agent certificate thumbprint: ...")` line stays right after it.)

- [ ] **Step 5: In `src/Midview.Session/Program.cs`**, replace

```csharp
var serverCertificate = SelfSignedCertificate.Create(Environment.MachineName);
```

with

```csharp
// Persisted, and normally created by the Agent before it launches this
// process (see SessionIdentity) -- so the thumbprint below stays the same
// across restarts and the teacher's classroom list keeps working.
var serverCertificate = SessionIdentity.LoadOrCreate(SessionIdentity.DefaultCertificatePath);
```

Leave the `Console.WriteLine` of the thumbprint on the next line as it is.

- [ ] **Step 6: Build and run the existing tests**

Run: `dotnet build Midview.sln`
Expected: `0 Erro(s)`, with only the three pre-existing `xUnit1031` warnings (in `SecureChannelTests.cs` and `SessionServerTests.cs`).

Run: `dotnet test Midview.sln`
Expected: PASS, 68 total (Protocol 17, Agent 42, Session 9). Nothing here changed behavior that the existing tests cover.

- [ ] **Step 7: Commit**

```bash
git add src/Midview.Agent/AgentDataDirectory.cs src/Midview.Agent/AgentLog.cs src/Midview.Agent/Program.cs src/Midview.Agent/AgentServerWorker.cs src/Midview.Session/Program.cs
git commit -m "Let Session read the Agent's data directory and keep a stable identity" -m "Session runs as the logged-in student, but %ProgramData%\Midview was SYSTEM/Administrators-only (and that ACL was only applied when the directory was created), so a standard login couldn't even read trusted-masters.json. EnsureSecure now reapplies the ACL on every start with Users read-only, and agent-identity.pfx gets its own admin-only ACL. The Agent creates session-identity.pfx before the hosted services start, since SessionSupervisor launches Session immediately and Session can't create files there itself. AgentLog no longer reapplies the ACL per line and never throws." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Session — `ThumbnailRenderer`

**Files:**
- Create: `src/Midview.Session/ThumbnailRenderer.cs`
- Test: `tests/Midview.Session.Tests/ThumbnailRendererTests.cs`

**Interfaces:**
- Consumes: `TileCodec.EncodeTile(Bitmap source, Rectangle rect, long quality = 90L) -> byte[]` and `TileCodec.DecodeTile(byte[]) -> Bitmap` (existing, `Midview.Protocol`).
- Produces: `ThumbnailRenderer.Render(Bitmap frame, int maxWidth, int maxHeight) -> byte[]` (JPEG bytes). Task 5's `SessionServer` calls it with 320, 180.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Midview.Session.Tests/ThumbnailRendererTests.cs
using System.Drawing;
using Midview.Protocol;
using Midview.Session;
using Xunit;

public class ThumbnailRendererTests
{
    [Fact]
    public void Render_WidescreenFrame_FillsTheBoundsExactly()
    {
        using var frame = new Bitmap(1920, 1080);

        var jpegBytes = ThumbnailRenderer.Render(frame, 320, 180);

        using var thumbnail = TileCodec.DecodeTile(jpegBytes);
        Assert.Equal(320, thumbnail.Width);
        Assert.Equal(180, thumbnail.Height);
    }

    [Fact]
    public void Render_NonWidescreenFrame_KeepsAspectRatioWithinTheBounds()
    {
        using var frame = new Bitmap(1280, 1024);

        var jpegBytes = ThumbnailRenderer.Render(frame, 320, 180);

        using var thumbnail = TileCodec.DecodeTile(jpegBytes);
        Assert.Equal(225, thumbnail.Width);
        Assert.Equal(180, thumbnail.Height);
    }

    [Fact]
    public void Render_KeepsTheFramesContent()
    {
        using var frame = new Bitmap(1920, 1080);
        using (var g = Graphics.FromImage(frame))
        {
            g.Clear(Color.FromArgb(200, 30, 90));
        }

        var jpegBytes = ThumbnailRenderer.Render(frame, 320, 180);

        using var thumbnail = TileCodec.DecodeTile(jpegBytes);
        var pixel = thumbnail.GetPixel(160, 90);
        Assert.InRange(Math.Abs(pixel.R - 200), 0, 15);
        Assert.InRange(Math.Abs(pixel.G - 30), 0, 15);
        Assert.InRange(Math.Abs(pixel.B - 90), 0, 15);
    }
}
```

(1280×1024 → scale = min(320/1280, 180/1024) = 0.17578 → 225×180.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Midview.Session.Tests`
Expected: FAIL to compile, `The name 'ThumbnailRenderer' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

```csharp
// src/Midview.Session/ThumbnailRenderer.cs
using System.Drawing;
using System.Drawing.Drawing2D;
using Midview.Protocol;

namespace Midview.Session;

public static class ThumbnailRenderer
{
    // Lower than TileCodec's default of 90: the classroom grid shows this
    // image at a fraction of the screen's real size, where the extra
    // fidelity isn't visible but the extra bytes still cost bandwidth
    // across every machine in the lab.
    private const long JpegQuality = 75L;

    // Scales the whole frame to fit within maxWidth x maxHeight, keeping its
    // aspect ratio (a 5:4 screen isn't stretched into a 16:9 box), then
    // JPEG-encodes it as one image -- no 64px tile grid, no diffing.
    public static byte[] Render(Bitmap frame, int maxWidth, int maxHeight)
    {
        var scale = Math.Min((double)maxWidth / frame.Width, (double)maxHeight / frame.Height);
        var width = Math.Max(1, (int)Math.Round(frame.Width * scale));
        var height = Math.Max(1, (int)Math.Round(frame.Height * scale));

        using var thumbnail = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(thumbnail))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(frame, 0, 0, width, height);
        }

        return TileCodec.EncodeTile(thumbnail, new Rectangle(0, 0, width, height), JpegQuality);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Midview.Session.Tests`
Expected: PASS, 12/12 (9 existing + 3 new).

- [ ] **Step 5: Commit**

```bash
git add src/Midview.Session/ThumbnailRenderer.cs tests/Midview.Session.Tests/ThumbnailRendererTests.cs
git commit -m "Add a one-shot downscaled screenshot for the classroom grid" -m "A grid of 25+ machines can't afford full control's tile stream per machine. ThumbnailRenderer fits one frame into a small box (keeping aspect ratio) and encodes it as a single JPEG." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Session — thumbnail requests alongside full control

**Files:**
- Modify: `src/Midview.Protocol/MessageType.cs`
- Modify: `src/Midview.Session/SessionServer.cs` (replace the `SessionServer` class; the `InputEventCodec` and `TileMessageCodec` classes above it stay untouched)
- Test: `tests/Midview.Session.Tests/SessionServerThumbnailTests.cs`

**Interfaces:**
- Consumes: `ThumbnailRenderer.Render` (Task 4). The test fakes `CountingScreenCapture` and `FakeInputInjector` (public, in `tests/Midview.Session.Tests/SessionServerTests.cs`) and `FakeClock` and `FakeInputDeviceController` (public, in `InputLockManagerTests.cs`, with `DisableCalls`/`EnableCalls` counters).
- Produces: `MessageType.RequestThumbnail = 12` (empty payload, Master → Session) and `MessageType.ThumbnailFrame = 13` (payload = raw JPEG bytes, Session → Master). Task 7's `PreviewClient` uses both. The public `SessionServer` constructor and `Run(CancellationToken)` signatures are unchanged.

Behavior changes, all deliberate (see "Where this plan departs from the spec", item 1):
- `Run` hands each accepted connection to its own background thread instead of serving it inline.
- The capture thread (with its `FramePipeline`) and the heartbeat watchdog timer start on the connection's **first `StartControl`**, not when it connects. Everything after `StartControl` is the same code as today, moved into a nested `ControlFeed` class.
- A connection's cleanup calls `StopLock()` only if that connection is controlling at the time.
- `RequestThumbnail` on a connection without a capture thread → one `ThumbnailFrame` reply. On a connection with one it's ignored, because that thread is the channel's only writer and `SslStream` doesn't support two concurrent writers. A capture/encode failure is logged, gets no reply, and doesn't end the connection.

- [ ] **Step 1: Add the message types.** In `src/Midview.Protocol/MessageType.cs`, after `CommandAck = 11,` add:

```csharp
    RequestThumbnail = 12,
    ThumbnailFrame = 13,
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/Midview.Session.Tests/SessionServerThumbnailTests.cs
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;
using Midview.Session;
using Xunit;

// A real SessionServer on a loopback port, plus any number of trusted
// clients connected to it -- the classroom grid's preview connection and a
// full-control connection are just two such clients.
public sealed class LoopbackSessionServer : IDisposable
{
    private readonly X509Certificate2 _serverCertificate = SelfSignedCertificate.Create("session-under-test");
    private readonly X509Certificate2 _clientCertificate = SelfSignedCertificate.Create("master-under-test");
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly List<SecureChannel> _channels = new();
    private readonly Task _serverTask;

    public LoopbackSessionServer(IScreenCapture screenCapture, FakeInputDeviceController inputDeviceController)
    {
        _listener.Start();
        var server = new SessionServer(
            _listener, _serverCertificate, new TrustStore(new[] { _clientCertificate.Thumbprint }),
            screenCapture, new FakeInputInjector(),
            new InputLockManager(inputDeviceController, new FakeClock(), TimeSpan.FromSeconds(5)));
        _serverTask = Task.Run(() => server.Run(CancellationToken.None));
    }

    public SecureChannel Connect()
    {
        var tcpClient = new TcpClient();
        tcpClient.Connect(IPAddress.Loopback, ((IPEndPoint)_listener.LocalEndpoint).Port);
        // Bounded, so a server that never answers (or never even finishes the
        // TLS handshake) fails the test instead of hanging it.
        tcpClient.ReceiveTimeout = 5000;
        var channel = SecureChannel.AuthenticateAsClient(
            tcpClient, "session-under-test", _clientCertificate, new TrustStore(new[] { _serverCertificate.Thumbprint }));
        _channels.Add(channel);
        return channel;
    }

    public void Dispose()
    {
        foreach (var channel in _channels)
        {
            channel.Dispose();
        }

        _listener.Stop(); // unblocks Run()'s AcceptTcpClient() with an expected exception
        try
        {
            _serverTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Expected: that exception is this helper's shutdown mechanism,
            // same as in SessionServerTests.
        }
    }
}

public class SessionServerThumbnailTests
{
    [Fact]
    public void RequestThumbnail_WithoutControl_RepliesWithThumbnailAndLeavesInputUnlocked()
    {
        var inputDeviceController = new FakeInputDeviceController();
        using var server = new LoopbackSessionServer(new CountingScreenCapture(), inputDeviceController);
        var preview = server.Connect();

        preview.Send(new Message(MessageType.RequestThumbnail, Array.Empty<byte>()));
        var reply = preview.Receive();

        // Not FrameTile: a preview connection must never get the full-control stream.
        Assert.Equal(MessageType.ThumbnailFrame, reply.Type);
        using var thumbnail = TileCodec.DecodeTile(reply.Payload);
        Assert.InRange(thumbnail.Width, 1, 320);
        Assert.InRange(thumbnail.Height, 1, 180);
        Assert.Equal(0, inputDeviceController.DisableCalls);
    }

    [Fact]
    public void RequestThumbnail_WhileAnotherConnectionIsControlling_StillReplies()
    {
        using var server = new LoopbackSessionServer(new CountingScreenCapture(), new FakeInputDeviceController());
        var control = server.Connect();
        control.Send(new Message(MessageType.StartControl, Array.Empty<byte>()));
        Assert.Equal(MessageType.FrameTile, control.Receive().Type); // full control is live

        var preview = server.Connect();
        preview.Send(new Message(MessageType.RequestThumbnail, Array.Empty<byte>()));

        Assert.Equal(MessageType.ThumbnailFrame, preview.Receive().Type);
    }

    [Fact]
    public void ClosingAPreviewConnection_DoesNotReleaseAnotherConnectionsInputLock()
    {
        var inputDeviceController = new FakeInputDeviceController();
        using var server = new LoopbackSessionServer(new CountingScreenCapture(), inputDeviceController);
        var control = server.Connect();
        control.Send(new Message(MessageType.StartControl, Array.Empty<byte>()));
        control.Receive(); // first FrameTile: StartControl has been handled, input is locked

        var preview = server.Connect();
        preview.Send(new Message(MessageType.RequestThumbnail, Array.Empty<byte>()));
        preview.Receive();
        preview.Dispose();
        Thread.Sleep(300); // let the server notice the closed preview connection and run its cleanup

        Assert.Equal(1, inputDeviceController.DisableCalls);
        Assert.Equal(0, inputDeviceController.EnableCalls);
    }
}
```

These are deterministic. `FakeClock` never advances, so the heartbeat watchdog can't release the lock on its own during test 3. `CountingScreenCapture` returns a different 64×64 frame every call, so a control connection always has a tile to send.

- [ ] **Step 3: Run the tests to verify they fail, for the right reasons**

Run: `dotnet test tests/Midview.Session.Tests --filter "FullyQualifiedName~SessionServerThumbnailTests"`
Expected: 3 FAIL, taking about 12 s in total:
- `RequestThumbnail_WithoutControl_...`: `Assert.Equal() Failure ... Expected: ThumbnailFrame, Actual: FrameTile`. Today every connection starts the capture thread on connect.
- The other two: `System.IO.IOException : Unable to read data from the transport connection` after 5 s. The second connection's TLS handshake never completes, because today's server is still busy serving the first connection.

If any of them fails differently, stop and find out why before continuing.

- [ ] **Step 4: Replace the `SessionServer` class.** In `src/Midview.Session/SessionServer.cs`, keep everything above `public sealed class SessionServer` (usings, namespace, `InputEventCodec`, `TileMessageCodec`) exactly as is, and replace from that line to the end of the file with:

```csharp
public sealed class SessionServer
{
    private const int HeartbeatCheckIntervalMs = 1000;

    // Capture cadence is fully decoupled from message traffic (mouse-move
    // events alone can arrive far faster than any capture pipeline should
    // run). ~15 fps matches Veyon's own real-world capture rate.
    private const int CaptureIntervalMs = 66;

    // The classroom grid's tile size in MidviewMaster; frames are scaled to
    // fit inside it, keeping their aspect ratio.
    private const int ThumbnailMaxWidth = 320;
    private const int ThumbnailMaxHeight = 180;

    private readonly TcpListener _listener;
    private readonly X509Certificate2 _serverCertificate;
    private readonly TrustStore _masterTrustStore;
    private readonly IScreenCapture _screenCapture;
    private readonly IInputInjector _inputInjector;
    private readonly InputLockManager _inputLockManager;

    public SessionServer(
        TcpListener listener, X509Certificate2 serverCertificate, TrustStore masterTrustStore,
        IScreenCapture screenCapture, IInputInjector inputInjector, InputLockManager inputLockManager)
    {
        _listener = listener;
        _serverCertificate = serverCertificate;
        _masterTrustStore = masterTrustStore;
        _screenCapture = screenCapture;
        _inputInjector = inputInjector;
        _inputLockManager = inputLockManager;
    }

    public void Run(CancellationToken cancellationToken)
    {
        _listener.Start();
        while (!cancellationToken.IsCancellationRequested)
        {
            var tcpClient = _listener.AcceptTcpClient();
            // Nagle's algorithm batches/delays small writes waiting to
            // coalesce them -- exactly wrong for this protocol's frequent
            // tiny messages (mouse moves, heartbeats, tile updates), adding
            // real latency to both directions regardless of how fast either
            // side processes them.
            tcpClient.NoDelay = true;

            // Each connection gets its own thread: MidviewMaster keeps a
            // long-lived thumbnail connection open to every machine in its
            // classroom grid, so handling one client at a time would leave
            // a full-control connection waiting behind it forever.
            var clientThread = new Thread(() =>
            {
                try
                {
                    HandleClient(tcpClient, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling a client connection: {ex.Message}");
                }
            }) { IsBackground = true };
            clientThread.Start();
        }
    }

    private void HandleClient(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        using var client = tcpClient;
        using var channel = SecureChannel.AuthenticateAsServer(client, _serverCertificate, _masterTrustStore);
        var isControlling = false;

        // Started by the first StartControl, never before: a classroom-grid
        // preview connection only ever asks for thumbnails, so it must not
        // pay for a ~15 fps capture thread or a heartbeat watchdog.
        ControlFeed? controlFeed = null;

        try
        {
            var messageCount = 0;
            var maxGapMs = 0.0;
            var gapSw = System.Diagnostics.Stopwatch.StartNew();

            while (!cancellationToken.IsCancellationRequested && tcpClient.Connected)
            {
                var receiveSw = System.Diagnostics.Stopwatch.StartNew();
                var message = channel.Receive();
                var receiveMs = receiveSw.Elapsed.TotalMilliseconds;

                var gapMs = gapSw.Elapsed.TotalMilliseconds;
                gapSw.Restart();
                maxGapMs = Math.Max(maxGapMs, gapMs);
                messageCount++;
                if (messageCount % 50 == 0)
                {
                    SessionDiagnostics.Log(
                        $"messages received={messageCount} lastReceiveBlockedFor={receiveMs:F1}ms maxGapSinceLast50={maxGapMs:F1}ms");
                    maxGapMs = 0.0;
                }

                switch (message.Type)
                {
                    case MessageType.StartControl:
                        isControlling = true;
                        _inputLockManager.StartLock();
                        controlFeed ??= new ControlFeed(_screenCapture, channel, _inputLockManager, cancellationToken);
                        break;

                    case MessageType.StopControl:
                        isControlling = false;
                        _inputLockManager.StopLock();
                        break;

                    case MessageType.Heartbeat:
                        _inputLockManager.OnHeartbeatReceived();
                        break;

                    case MessageType.InputEvent:
                        if (isControlling)
                        {
                            _inputInjector.Inject(InputEventCodec.Decode(message.Payload));
                        }
                        break;

                    // Only on a connection with no capture thread: that thread
                    // is then this channel's one writer, and SslStream doesn't
                    // support two threads writing at once.
                    case MessageType.RequestThumbnail when controlFeed is null:
                        SendThumbnail(channel);
                        break;
                }
            }
        }
        finally
        {
            controlFeed?.Dispose();
            // Safety net for this connection's own lock only: a preview
            // connection closing must not unlock input that a separate
            // full-control connection to this machine is still holding.
            if (isControlling)
            {
                _inputLockManager.StopLock();
            }
        }
    }

    private void SendThumbnail(SecureChannel channel)
    {
        byte[] jpegBytes;
        try
        {
            using var frame = _screenCapture.CaptureFrame();
            jpegBytes = ThumbnailRenderer.Render(frame, ThumbnailMaxWidth, ThumbnailMaxHeight);
        }
        catch (Exception ex)
        {
            // Not rethrown: one failed capture shouldn't end the connection.
            // No reply is sent; the Master's poll times out and shows this
            // machine as offline until its next attempt.
            Console.WriteLine($"Thumbnail capture failed: {ex.Message}");
            return;
        }

        channel.Send(new Midview.Protocol.Message(MessageType.ThumbnailFrame, jpegBytes));
    }

    // One connection's full-control feed: the fixed-cadence capture thread
    // streaming FrameTiles, plus the watchdog that releases the input lock
    // if the Master stops sending heartbeats.
    private sealed class ControlFeed : IDisposable
    {
        private readonly System.Threading.Timer _heartbeatTimer;
        private readonly CancellationTokenSource _captureCts;
        private readonly Thread _captureThread;

        public ControlFeed(
            IScreenCapture screenCapture, SecureChannel channel, InputLockManager inputLockManager,
            CancellationToken cancellationToken)
        {
            _heartbeatTimer = new System.Threading.Timer(_ => inputLockManager.CheckHeartbeatTimeout(),
                null, HeartbeatCheckIntervalMs, HeartbeatCheckIntervalMs);

            // A fresh FramePipeline per connection: it isn't thread-safe, and
            // reusing one server-wide let a slow-to-stop capture thread from a
            // previous connection race a new one over the same mutable state
            // after a reconnect (also left the diff starting from a stale frame).
            var framePipeline = new FramePipeline(screenCapture);

            // The capture loop runs on its own thread at a fixed cadence,
            // independent of how often messages arrive on the connection.
            // The two threads never touch the channel at the same time from
            // opposite ends of a single call: this thread only sends
            // (FrameTile), HandleClient's loop only receives -- the one
            // reader/one writer pattern SslStream supports without locking.
            _captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _captureThread = new Thread(() => CaptureLoop(framePipeline, channel, _captureCts)) { IsBackground = true };
            _captureThread.Start();
        }

        public void Dispose()
        {
            _captureCts.Cancel();
            _captureThread.Join(TimeSpan.FromSeconds(2));
            _heartbeatTimer.Dispose();
            _captureCts.Dispose();
        }

        private static void CaptureLoop(FramePipeline framePipeline, SecureChannel channel, CancellationTokenSource captureCts)
        {
            try
            {
                while (!captureCts.IsCancellationRequested)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    foreach (var tile in framePipeline.CaptureNextFrame())
                    {
                        channel.Send(new Midview.Protocol.Message(MessageType.FrameTile, TileMessageCodec.Encode(tile)));
                    }

                    var remaining = CaptureIntervalMs - (int)sw.ElapsedMilliseconds;
                    if (remaining > 0)
                    {
                        captureCts.Token.WaitHandle.WaitOne(remaining);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"Capture loop stopped: {ex.Message}");
                captureCts.Cancel(); // connection is likely dead; let the receive loop wind down too
            }
        }
    }
}
```

Review against the old file: `CaptureLoop`'s body, the diagnostics block, and the `StartControl`/`StopControl`/`Heartbeat`/`InputEvent` cases are the same code as before, and only their location changed. In `StartControl`, `StartLock()` runs before the feed starts, so a `FrameTile` never reaches the Master before input is locked. That's the same order as before, when the lock came first and the capture thread was already running.

- [ ] **Step 5: Run all Session tests**

Run: `dotnet test tests/Midview.Session.Tests`
Expected: PASS, 15/15 (9 existing + 3 renderer + 3 new), in about 1 s. The existing `CaptureCadence_IsDecoupledFromMessageRate` sends `StartControl` first, so it passes unchanged.

- [ ] **Step 6: Check for flakiness.** The new tests involve real threads and sockets.

Run: `dotnet test tests/Midview.Session.Tests --no-build` five times in a row.
Expected: 15/15 every time.

- [ ] **Step 7: Commit**

```bash
git add src/Midview.Protocol/MessageType.cs src/Midview.Session/SessionServer.cs tests/Midview.Session.Tests/SessionServerThumbnailTests.cs
git commit -m "Serve thumbnail requests alongside full control in SessionServer" -m "The classroom grid keeps a long-lived preview connection open to every machine. SessionServer served one client at a time (so that connection would have blocked full control forever), started the ~15 fps capture thread on every connection, and released the input lock whenever any connection ended. Each connection now gets its own thread, the capture thread and heartbeat watchdog start on StartControl, only a controlling connection releases the lock, and RequestThumbnail gets a single downscaled ThumbnailFrame back." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Master — roster record and file, plus the Master test project

**Files:**
- Create: `tests/Midview.Master.Tests/Midview.Master.Tests.csproj`
- Modify: `Midview.sln` (via `dotnet sln add`, not by hand)
- Create: `src/Midview.Master/KnownMachine.cs`
- Create: `src/Midview.Master/KnownMachinesFile.cs`
- Test: `tests/Midview.Master.Tests/KnownMachinesFileTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record KnownMachine(string Name, string HostAddress, string SessionThumbprint, string? AgentThumbprint)` in namespace `Midview.Master`.
  - `KnownMachinesFile.DefaultPath` (`string`, `%LOCALAPPDATA%\Midview\known-machines.json`).
  - `KnownMachinesFile.Load(string path) -> IReadOnlyList<KnownMachine>`: empty list if the file doesn't exist. If the JSON is unreadable, it moves the file to `path + ".corrupt"` and throws `InvalidDataException`, whose message is Portuguese and teacher-facing.
  - `KnownMachinesFile.Save(string path, IReadOnlyList<KnownMachine> machines)`: creates the directory if needed, writes indented JSON.
  - Tasks 7 and 8 use `KnownMachine`. Task 8 uses the file class.

- [ ] **Step 1: Create the test project**

```xml
<!-- tests/Midview.Master.Tests/Midview.Master.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Midview.Master\Midview.Master.csproj" />
  </ItemGroup>
</Project>
```

No `<UseWPF>` is needed here. The WPF framework reference flows in from the referenced `Midview.Master` project, and referencing a WinExe from a test project builds fine (both verified). This test project isn't WPF, so `System.IO` *is* an implicit using in it.

Then register it in the existing `tests` solution folder:

Run: `dotnet sln Midview.sln add tests/Midview.Master.Tests/Midview.Master.Tests.csproj --solution-folder tests`
Expected: a message that the project was added to the solution. `git diff Midview.sln` should show the new project nested under the existing `tests` folder GUID `{F0E44190-22E9-4688-80E0-EE51E6AB020C}`, not a second `tests` folder.

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/Midview.Master.Tests/KnownMachinesFileTests.cs
using Midview.Master;
using Xunit;

public class KnownMachinesFileTests
{
    [Fact]
    public void Load_NoFile_ReturnsEmptyList()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-known-{Guid.NewGuid()}.json");

        var result = KnownMachinesFile.Load(path);

        Assert.Empty(result);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-known-{Guid.NewGuid()}.json");
        var machines = new[]
        {
            new KnownMachine("Aluno-25", "192.168.0.125", "AAAA1111", "BBBB2222"),
            new KnownMachine("Aluno-26", "192.168.0.126", "CCCC3333", null),
        };
        try
        {
            KnownMachinesFile.Save(path, machines);
            var result = KnownMachinesFile.Load(path);

            Assert.Equal(machines, result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_CorruptFile_MovesItAsideAndThrows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-known-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, "{ not valid json");

            Assert.Throws<InvalidDataException>(() => KnownMachinesFile.Load(path));
            Assert.False(File.Exists(path));
            Assert.Equal("{ not valid json", File.ReadAllText(path + ".corrupt"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".corrupt");
        }
    }

    [Fact]
    public void Save_MissingDirectory_CreatesIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"midview-known-{Guid.NewGuid()}");
        var path = Path.Combine(directory, "known-machines.json");
        try
        {
            KnownMachinesFile.Save(path, new[] { new KnownMachine("Aluno-25", "192.168.0.125", "AAAA1111", null) });

            Assert.Single(KnownMachinesFile.Load(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Midview.Master.Tests`
Expected: FAIL to compile, with `KnownMachinesFile` / `KnownMachine` not found.

- [ ] **Step 4: Write the implementation**

```csharp
// src/Midview.Master/KnownMachine.cs
namespace Midview.Master;

// One machine in the teacher's classroom list. AgentThumbprint is optional:
// it's only needed for site blocking, and whatever the teacher typed in that
// field when saving is kept as-is.
public sealed record KnownMachine(string Name, string HostAddress, string SessionThumbprint, string? AgentThumbprint);
```

```csharp
// src/Midview.Master/KnownMachinesFile.cs
using System.IO;
using System.Text.Json;

namespace Midview.Master;

// The teacher's own classroom roster -- a per-user convenience file, not a
// security boundary, so unlike trusted-masters.json it gets no special ACL.
public static class KnownMachinesFile
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Midview", "known-machines.json");

    public static IReadOnlyList<KnownMachine> Load(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<KnownMachine>();
        }

        var json = File.ReadAllText(path);
        try
        {
            return JsonSerializer.Deserialize<List<KnownMachine>>(json) ?? new List<KnownMachine>();
        }
        catch (JsonException ex)
        {
            // Moved aside instead of left in place: the teacher's next save
            // would otherwise silently overwrite whatever it still held.
            var corruptPath = path + ".corrupt";
            File.Move(path, corruptPath, overwrite: true);
            throw new InvalidDataException($"arquivo ilegível, movido para {corruptPath}", ex);
        }
    }

    public static void Save(string path, IReadOnlyList<KnownMachine> machines)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, JsonSerializer.Serialize(machines, WriteOptions));
    }
}
```

`System.Text.Json` deserializes the positional record through its constructor, matching JSON property names to constructor parameters, so no attributes are needed.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Midview.Master.Tests`
Expected: PASS, 4/4.

- [ ] **Step 6: Commit**

```bash
git add tests/Midview.Master.Tests/Midview.Master.Tests.csproj tests/Midview.Master.Tests/KnownMachinesFileTests.cs src/Midview.Master/KnownMachine.cs src/Midview.Master/KnownMachinesFile.cs Midview.sln
git commit -m "Add the teacher's persisted classroom roster" -m "KnownMachine holds what reconnecting to a machine needs (name, host, Session and optional Agent thumbprints); KnownMachinesFile keeps the list in %LOCALAPPDATA%. An unreadable file is moved aside rather than overwritten by the next save. Master had no test project, so this adds one." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Master — `PreviewClient` and `MachinePreviewPoller`

**Files:**
- Create: `src/Midview.Master/PreviewClient.cs`
- Create: `src/Midview.Master/MachinePreviewPoller.cs`
- Test: `tests/Midview.Master.Tests/MachinePreviewPollerTests.cs`

**Interfaces:**
- Consumes: `MessageType.RequestThumbnail` / `ThumbnailFrame` (Task 5), `KnownMachine` (Task 6), and `SecureChannel` / `TrustStore` / `Message` / `SelfSignedCertificate` / `TileCodec` (existing, `Midview.Protocol`).
- Produces:
  - `PreviewClient.Connect(string hostAddress, int port, string targetThumbprint, X509Certificate2 masterCertificate, TimeSpan timeout) -> PreviewClient`, `byte[] RequestThumbnail()`, and `Dispose()`.
  - `new MachinePreviewPoller(KnownMachine machine, int port, X509Certificate2 masterCertificate, TimeSpan interval, Action<BitmapSource?> onResult)`, `void Start()`, and `void Dispose()`. `onResult` is called on the poller's own thread once per cycle, with a **frozen** `BitmapSource` or `null`. Task 8's `MachineTileViewModel` constructs it with `MidviewPorts.Session` and a 3 s interval.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Midview.Master.Tests/MachinePreviewPollerTests.cs
using System.Collections.Concurrent;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Media.Imaging;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// Stands in for a Session on a loopback port: accepts one connection and
// answers every RequestThumbnail with the same 64x36 JPEG.
public sealed class FakeThumbnailServer : IDisposable
{
    private readonly X509Certificate2 _certificate = SelfSignedCertificate.Create("fake-session");
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public FakeThumbnailServer(X509Certificate2 trustedMasterCertificate)
    {
        _listener.Start();
        byte[] jpegBytes;
        using (var image = new Bitmap(64, 36))
        {
            jpegBytes = TileCodec.EncodeTile(image, new Rectangle(0, 0, 64, 36));
        }

        Task.Run(() =>
        {
            try
            {
                using var tcpClient = _listener.AcceptTcpClient();
                using var channel = SecureChannel.AuthenticateAsServer(
                    tcpClient, _certificate, new TrustStore(new[] { trustedMasterCertificate.Thumbprint }));
                while (true)
                {
                    if (channel.Receive().Type == MessageType.RequestThumbnail)
                    {
                        channel.Send(new Message(MessageType.ThumbnailFrame, jpegBytes));
                    }
                }
            }
            catch (Exception)
            {
                // The poller disconnected or the test ended.
            }
        });
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public string Thumbprint => _certificate.Thumbprint;

    public void Dispose() => _listener.Stop();
}

public class MachinePreviewPollerTests
{
    [Fact]
    public void ReachableSession_ReportsItsThumbnail()
    {
        var masterCertificate = SelfSignedCertificate.Create("master-under-test");
        using var session = new FakeThumbnailServer(masterCertificate);
        var results = new BlockingCollection<BitmapSource?>();
        using var poller = new MachinePreviewPoller(
            new KnownMachine("Aluno-25", "127.0.0.1", session.Thumbprint, null), session.Port,
            masterCertificate, TimeSpan.FromMilliseconds(100), results.Add);

        poller.Start();

        Assert.True(results.TryTake(out var thumbnail, TimeSpan.FromSeconds(10)));
        Assert.NotNull(thumbnail);
        Assert.Equal(64, thumbnail.PixelWidth);
        Assert.Equal(36, thumbnail.PixelHeight);
        Assert.True(thumbnail.IsFrozen); // the UI thread can only use it if it's frozen
    }

    [Fact]
    public void NothingListening_ReportsNullInsteadOfThrowing()
    {
        var unusedListener = new TcpListener(IPAddress.Loopback, 0);
        unusedListener.Start();
        var closedPort = ((IPEndPoint)unusedListener.LocalEndpoint).Port;
        unusedListener.Stop();
        var results = new BlockingCollection<BitmapSource?>();
        using var poller = new MachinePreviewPoller(
            new KnownMachine("Aluno-26", "127.0.0.1", "0000", null), closedPort,
            SelfSignedCertificate.Create("master-under-test"), TimeSpan.FromMilliseconds(100), results.Add);

        poller.Start();

        Assert.True(results.TryTake(out var thumbnail, TimeSpan.FromSeconds(10)));
        Assert.Null(thumbnail);
    }
}
```

(`System.Drawing` is available here through `Midview.Protocol`'s `System.Drawing.Common` package reference, which flows transitively.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Midview.Master.Tests`
Expected: FAIL to compile, with `MachinePreviewPoller` not found.

- [ ] **Step 3: Write `PreviewClient`**

```csharp
// src/Midview.Master/PreviewClient.cs
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// The classroom grid's connection to one machine's Session. It stays open
// between polls (a fresh TLS handshake to every machine every few seconds
// would be needless overhead) and only ever asks for thumbnails: it never
// sends StartControl, so the Session never starts its capture thread or
// locks the student's input because of it.
public sealed class PreviewClient : IDisposable
{
    private readonly SecureChannel _channel;

    private PreviewClient(SecureChannel channel)
    {
        _channel = channel;
    }

    // Every step is bounded by the timeout, connecting included: a plain
    // TcpClient.Connect to a powered-off machine can hang for ~20 s.
    public static PreviewClient Connect(
        string hostAddress, int port, string targetThumbprint, X509Certificate2 masterCertificate, TimeSpan timeout)
    {
        var tcpClient = new TcpClient();
        try
        {
            if (!tcpClient.ConnectAsync(hostAddress, port).Wait(timeout))
            {
                throw new TimeoutException($"Connecting to {hostAddress}:{port} timed out");
            }

            tcpClient.NoDelay = true;
            tcpClient.ReceiveTimeout = (int)timeout.TotalMilliseconds;
            tcpClient.SendTimeout = (int)timeout.TotalMilliseconds;
            var channel = SecureChannel.AuthenticateAsClient(
                tcpClient, hostAddress, masterCertificate, new TrustStore(new[] { targetThumbprint }));
            return new PreviewClient(channel);
        }
        catch
        {
            tcpClient.Dispose();
            throw;
        }
    }

    // Returns the JPEG bytes of one downscaled screenshot.
    public byte[] RequestThumbnail()
    {
        _channel.Send(new Message(MessageType.RequestThumbnail, Array.Empty<byte>()));
        var reply = _channel.Receive();
        if (reply.Type != MessageType.ThumbnailFrame)
        {
            throw new InvalidDataException($"Expected {MessageType.ThumbnailFrame}, got {reply.Type}");
        }
        return reply.Payload;
    }

    public void Dispose() => _channel.Dispose();
}
```

The socket `ReceiveTimeout` also bounds the TLS handshake (`SslStream`'s synchronous reads honour it) and every `Receive()`. That's what turns "Session never replied" (e.g. a failed capture, see Task 5) into an `IOException` after 5 s, rather than a poller stuck forever.

- [ ] **Step 4: Write `MachinePreviewPoller`**

```csharp
// src/Midview.Master/MachinePreviewPoller.cs
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Media.Imaging;

namespace Midview.Master;

// Keeps one machine's grid tile fresh: every interval, asks its Session for
// a thumbnail over a long-lived PreviewClient. Each machine gets its own
// poller on its own thread, so a slow or offline machine never delays
// another machine's tile.
public sealed class MachinePreviewPoller : IDisposable
{
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(5);

    private readonly KnownMachine _machine;
    private readonly int _port;
    private readonly X509Certificate2 _masterCertificate;
    private readonly TimeSpan _interval;
    private readonly Action<BitmapSource?> _onResult;
    private readonly ManualResetEventSlim _stopRequested = new();
    private readonly Thread _thread;

    // onResult runs on the poller's own thread, once per cycle: a frozen
    // thumbnail, or null when that cycle failed for any reason.
    public MachinePreviewPoller(
        KnownMachine machine, int port, X509Certificate2 masterCertificate, TimeSpan interval,
        Action<BitmapSource?> onResult)
    {
        _machine = machine;
        _port = port;
        _masterCertificate = masterCertificate;
        _interval = interval;
        _onResult = onResult;
        _thread = new Thread(PollLoop) { IsBackground = true, Name = $"Preview poller: {machine.Name}" };
    }

    public void Start() => _thread.Start();

    // Doesn't wait for the thread: it notices within one network timeout and
    // exits on its own, which beats blocking the UI thread that long.
    public void Dispose() => _stopRequested.Set();

    private void PollLoop()
    {
        PreviewClient? client = null;
        try
        {
            while (!_stopRequested.IsSet)
            {
                BitmapSource? thumbnail;
                try
                {
                    client ??= PreviewClient.Connect(
                        _machine.HostAddress, _port, _machine.SessionThumbprint, _masterCertificate, NetworkTimeout);
                    thumbnail = Decode(client.RequestThumbnail());
                }
                catch (Exception)
                {
                    // Offline, unreachable, wrong thumbprint, no reply in
                    // time, a corrupt image: all the same to the grid. Show
                    // the placeholder, and start over on a fresh connection
                    // next cycle -- an SslStream that failed mid-read can't
                    // be reused.
                    client?.Dispose();
                    client = null;
                    thumbnail = null;
                }

                if (_stopRequested.IsSet)
                {
                    break;
                }
                _onResult(thumbnail);
                _stopRequested.Wait(_interval);
            }
        }
        finally
        {
            client?.Dispose();
        }
    }

    private static BitmapSource Decode(byte[] jpegBytes)
    {
        using var stream = new MemoryStream(jpegBytes);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze(); // decoded on this thread; freezing is what lets the UI thread use it
        return frame;
    }
}
```

Why a dedicated thread per machine rather than `Task.Run` plus `Task.Delay`: every call inside the loop is synchronous and can block for up to 5 s (the network timeout). With 25 machines, several of them offline, that would starve the thread pool. It's the same dedicated-thread approach `SessionServer` uses.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Midview.Master.Tests`
Expected: PASS, 6/6 (4 from Task 6 + 2 new), in about 2 s.

- [ ] **Step 6: Commit**

```bash
git add src/Midview.Master/PreviewClient.cs src/Midview.Master/MachinePreviewPoller.cs tests/Midview.Master.Tests/MachinePreviewPollerTests.cs
git commit -m "Poll each classroom machine for a thumbnail on its own connection" -m "PreviewClient keeps one TLS connection per machine open between polls, with every step bounded by a timeout so a powered-off machine can't hang it. MachinePreviewPoller runs one machine's loop on its own thread, decodes and freezes the image off the UI thread, and reports any failure as null (offline) and reconnects next cycle." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Master — the classroom grid window

**Files:**
- Rename: `src/Midview.Master/MainWindow.xaml` → `src/Midview.Master/AddMachineWindow.xaml` (`git mv`, then full replacement below)
- Rename: `src/Midview.Master/MainWindow.xaml.cs` → `src/Midview.Master/AddMachineWindow.xaml.cs` (`git mv`, then full replacement below)
- Create: `src/Midview.Master/MainWindow.xaml`, `src/Midview.Master/MainWindow.xaml.cs` (new grid)
- Create: `src/Midview.Master/MidviewPorts.cs`, `src/Midview.Master/ConnectionErrors.cs`, `src/Midview.Master/MachineTileViewModel.cs`
- Modify: `src/Midview.Master/App.xaml`

**Interfaces:**
- Consumes: `KnownMachine`, `KnownMachinesFile` (Task 6), `MachinePreviewPoller` (Task 7), and existing `ConnectionClient.Connect(string, int, string, X509Certificate2)`, `ViewControlWindow(ConnectionClient)`, `AgentCommandClient.SendCommand(...)`, `MasterIdentity`.
- Produces: `new AddMachineWindow(X509Certificate2 masterCertificate, KnownMachine? machine)` with `event Action<KnownMachine>? MachineSaved`, `MachineTileViewModel`, `MidviewPorts.Session` / `MidviewPorts.Agent`, and `ConnectionErrors.Describe(Exception) -> string`. Nothing after this task consumes them. It's the last code task.

No automated tests: this is WPF window code, validated live in Task 9 (spec). `ViewControlWindow.xaml(.cs)` is not touched.

- [ ] **Step 1: Move the manual-entry window aside, keeping its history**

```bash
git mv src/Midview.Master/MainWindow.xaml src/Midview.Master/AddMachineWindow.xaml
git mv src/Midview.Master/MainWindow.xaml.cs src/Midview.Master/AddMachineWindow.xaml.cs
```

- [ ] **Step 2: Add the two small shared helpers.** Both windows now need the ports and the exception text. Previously a literal `5901` and the `AgentServerWorkerAgentPort` constant lived inside `MainWindow`, and `DescribeException` was private to it.

```csharp
// src/Midview.Master/MidviewPorts.cs
namespace Midview.Master;

internal static class MidviewPorts
{
    public const int Session = 5901; // must match the TcpListener in Midview.Session's Program.cs
    public const int Agent = 28900; // must match Midview.Agent.AgentServerWorker.AgentPort
}
```

```csharp
// src/Midview.Master/ConnectionErrors.cs
namespace Midview.Master;

internal static class ConnectionErrors
{
    // Socket and TLS failures often carry the useful part (e.g. "connection
    // refused") only in the inner exception.
    public static string Describe(Exception ex) =>
        ex.InnerException is not null ? $"{ex.Message} -> {ex.InnerException.Message}" : ex.Message;
}
```

- [ ] **Step 3: Replace `src/Midview.Master/AddMachineWindow.xaml`**. It keeps every existing field and label, renames the class, and adds the "Salvar na lista da turma" section at the bottom, after the Agent thumbprint field that the save captures:

```xml
<Window x:Class="Midview.Master.AddMachineWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="MidviewMaster - Adicionar/testar máquina" Height="680" Width="420">
    <StackPanel Margin="16">
        <TextBlock Text="This MidviewMaster's certificate thumbprint (register this once in every Agent/Session's trust list):" Margin="0,0,0,4" TextWrapping="Wrap" />
        <TextBox x:Name="MyThumbprintTextBlock" IsReadOnly="True" Margin="0,0,0,8" />
        <TextBlock Text="To authorize another teacher's PC as this same Master, copy this file to the same path there (same thumbprint, no lab machine needs updating):" Margin="0,0,0,4" TextWrapping="Wrap" FontSize="11" Foreground="Gray" />
        <TextBox x:Name="KeyFilePathTextBlock" IsReadOnly="True" Margin="0,0,0,8" FontSize="11" />
        <TextBlock Text="Host address:" Margin="0,0,0,4" />
        <TextBox x:Name="HostAddressTextBox" Text="localhost" Margin="0,0,0,8" />
        <TextBlock Text="Trusted certificate thumbprint (Session's):" Margin="0,0,0,4" />
        <TextBox x:Name="ThumbprintTextBox" Margin="0,0,0,8" />
        <Button Content="Connect" Click="OnConnectClicked" />
        <TextBlock x:Name="ConnectStatusTextBlock" Margin="0,4,0,0" TextWrapping="Wrap" />
        <Separator Margin="0,16,0,16" />
        <TextBlock Text="Agent's certificate thumbprint:" Margin="0,0,0,4" />
        <TextBox x:Name="AgentThumbprintTextBox" Margin="0,0,0,8" />
        <TextBlock Text="Domain (for Block/Unblock Domain):" Margin="0,0,0,4" />
        <TextBox x:Name="DomainTextBox" Margin="0,0,0,8" />
        <WrapPanel>
            <Button Content="Block Internet" Click="OnBlockInternetClicked" Margin="0,0,8,4" />
            <Button Content="Unblock Internet" Click="OnUnblockInternetClicked" Margin="0,0,8,4" />
            <Button Content="Block Domain" Click="OnBlockDomainClicked" Margin="0,0,8,4" />
            <Button Content="Unblock Domain" Click="OnUnblockDomainClicked" Margin="0,0,0,4" />
        </WrapPanel>
        <TextBlock x:Name="SiteBlockStatusTextBlock" Margin="0,4,0,0" />
        <Separator Margin="0,16,0,16" />
        <TextBlock Text="Nome na lista da turma:" Margin="0,0,0,4" />
        <TextBox x:Name="MachineNameTextBox" Margin="0,0,0,8" />
        <Button x:Name="SaveToRosterButton" Content="Salvar na lista da turma" Click="OnSaveToRosterClicked" IsEnabled="False" />
        <TextBlock x:Name="SaveStatusTextBlock" Text="Só dá para salvar depois de um Connect bem-sucedido com o endereço e o thumbprint acima." Margin="0,4,0,0" TextWrapping="Wrap" FontSize="11" Foreground="Gray" />
    </StackPanel>
</Window>
```

- [ ] **Step 4: Replace `src/Midview.Master/AddMachineWindow.xaml.cs`**

```csharp
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// Where a machine is proven to work before it joins the classroom list, and
// where one-off unsaved connections still happen. Opened blank from the
// grid's "Adicionar máquina", or prefilled with a saved machine -- which is
// also what scopes the site-blocking buttons to that machine.
public partial class AddMachineWindow : Window
{
    private readonly X509Certificate2 _masterCertificate;

    // What the last successful Connect used. Only this exact pair can be
    // saved: the roster should never hold an entry nobody has tested.
    private (string HostAddress, string Thumbprint)? _verified;

    public AddMachineWindow(X509Certificate2 masterCertificate, KnownMachine? machine)
    {
        InitializeComponent();
        _masterCertificate = masterCertificate;
        MyThumbprintTextBlock.Text = _masterCertificate.Thumbprint;
        KeyFilePathTextBlock.Text = MasterIdentity.DefaultCertificatePath;

        if (machine is not null)
        {
            MachineNameTextBox.Text = machine.Name;
            HostAddressTextBox.Text = machine.HostAddress;
            ThumbprintTextBox.Text = machine.SessionThumbprint;
            AgentThumbprintTextBox.Text = machine.AgentThumbprint ?? string.Empty;
        }

        // Subscribed here, not in XAML: TextChanged already fires inside
        // InitializeComponent (for HostAddressTextBox's default text),
        // before SaveToRosterButton has been created.
        HostAddressTextBox.TextChanged += (_, _) => UpdateSaveButton();
        ThumbprintTextBox.TextChanged += (_, _) => UpdateSaveButton();
    }

    // Raised on the UI thread. MainWindow adds the machine to the roster, or
    // replaces the saved entry with the same host address.
    public event Action<KnownMachine>? MachineSaved;

    private async void OnConnectClicked(object sender, RoutedEventArgs e)
    {
        ConnectStatusTextBlock.Text = "Connecting...";
        var hostAddress = HostAddressTextBox.Text;
        var thumbprint = ThumbprintTextBox.Text;
        try
        {
            var client = await Task.Run(() => ConnectionClient.Connect(
                hostAddress, MidviewPorts.Session, thumbprint, _masterCertificate));

            ConnectStatusTextBlock.Text = string.Empty;
            _verified = (hostAddress, thumbprint);
            UpdateSaveButton();
            var viewControlWindow = new ViewControlWindow(client);
            viewControlWindow.Show();
        }
        catch (Exception ex)
        {
            ConnectStatusTextBlock.Text = $"Failed: {ConnectionErrors.Describe(ex)}";
        }
    }

    private void UpdateSaveButton() =>
        SaveToRosterButton.IsEnabled = _verified is { } verified
            && HostAddressTextBox.Text == verified.HostAddress
            && ThumbprintTextBox.Text == verified.Thumbprint;

    private void OnSaveToRosterClicked(object sender, RoutedEventArgs e)
    {
        if (_verified is not { } verified)
        {
            return;
        }

        var name = MachineNameTextBox.Text.Trim();
        var agentThumbprint = AgentThumbprintTextBox.Text;
        var machine = new KnownMachine(
            name.Length > 0 ? name : verified.HostAddress,
            verified.HostAddress,
            verified.Thumbprint,
            string.IsNullOrWhiteSpace(agentThumbprint) ? null : agentThumbprint);

        MachineSaved?.Invoke(machine);
        SaveStatusTextBlock.Text = $"\"{machine.Name}\" salva na lista da turma.";
    }

    private async void OnBlockInternetClicked(object sender, RoutedEventArgs e) =>
        await SendAgentCommandAsync(MessageType.BlockInternet, Array.Empty<byte>());

    private async void OnUnblockInternetClicked(object sender, RoutedEventArgs e) =>
        await SendAgentCommandAsync(MessageType.UnblockInternet, Array.Empty<byte>());

    private async void OnBlockDomainClicked(object sender, RoutedEventArgs e) =>
        await SendAgentCommandAsync(MessageType.BlockDomain, Encoding.UTF8.GetBytes(DomainTextBox.Text));

    private async void OnUnblockDomainClicked(object sender, RoutedEventArgs e) =>
        await SendAgentCommandAsync(MessageType.UnblockDomain, Encoding.UTF8.GetBytes(DomainTextBox.Text));

    private async Task SendAgentCommandAsync(MessageType commandType, byte[] payload)
    {
        SiteBlockStatusTextBlock.Text = "Sending...";
        var hostAddress = HostAddressTextBox.Text;
        var thumbprint = AgentThumbprintTextBox.Text;
        try
        {
            var success = await Task.Run(() => AgentCommandClient.SendCommand(
                hostAddress, MidviewPorts.Agent, thumbprint, _masterCertificate, commandType, payload));
            SiteBlockStatusTextBlock.Text = success ? "OK" : "Failed";
        }
        catch (Exception ex)
        {
            SiteBlockStatusTextBlock.Text = $"Failed: {ConnectionErrors.Describe(ex)}";
        }
    }
}
```

Compared with the old `MainWindow.xaml.cs`, the Connect and site-blocking handlers are unchanged. They just use `MidviewPorts.*` and `ConnectionErrors.Describe` instead of the literal `5901`, the private port constant and the private `DescribeException`. What's new is the constructor parameters and prefill, the `_verified` tracking, and the Save button.

- [ ] **Step 5: Write `src/Midview.Master/MachineTileViewModel.cs`**

```csharp
using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Midview.Master;

// One tile in the classroom grid. Owns that machine's preview poller and
// moves each poll result onto the UI thread.
public sealed class MachineTileViewModel : INotifyPropertyChanged, IDisposable
{
    // Frequent enough to feel live, far below full control's ~15 fps.
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly MachinePreviewPoller _poller;

    public MachineTileViewModel(KnownMachine machine, X509Certificate2 masterCertificate)
    {
        Machine = machine;
        _poller = new MachinePreviewPoller(machine, MidviewPorts.Session, masterCertificate, PollInterval, OnPollResult);
        _poller.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public KnownMachine Machine { get; }

    public string Name => Machine.Name;

    public ImageSource? Thumbnail { get; private set; }

    public string StatusText { get; private set; } = "Conectando...";

    // Shown both before the first image and whenever the machine is
    // unreachable -- the tile never keeps a stale frame on screen.
    public bool ShowPlaceholder => Thumbnail is null;

    public void Dispose() => _poller.Dispose();

    private void OnPollResult(BitmapSource? thumbnail) => _dispatcher.InvokeAsync(() =>
    {
        Thumbnail = thumbnail;
        StatusText = thumbnail is null ? "Sem conexão" : string.Empty;
        OnPropertyChanged(nameof(Thumbnail));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ShowPlaceholder));
    });

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
```

It must be constructed on the UI thread (it captures `Dispatcher.CurrentDispatcher`). `MainWindow` always does that.

- [ ] **Step 6: Write the new `src/Midview.Master/MainWindow.xaml`**

```xml
<Window x:Class="Midview.Master.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="MidviewMaster - Turma" Height="760" Width="1400"
        Closed="OnWindowClosed">
    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BooleanToVisibility" />
    </Window.Resources>
    <DockPanel>
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Background="#FF2D2D30">
            <Button Content="Adicionar máquina" Click="OnAddMachineClicked" Padding="10,4" Margin="4" />
            <TextBlock x:Name="StatusTextBlock" Foreground="White" VerticalAlignment="Center" Margin="8,0" />
        </StackPanel>
        <Grid>
            <TextBlock x:Name="EmptyRosterTextBlock" Text="Nenhuma máquina na lista da turma ainda. Clique em &quot;Adicionar máquina&quot; para conectar e salvar a primeira."
                       HorizontalAlignment="Center" VerticalAlignment="Center" Foreground="Gray" />
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <ItemsControl x:Name="MachinesItemsControl" Margin="8">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <WrapPanel />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Button Click="OnMachineTileClicked" Margin="6" Padding="4" ToolTip="Clique para controlar este computador">
                                <Button.ContextMenu>
                                    <ContextMenu>
                                        <MenuItem Header="Detalhes e bloqueio de sites..." Click="OnMachineDetailsClicked" />
                                        <MenuItem Header="Remover da lista" Click="OnRemoveMachineClicked" />
                                    </ContextMenu>
                                </Button.ContextMenu>
                                <StackPanel Width="320">
                                    <Grid Width="320" Height="180" Background="Black">
                                        <Image Source="{Binding Thumbnail}" Stretch="Uniform" />
                                        <TextBlock Text="{Binding StatusText}" Foreground="LightGray"
                                                   HorizontalAlignment="Center" VerticalAlignment="Center"
                                                   Visibility="{Binding ShowPlaceholder, Converter={StaticResource BooleanToVisibility}}" />
                                    </Grid>
                                    <TextBlock Text="{Binding Name}" FontWeight="SemiBold" Margin="0,4,0,0" TextTrimming="CharacterEllipsis" />
                                </StackPanel>
                            </Button>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
        </Grid>
    </DockPanel>
</Window>
```

(The context menu's `MenuItem`s inherit the tile's `DataContext` from the `Button` that owns the menu. That's what the handlers below rely on.)

- [ ] **Step 7: Write the new `src/Midview.Master/MainWindow.xaml.cs`**

```csharp
using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;

namespace Midview.Master;

// The classroom console: one live thumbnail per saved machine, click one to
// take full control of it (same ViewControlWindow as a manual Connect).
public partial class MainWindow : Window
{
    private readonly X509Certificate2 _masterCertificate = MasterIdentity.LoadOrCreate(MasterIdentity.DefaultCertificatePath);

    // The single source of truth for the roster while the app runs; the
    // file is rewritten from it after every change.
    private readonly ObservableCollection<MachineTileViewModel> _tiles = new();

    public MainWindow()
    {
        InitializeComponent();
        MachinesItemsControl.ItemsSource = _tiles;

        try
        {
            foreach (var machine in KnownMachinesFile.Load(KnownMachinesFile.DefaultPath))
            {
                _tiles.Add(new MachineTileViewModel(machine, _masterCertificate));
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Não foi possível ler a lista da turma: {ex.Message}";
        }

        UpdateEmptyRosterHint();
    }

    private void OnAddMachineClicked(object sender, RoutedEventArgs e) => OpenAddMachineWindow(machine: null);

    private void OnMachineDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTileViewModel tile)
        {
            OpenAddMachineWindow(tile.Machine);
        }
    }

    private void OpenAddMachineWindow(KnownMachine? machine)
    {
        var window = new AddMachineWindow(_masterCertificate, machine) { Owner = this };
        window.MachineSaved += OnMachineSaved;
        window.Show();
    }

    // Same host address as a saved machine means "update that machine"
    // (e.g. its Session was reinstalled and got a new thumbprint), not a
    // second tile for the same computer.
    private void OnMachineSaved(KnownMachine machine)
    {
        var tile = new MachineTileViewModel(machine, _masterCertificate);
        var existing = _tiles.FirstOrDefault(t =>
            string.Equals(t.Machine.HostAddress, machine.HostAddress, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _tiles.Add(tile);
        }
        else
        {
            _tiles[_tiles.IndexOf(existing)] = tile;
            existing.Dispose();
        }

        SaveRoster();
    }

    private void OnRemoveMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTileViewModel tile)
        {
            _tiles.Remove(tile);
            tile.Dispose();
            SaveRoster();
        }
    }

    private void SaveRoster()
    {
        try
        {
            KnownMachinesFile.Save(KnownMachinesFile.DefaultPath, _tiles.Select(t => t.Machine).ToList());
            StatusTextBlock.Text = string.Empty;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Não foi possível salvar a lista da turma: {ex.Message}";
        }

        UpdateEmptyRosterHint();
    }

    private void UpdateEmptyRosterHint() =>
        EmptyRosterTextBlock.Visibility = _tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    // Exactly what AddMachineWindow's manual Connect builds, from the saved
    // entry instead of re-typed fields. Independent of the tile's own preview
    // connection, which keeps polling alongside full control.
    private async void OnMachineTileClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not MachineTileViewModel tile)
        {
            return;
        }

        var machine = tile.Machine;
        StatusTextBlock.Text = $"Conectando a {machine.Name}...";
        try
        {
            var client = await Task.Run(() => ConnectionClient.Connect(
                machine.HostAddress, MidviewPorts.Session, machine.SessionThumbprint, _masterCertificate));

            StatusTextBlock.Text = string.Empty;
            var viewControlWindow = new ViewControlWindow(client);
            viewControlWindow.Show();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Falha ao conectar a {machine.Name}: {ConnectionErrors.Describe(ex)}";
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        foreach (var tile in _tiles)
        {
            tile.Dispose();
        }
    }
}
```

- [ ] **Step 8: Make closing the grid close the app.** In `src/Midview.Master/App.xaml`, change

```xml
             StartupUri="MainWindow.xaml">
```

to

```xml
             StartupUri="MainWindow.xaml"
             ShutdownMode="OnMainWindowClose">
```

Once the grid closes its pollers are stopped, so an `AddMachineWindow` left open behind it would save into a list nobody is showing.

- [ ] **Step 9: Build and run everything**

Run: `dotnet build Midview.sln`
Expected: `0 Erro(s)`. The only warnings are the three pre-existing `xUnit1031` ones, and there are no `MC` (XAML) warnings.

Run: `dotnet test Midview.sln`
Expected: PASS, **80** total (Protocol 17, Agent 42, Session 15, Master 6).

- [ ] **Step 10: Commit**

```bash
git add src/Midview.Master tests/Midview.Master.Tests
git commit -m "Make the classroom grid MidviewMaster's main window" -m "One tile per saved machine, each with its own preview poller, showing the latest thumbnail or a 'Sem conexão' placeholder (never a stale frame); clicking a tile opens the same ViewControlWindow a manual Connect does. The old manual-entry window becomes AddMachineWindow, where a machine must connect successfully before it can be saved; right-clicking a tile opens it prefilled (scoping site blocking to that machine) or removes the machine from the list." -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

(`git status` before committing should show the two renames as `R` entries, plus the new files. Nothing under `ViewControlWindow` should appear.)

---

### Task 9: End-to-end validation on real machines

No new code. This runs the whole feature on Aluno-25 (student) and the teacher's PC, with the project owner present. Several checks here have **never** been exercised before: Session running in a *standard* (non-admin) student login is one of them. A failure is information, not a surprise.

- [ ] **Step 1: Publish** (from the repo root on the dev machine)

```bash
dotnet publish src/Midview.Agent -c Release -r win-x64 --self-contained true -o publish/Agent
dotnet publish src/Midview.Session -c Release -r win-x64 --self-contained true -o publish/Session
dotnet publish src/Midview.Master -c Release -r win-x64 --self-contained true -o publish/Master
```

- [ ] **Step 2: Deploy to Aluno-25.** In an elevated prompt there, run `sc.exe stop MidviewAgent`, then `taskkill /IM Midview.Session.exe /F` (the Agent's auto-launched copy). Copy the contents of **both** `publish/Agent` and `publish/Session` into the Agent's existing install folder: the Agent expects `Midview.Session.exe` right next to `Midview.Agent.exe`. Then run `sc.exe start MidviewAgent`.

- [ ] **Step 3: Check the data-directory ACL** (elevated prompt on Aluno-25):
  - `icacls "%ProgramData%\Midview"` lists `NT AUTHORITY\SYSTEM:(OI)(CI)(F)`, `BUILTIN\Administrators:(OI)(CI)(F)` and `BUILTIN\Users:(OI)(CI)(RX)`, and nothing else.
  - `icacls "%ProgramData%\Midview\agent-identity.pfx"` lists **only** SYSTEM and Administrators, with no `(I)` inherited marks and no Users entry.
  - `%ProgramData%\Midview\session-identity.pfx` exists, and `agent.log` has a new line `Session certificate thumbprint: <X>`. Write `<X>` down.

- [ ] **Step 4: Standard-user login.** Log in to Aluno-25 as a **non-administrator** student account.
  - Task Manager shows `Midview.Session.exe` running under that user within about 5 s (auto-launched by the Agent).
  - In that user's own (non-elevated) prompt, `type "%ProgramData%\Midview\trusted-masters.json"` prints the list. `type "%ProgramData%\Midview\agent-identity.pfx"` fails with "Access is denied".

- [ ] **Step 5: Thumbprint survives a reboot.** Reboot Aluno-25. The newest `Session certificate thumbprint:` line in `agent.log` must equal `<X>`.

- [ ] **Step 6: Register the machine** (teacher's PC, `publish/Master/Midview.Master.exe`):
  - The grid opens showing the empty-roster hint. **Adicionar máquina** opens the form.
  - "Salvar na lista da turma" is disabled. Enter Aluno-25's address and `<X>`, then click **Connect**. `ViewControlWindow` opens and full control works exactly as before. Close it.
  - "Salvar na lista da turma" is now enabled. Edit one character of the host address: it disables again. Put the character back: it re-enables.
  - Enter the Agent thumbprint and the name `Aluno-25`, then click Save. A tile appears in the grid and shows a live screenshot within about 3 s. Its proportions match Aluno-25's screen.

- [ ] **Step 7: Persistence.** Close and reopen Master. The tile is still there. `%LOCALAPPDATA%\Midview\known-machines.json` contains the entry, with the Agent thumbprint.

- [ ] **Step 8: Preview-only is cheap.** While Master only shows the grid (no control window open), Aluno-25's `session-perf.log` (next to `Midview.Session.exe`) must grow by only an occasional line: `HandleClient` logs one line per 50 received messages on each connection, so a lone preview connection (Master polls it every ~3 s) adds a line roughly every 150 s. It must **not** grow one line per frame the way it does while someone is controlling, since `FramePipeline` isn't running at all for a preview-only connection. `Midview.Session.exe` CPU in Task Manager stays near idle between polls.

- [ ] **Step 9: Full control from the grid, alongside the preview.** Click the tile. `ViewControlWindow` opens, mouse and keyboard work, and the student's own input is locked. The tile keeps updating every ~3 s behind it. Close the window: the student's input is unlocked again.

- [ ] **Step 10: A closing preview connection must not unlock someone else's control.** Open control again from the tile. While it's open, right-click the tile, choose **Remover da lista**, and wait about 10 s (the poller's connection closes). The student's input must **still be locked** while `ViewControlWindow` is open. Close `ViewControlWindow`, then re-add the machine via Adicionar máquina.

- [ ] **Step 11: Offline and back.** Shut Aluno-25 down (or unplug its network). Within about 10 s the tile shows "Sem conexão" on black, and never keeps a frozen old screenshot. Bring it back: the thumbnail returns on its own, without touching Master.

- [ ] **Step 12: One dead machine doesn't slow the others.** Close Master, then hand-edit `known-machines.json` to add a second entry with an address no machine uses (e.g. copy the Aluno-25 entry and change `HostAddress` to an unused LAN IP). Reopen Master. The bogus tile shows "Sem conexão", while Aluno-25's tile still refreshes every ~3 s. Remove the bogus entry via right-click afterwards.

- [ ] **Step 13: Site blocking scoped to a tile.** Right-click → **Detalhes e bloqueio de sites...** opens the form prefilled with Aluno-25's name, address and both thumbprints. Run **Block Domain** `example.com`: the status shows `OK`, and `ping example.com` on Aluno-25 resolves to `127.0.0.1`. **Unblock Domain** reverts it.

- [ ] **Step 14: Corrupt roster.** Close Master, put `{ broken` in `known-machines.json`, and reopen. Master starts with an empty grid. The status bar says the list was unreadable and names `known-machines.json.corrupt`, and that file holds `{ broken`. Restore your real list afterwards (rename it back, or re-add the machine).

- [ ] **Step 15: If anything above fails**, use `superpowers:systematic-debugging` before changing code. Don't patch symptoms.

- [ ] **Step 16: Mark the spec validated.** Once every check passes, change the spec's `Status:` line from `Approved for planning` to `Implemented and validated`, then commit:

```bash
git add docs/superpowers/specs/2026-09-22-classroom-console-design.md
git commit -m "Mark the classroom console as implemented and validated" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Self-review notes

- **Spec coverage:**

  | Spec requirement | Where |
  |---|---|
  | Persisted Session identity at `%ProgramData%\Midview\session-identity.pfx` | Tasks 2–3 |
  | `KnownMachine` + `KnownMachinesFile` at `%LOCALAPPDATA%`, plain file | Task 6 |
  | Register only after a successful Connect; Agent thumbprint captured as-is | Task 8 (`_verified`, `UpdateSaveButton`, `OnSaveToRosterClicked`) |
  | `RequestThumbnail` / `ThumbnailFrame`; downscale with `DrawImage`; single JPEG; no diff, capture thread or input lock | Tasks 4–5 |
  | Preview works whether or not someone is controlling; doesn't interfere with full control | Task 5 (tests 2 and 3), Task 9 Steps 9–10 |
  | One long-lived connection per machine, own timer, 3–5 s cadence | Task 7 (`PreviewClient`, `MachinePreviewPoller`), Task 8 (3 s) |
  | Offline placeholder instead of a stale image; keeps retrying | Task 7 (null on failure, reconnect), Task 8 (`ShowPlaceholder`), Task 9 Step 11 |
  | Grid (`ItemsControl` + `WrapPanel`) as primary window; click opens `ViewControlWindow` unchanged | Task 8 |
  | Manual fields and site blocking move to an Add/test view, scoped to the selected machine | Task 8 (`AddMachineWindow`, prefill from the context menu) |
  | Thumbnail failure: logged, no reply, connection survives | Task 5 (`SendThumbnail`) |
  | Tests for `SessionIdentity`, `KnownMachinesFile`, loopback thumbnail with and without concurrent control | Tasks 2, 6, 5 |
  | Non-goals (no discovery, full-control protocol unchanged, no multi-teacher) | Global Constraints; Task 5 only moves the control code |

- **Placeholder scan:** every code step has complete code, and every run step has an exact command and expected output. The only hand-offs to a human are in Task 9, and each one says exactly what to check.
- **Type consistency:** `KnownMachine(Name, HostAddress, SessionThumbprint, AgentThumbprint)` is the same in Tasks 6–8. `MachinePreviewPoller(KnownMachine, int port, X509Certificate2, TimeSpan, Action<BitmapSource?>)` is the same in Task 7's tests and implementation and Task 8's view model. `PreviewClient.Connect(host, port, thumbprint, cert, timeout)` matches its caller in the poller. `MessageType.RequestThumbnail = 12` / `ThumbnailFrame = 13` are used by Task 5 (server) and Task 7 (client and fake server). `AddMachineWindow(X509Certificate2, KnownMachine?)` plus `MachineSaved` match `MainWindow.OpenAddMachineWindow`. `MidviewPorts.Session` / `.Agent` replace the old literal `5901` and `AgentServerWorkerAgentPort`. `SessionIdentity.LoadOrCreate` / `.DefaultCertificatePath` match their callers in both `Program.cs` files.
- **Checked in a prototype:** Tasks 2–8 were implemented as written in a scratch clone of `master` at `11c60b3`. The build was clean, and all 80 tests passed (Session tests five runs in a row). Task 5's new tests failed first against the unchanged server, for the reasons listed in its Step 3.
