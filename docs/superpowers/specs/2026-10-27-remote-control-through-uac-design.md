# Midview — Remote Control Through UAC (Design)

Date: 2026-10-27
Status: Design (not yet approved or implemented). Revised 2026-10-27 after an
independent four-lens review (feasibility, security, codebase-fit,
builder-check); the fixes from that review are folded in below.

## Context

When the teacher runs something that needs elevation on a student machine
(most often an installer that cannot run silently), Windows shows the UAC
prompt on the **secure desktop**. At that moment the teacher loses remote
control: `Midview.Session.exe` runs with the student's token on the normal
desktop (`winsta0\default`), so it can neither capture the secure desktop nor
inject keyboard/mouse into it (`SessionServer.CaptureLoop` already just
retries while capture fails). The teacher has to walk to the machine and type
the lab's administrator password by hand.

The companion feature "Instalar programa" (built 2026-10-27; `ProgramInstall` /
`ProgramInstaller` / `InstallClient` / `InstallWindow`) already covers the
common case: the Agent installs a silent `.msi`/`.exe` as its own SYSTEM
identity, with no UAC prompt at all. This feature covers what is left: an
installer that forces a wizard, or any other elevation prompt, where the
teacher must drive the machine and answer UAC live.

The administrator password is the same on every lab machine and the teacher
knows it by heart. They asked for **both** ways to answer UAC remotely:
1. **Type it live** — forward the teacher's keystrokes into the UAC prompt,
   the same as controlling the normal desktop.
2. **One-click fill** — save the lab administrator credentials in the Master
   and send them on a click, so the teacher does not retype the password on
   each machine.

**Shipping in two stages** (review recommendation, agreed): stage 1 is
live-typing surviving UAC, verified end-to-end on a real lab machine; stage 2
is one-click fill, built on top. Separate plans/PRs. Everything below marks
which stage each part belongs to.

## Requirements established

- Keep remote control working while the UAC secure desktop is up: the teacher
  keeps seeing the screen and keeps controlling keyboard and mouse.
- The typed admin password must **never** travel through `Midview.Session.exe`
  (student-owned: the student is logged on as themselves, can debug or replace
  their own process, and can read the Session certificate's private key from
  `%ProgramData%\Midview\session-identity.pfx` — this is deliberate; see
  `SessionIdentity`). This invariant is load-bearing and drives the fail-closed
  gating in §4.
- Leave UAC itself untouched: **do not** change `PromptOnSecureDesktop`.
  Turning the secure desktop off would let a student draw a fake UAC prompt to
  steal the admin password, and would not help anyway (UIPI drops input from a
  medium-integrity process into `consent.exe`).
- Reuse the existing trust model: only a teacher this machine trusts
  (`TrustStore` / `trusted-masters.json`) may connect, over the same TLS
  (`SecureChannel`).
- Degrade safely **but never silently for a password-bearing action**: a
  machine running an older Midview still controls as today (through the
  Session), and the UAC-answering affordances are simply absent there.
- Ship through the existing network update; the Agent opens any new firewall
  port itself at startup.

## Design

### 1. A console-session helper that runs as SYSTEM  (stage 1)

Only a process running as SYSTEM **inside the console session** can attach to
the secure desktop. The Agent is SYSTEM but lives in session 0 (services) and
cannot reach `winsta0`'s desktops in the console session; the Session is in the
console session but runs as the student. So we add a third process — the
**console helper** — launched by the Agent into the active console session with
a **SYSTEM primary token**.

- **Launch (a genuinely new interop path, not a branch of the student
  launcher).** `UserSessionProcessLauncher` today uses `WTSQueryUserToken` (the
  student's token) and never sets the session id or `STARTUPINFO.lpDesktop`
  (`UserSessionProcessLauncher.cs`). The helper needs a **separate method**:
  `OpenProcessToken` on the Agent's own process → `DuplicateTokenEx` (primary) →
  `SetTokenInformation(TokenSessionId = WTSGetActiveConsoleSessionId())` →
  `CreateProcessAsUser` with `STARTUPINFO.lpDesktop = "winsta0\\default"`. This
  requires **SeTcbPrivilege**, which LocalSystem holds and enables by default;
  the launcher must therefore run only from the SYSTEM service. Gated on
  `UserSessionProcessLauncher.SomeoneLoggedOn()`.
- **Supervision.** A second supervisor (the existing `SessionSupervisor` with a
  helper `ISessionProcessLauncher`) keeps the helper running while the lifetime
  policy below says it should be, and restarts it if it dies.
- **No student-facing UI, ever.** The helper owns **no windows, no hooks, no
  message pump** — mandatory, both because `SetThreadDesktop` fails on a thread
  that owns a window and because running windowless at System integrity means
  UIPI blocks any student window-message / "shatter" attempt against it.
- **DPI.** The helper's own entry point must set per-monitor DPI awareness
  (`Application.SetHighDpiMode(PerMonitorV2)` or an app.manifest /
  `SetProcessDpiAwarenessContext`) before any capture — exactly as
  `Midview.Session/Program.cs:11` does. Without it, capture on a 125%/150%
  monitor silently returns only the top-left region and clicks mis-map. Keep
  this call in the helper's `Main`, **out of** any shared capture class so the
  refactor cannot drop it.
- **Lifetime (my call): continuous while someone is logged on**, supervised
  like the Session. On-demand (bind only while controlling) is a smaller attack
  surface but needs a start trigger — the Master would have to ask the Agent to
  start the helper before connecting — which buys little in stage 1; the
  listener is windowless, System-integrity and gated to the Agent thumbprint,
  the same trust as the Agent's own port. On-demand stays available as a later
  tightening.

### 2. Following the input desktop  (stage 1)

A thread can capture/inject only on the desktop it is attached to
(`SetThreadDesktop`), and the secure desktop is a different desktop object
(`Winlogon`) from `Default`. The helper runs a **desktop-follow** loop:

- Poll `OpenInputDesktop()` a few times a second. When it changes
  (Default → Winlogon when UAC appears, and back), re-attach the capture thread
  and the inject thread with `SetThreadDesktop` and re-create the capture
  against the new desktop.
- **Capture primitive:** a raw GDI DC against the attached desktop
  (`CreateDC`/`GetDC` + `BitBlt`), **not** WinForms `Graphics.CopyFromScreen`,
  so it is tied to the thread's desktop rather than the process default. It
  returns the dimmed UAC dialog while the secure desktop is up. (The existing
  `GdiScreenCapture`/`CursorOverlay` are pure GDI and create no window, so they
  are safe to reuse once the DPI and desktop-attach concerns above are handled.)
- **Injection:** `SendInput` from a thread attached to that same desktop;
  SYSTEM integrity clears UIPI into `consent.exe`.
- Capture and inject are **two windowless threads**, each holding its own
  `SetThreadDesktop` to the current input desktop.

Multi-monitor: capture follows the primary screen; the secure desktop is
single-screen, so this is unchanged. Out of scope: non-primary monitors on the
secure desktop.

### 3. Protocol reuse and consolidation  (stage 1)

The helper speaks the **same wire protocol** the Session uses for view/control:
`ScreenStream`, `StartControl`/`StopControl`, `InputEvent`, `FrameAck`,
`Heartbeat`, `ScreenFrame` (see `ConnectionClient`, `MessageType`,
`ScreenFrame`, `FrameCredit`, `FramePipeline`, `TileCodec`).

The input wire format is **already duplicated**: `InputEvent`/`InputEventKind`
live in `Midview.Session`, `InputEventCodec` is `internal` to `SessionServer`,
and `Midview.Master` carries its own `InputEventKind` enum and a hand-rolled
encoder (`ViewControlWindow.xaml.cs`). Rather than add a third copy for the
helper, **move `InputEvent`, `InputEventKind` and the input codec into
`Midview.Protocol`** (where `MessageType.InputEvent` and `ScreenFrame` already
live, and which Master, Session and the helper all reference). This unifies the
three copies into one and exposes nothing new to the student process — the wire
format is already effectively public and is a serializer, not a secret.

### 4. The Master drives it through "Controlar" — fail-closed  (stage 1)

Today control is `ConnectionClient.Connect(host, 5901, machine.SessionThumbprint,
masterCert)` → `ViewControlWindow(client, name, control)`
(`MainWindow.xaml.cs:477`).

- **Controlar** first tries the helper: a **bounded** connect
  (`TcpConnect.WithTimeout`, as `InstallClient` does) to **5902** with
  `machine.AgentThumbprint`. On success, control runs over the helper and
  survives UAC.
- On refusal/timeout, or when `machine.AgentThumbprint` is null/empty (older or
  un-paired machine), it falls back to today's path (5901 + `SessionThumbprint`)
  **fast** (no ~21 s stall), with no error — but this is a **degraded** mode.
- **The endpoint identity (helper 5902 vs Session 5901) is carried into
  `ViewControlWindow`.** It must not be inferred from "is controlling."
- **Fail-closed gate (the linchpin of the whole design):** the UAC-answering
  affordances — admitting admin-password keystrokes for the purpose of filling
  UAC, and the "Preencher a senha de administrador" action — exist **only** when
  the control channel is the 5902 helper authenticated to
  `machine.AgentThumbprint`. On any 5901 fallback the fill action is absent and
  the window visibly tells the teacher control is in legacy mode and cannot
  answer UAC. This is what stops a student who squats 5902 (forcing a downgrade)
  from harvesting the admin password through the Session they own.
- **TLS pinning:** 5902 uses `AuthenticateAsClient` with
  `new TrustStore({ machine.AgentThumbprint })`; **never** an accept-any-server
  variant, and the connect is skipped entirely when the thumbprint is empty.

"Ver a tela" (view-only), chat, files, class folder, thumbnails stay on the
Session (5901) as today.

### 5. Input lock and clipboard while controlling via the helper  (stage 1)

Control today carries the student input lock (`InputLockManager`, low-level
hooks in the Session) and clipboard sync — both part of the Session's control
connection, and both collide with the helper's windowless/`SetThreadDesktop`
constraint. Decisions:

- **Input lock (my call):** the low-level input hook needs a message pump and a
  hooked thread, which the helper cannot have (its threads must stay windowless
  to `SetThreadDesktop`), so the lock stays in the Session. When the Master
  controls via 5902 it also opens and holds a lock channel to the Session on
  5901 — a new first-message `MessageType.HoldInputLock` served exactly like
  `LockScreen` (`ServeScreenLock` / `ScreenLockKeeper`), but calling
  `InputLockManager.StartLock()` (the control lock, with the "você está sendo
  controlado" notice) and streaming nothing. A Master-side `ControlLockKeeper`
  (mirror of `ScreenLockKeeper`) holds it with heartbeats while controlling via
  the helper. The helper does not lock.
- **Clipboard (my call):** clipboard sync stays on the Session path only; it is
  not offered on the helper control path, and is suppressed entirely while a
  credential fill is in flight (stage 2), so a password never reaches the shared
  clipboard.

### 6. Answering UAC

- **Live typing (stage 1).** Needs no new message: the teacher's keystrokes go
  as `InputEvent` (VK codes), now injected on whichever desktop is active,
  including the secure one. **Assumption, documented:** the teacher's and the
  student machine's keyboard layouts match — VK replay reproduces characters per
  the *student* machine's layout, so a mismatched layout (e.g. US vs ABNT2)
  would mistype the symbols common in passwords. The lab is uniform, so this
  holds; it is called out so a future mixed-layout lab is a known constraint,
  not a silent bug. (`KEYEVENTF_UNICODE` does **not** apply to live typing — VK
  is correct there.)
- **One-click fill (stage 2).** A dedicated, helper-only credential message
  (a new `MessageType`, carrying the password) — **not** the 11-byte
  `InputEvent`, which has no character field. On the teacher's lab the UAC
  prompt already shows the admin account selected (decided 2026-10-27), so the
  helper types only the **password** into the focused field, as **Unicode**
  characters via `SendInput` with `KEYEVENTF_UNICODE` (each INPUT's `wScan` is a
  **UTF-16 code unit**; ASCII admin passwords are one unit each), then
  **VK_RETURN** to submit (Unicode input cannot press Enter). A mouse-click
  fallback to focus the password field is a builder detail for the case where
  focus is not already there. The teacher watches and can correct. (The message
  carries only the password; no account field is typed. If a future lab needs
  account selection, the message can grow a username without a protocol break.)
  - The password is stored once in the Master, encrypted at rest with **DPAPI
    (CurrentUser scope, plain — decided 2026-10-27)** under `MasterData.Folder`
    (via `System.Security.Cryptography.ProtectedData`).
  - Plaintext is held in memory only for the moment of the push, zeroed after,
    and never logged or shown in the Master's status/screen.
  - Credentials are **never** written to disk on the student machine and never
    sent to the Session — only injected into the live prompt by the SYSTEM
    helper, over the 5902 channel gated in §4.

### 7. Firewall and shipping  (stage 1)

- `AgentServerWorker` opens 5902 inbound at startup and the uninstaller removes
  it — two edits, mirroring `InboundRuleNames` and the Agent/Session ports.
  (The control connection is inbound, so `FirewallCommands`' outbound rules are
  untouched.) Bind 5902 with exclusive-address-use.
- The helper binary ships inside the student program and reaches the labs
  through the existing network update; no separate install step.

## Security envelope

- **Threat: a student recovering the shared admin password.** Defended by (a)
  the fail-closed gate in §4 — credential keystrokes and one-click fill are
  admitted only on the Agent-authenticated 5902 helper, never on the
  student-owned Session, even under a forced downgrade; (b) the helper
  authenticating with the **Agent** certificate, whose private key is ACL-locked
  to SYSTEM/Administrators (`AgentDataDirectory.RestrictToAdministrators`,
  applied in `Program.cs`), unlike the student-readable Session key; (c) TLS
  thumbprint pinning to `machine.AgentThumbprint`, fail-closed on empty.
- **A student who squats 5902** gains nothing: the Master requires the Agent
  thumbprint (which the student's key cannot satisfy), and the fill/credential
  affordances are gated on a *successful* helper authentication, not on mere
  connection.
- **Agent key hardening** (pre-existing, worth doing with this work): close the
  brief student-readable window when `agent-identity.pfx` is first created (set
  the restrictive ACL before writing contents, or create under a SYSTEM-only
  path), drop the `Exportable` flag if the server only needs to *use* the key,
  and confirm the `MachineKeys` container ACL denies standard users.
- **One-click fill at rest** trades some secrecy for speed and is the teacher's
  explicit opt-in; live typing (stage 1) stores nothing and remains the default.
- A malicious *trusted* teacher is out of scope (already trusted to control).

## Non-goals

- The Windows logon screen when **nobody** is logged on at the console.
- RDP / multiple simultaneous sessions, and non-primary monitors on the secure
  desktop.
- Changing UAC policy or elevation behavior on the student machines.

## Decided with the teacher (2026-10-27)

1. **Delivery is two-stage:** build and live-verify stage 1 (live typing through
   UAC) on a real machine before building stage 2 (one-click fill).
2. **UAC account prompt:** on the teacher's lab the prompt already shows the
   admin account selected, so one-click fill types only the password and Enter
   (§6). Still confirmed on a real machine before stage 2 is finalized.
3. **Password at rest:** plain DPAPI CurrentUser, no extra passphrase (§6).

## Open questions for the builder (sensible-default, settle in the plan)

- Port number (5902 proposed) and helper process/project name.
- Exact Unicode-typing sequence/timing the credential fields accept (data from
  the stage-2 live test, not a constant; a mouse-click fallback to focus the
  password field for the keyboard-uncertain case).
- Whether to keep the agent-key hardening in this work or a follow-up.

## Testing and live verification

- Unit/integration (`dotnet test Midview.sln`, SDK at
  `C:\Users\Neto\AppData\Local\Microsoft\dotnet\dotnet.exe`): the desktop-follow
  state machine (desktop-change → re-attach, with the desktop handle mocked);
  the Master's bounded 5902-then-5901 fallback **and the fail-closed gate** (a
  test asserting no credential/fill is admitted on a 5901 endpoint, and that a
  5902 server presenting any non-Agent-thumbprint cert is rejected); the
  one-click-fill credential message and Unicode encoder; one end-to-end control
  exchange against a real helper on loopback (as `InstallClientTests` does).
- Live, teacher-verified (the design keeps these behind the same code paths the
  tests exercise on the normal desktop, so the live gap is small): secure-desktop
  capture and injection on a real machine (stage 1), and the real UAC credential
  fill sequence/account-selection (stage 2).
