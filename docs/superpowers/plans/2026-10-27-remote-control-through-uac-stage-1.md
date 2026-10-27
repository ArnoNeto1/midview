# Remote Control Through UAC — Stage 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep live keyboard/mouse remote control working while the Windows UAC secure desktop is up, by adding a SYSTEM console-session helper the Master controls over a new port, with a fail-closed fallback to today's Session path.

**Architecture:** A new `Midview.Desk` process is launched by the Agent into the active console session with a SYSTEM token. It follows the input desktop (`OpenInputDesktop`/`SetThreadDesktop`) and serves screen + input over TLS on port 5902, authenticated with the Agent's (admin-only-key) certificate. The Master's "Controlar" prefers 5902 with the Agent thumbprint and, when it works, holds the student input lock on the Session (5901) via a new `HoldInputLock` message; it falls back to today's 5901 Session control otherwise.

**Tech Stack:** C#/.NET 8, Windows-only, WinForms/GDI for capture, `SendInput` for injection, the existing `SecureChannel`/`Message`/`ScreenFrame` wire protocol.

**Spec:** `docs/superpowers/specs/2026-10-27-remote-control-through-uac-design.md` (read it; this plan implements its stage 1 only — no one-click fill).

## Global Constraints

- Build and test only with the SDK at `C:\Users\Neto\AppData\Local\Microsoft\dotnet\dotnet.exe` (the `dotnet` on PATH has no SDK). Full suite: `"C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe" test Midview.sln`.
- Never name the product "Veyon" anywhere (code, comments, commits, docs).
- No `Co-Authored-By` trailer on commits unless `.claude/settings.json` has `attribution.commit` set.
- Keep files under 500 lines; validate input at trust boundaries; build + full test suite green before each commit.
- The admin password is typed live into UAC only over the 5902 helper channel; the Session (5901) physically cannot reach the secure desktop, so no password path exists there — do not add one.
- New user-facing strings are Brazilian Portuguese, matching the surrounding UI.
- Pre-existing warnings `xUnit1031` in `SecureChannelTests.cs`/`SessionServerTests.cs` are not new failures.

## Review Focus

- **Rapid, repeated desktop switches** (UAC open→close, Ctrl+Alt+Del, Win+L): the follower must re-attach on every change, including A→A (no-op) and A→B→A, without leaking desktop handles. → Task 4 test `Reports_change_only_when_the_input_desktop_actually_changes`.
- **Port 5902 squatted by a student process at bind time:** the helper must bind exclusive-address-use and the Master must reject a 5902 server presenting any non-Agent-thumbprint certificate (then fall back). → Task 6 test `A_server_cert_that_isnt_the_agent_thumbprint_is_rejected`; Task 9 note on exclusive bind.
- **`AgentThumbprint` null/empty on the roster entry:** the Master skips 5902 entirely, no error, uses 5901. → Task 10 test `No_agent_thumbprint_chooses_the_session_endpoint`.
- **Nobody logged on at the console:** the SYSTEM launch is a no-op and the supervisor does not thrash. → Task 8 test `Launch_is_refused_when_nobody_is_logged_on`.
- **Control connection drops while the lock is held via 5901:** the lock releases (the Session's heartbeat-timeout unlock is the backstop; the keeper stops heartbeating when the control window closes). → Task 3 test `Disposing_the_keeper_stops_the_hold`; relies on existing `InputLockManager` heartbeat-timeout behavior.

---

### Task 1: Move the input codec into Midview.Protocol; add HoldInputLock

**Files:**
- Create: `src/Midview.Protocol/InputEvent.cs` (moved `InputEvent`, `InputEventKind`, now `public`)
- Create: `src/Midview.Protocol/InputEventCodec.cs` (moved from `SessionServer.cs`, now `public`)
- Modify: `src/Midview.Session/InputEvent.cs` (delete — moved), `src/Midview.Session/SessionServer.cs` (drop the `internal InputEventCodec`, use `Midview.Protocol`), `src/Midview.Master/ViewControlWindow.xaml.cs` (replace its duplicate `InputEventKind` enum and hand-rolled encoder with `Midview.Protocol.InputEventCodec.Encode`)
- Modify: `src/Midview.Protocol/MessageType.cs` (add `HoldInputLock = 89` with a comment in the existing style)
- Test: `tests/Midview.Protocol.Tests/InputEventCodecTests.cs`

**Interfaces:**
- Produces: `Midview.Protocol.InputEventKind` (enum, same members/order as today), `Midview.Protocol.InputEvent(InputEventKind Kind, int X, int Y, ushort VirtualKeyCode)`, `public static class InputEventCodec { static byte[] Encode(InputEvent); static InputEvent Decode(byte[]); }`, `MessageType.HoldInputLock`.

- [ ] **Step 1: Write the failing test** `InputEventCodecTests`: `Encode_then_Decode_round_trips_each_kind` — for every `InputEventKind`, `Decode(Encode(e)) == e`; and `Decode_rejects_a_wrong_length_payload` returns the documented fallback (match current `InputEventCodec` behavior).
- [ ] **Step 2: Run it, expect FAIL** (`InputEventCodec` not in Protocol): `"C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe" test tests/Midview.Protocol.Tests --filter InputEventCodec`.
- [ ] **Step 3: Move the types.** Move `InputEvent`/`InputEventKind` to `Midview.Protocol` and make them `public`; move `InputEventCodec` to `Midview.Protocol` as `public`; delete the Session copies and the Master's duplicate enum + encoder, pointing both at `Midview.Protocol`. Add `MessageType.HoldInputLock = 89`. Keep the wire bytes identical (11-byte layout).
- [ ] **Step 4: Run the codec test (PASS) and the whole suite (green).** `"C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe" test Midview.sln`.
- [ ] **Step 5: Commit** — `refactor: move the input-event codec into Midview.Protocol and add HoldInputLock`.

---

### Task 2: Session serves HoldInputLock (lock input, stream nothing)

**Files:**
- Modify: `src/Midview.Session/SessionServer.cs` (handle `HoldInputLock` as a first message, like `LockScreen`)
- Test: `tests/Midview.Session.Tests/SessionServerTests.cs` (new facts)

**Interfaces:**
- Consumes: `MessageType.HoldInputLock` (Task 1), the existing `InputLockManager.StartLock()/StopLock()/OnHeartbeatReceived()`.
- Produces: a `HoldInputLock` first-message that calls `InputLockManager.StartLock()`, treats `Heartbeat` messages as `OnHeartbeatReceived()`, releases with `StopLock()` on disconnect, and never starts a capture thread (served by a new private `ServeInputLockHold`, mirroring `ServeScreenLock`).

- [ ] **Step 1: Write the failing tests** against a loopback `SessionServer` with a fake `IInputDeviceController`: `HoldInputLock_locks_the_student_input` (after sending `HoldInputLock`, the device controller is disabled and `InputLockManager.IsLocked`); `HoldInputLock_sends_no_frames` (no `FrameTile`/`ScreenFrame` arrives within a short window); `Dropping_the_hold_unlocks` (closing the connection, with no heartbeats, lets the heartbeat-timeout `StopLock` release it). Reuse the existing SessionServer test rig/fakes.
- [ ] **Step 2: Run, expect FAIL** (`HoldInputLock` unhandled → falls through / no lock).
- [ ] **Step 3: Implement `ServeInputLockHold`** in `SessionServer.cs`: on `firstMessage.Type == MessageType.HoldInputLock`, `StartLock()`, loop receiving (`OnHeartbeatReceived` on `Heartbeat`, ignore the rest), `StopLock()` in `finally`. Add the dispatch branch next to the `LockScreen` one. Do not touch `_servingSlot`, the capture thread, or `FramePipeline`.
- [ ] **Step 4: Run the new tests (PASS) and the suite (green).**
- [ ] **Step 5: Commit** — `feat: the Session can hold the input lock without streaming (HoldInputLock)`.

---

### Task 3: Master — ControlLockKeeper holds the input lock on 5901

**Files:**
- Create: `src/Midview.Master/ControlLockKeeper.cs` (mirror of `ScreenLockKeeper.cs`)
- Test: `tests/Midview.Master.Tests/ControlLockKeeperTests.cs`

**Interfaces:**
- Consumes: `MessageType.HoldInputLock` (Task 1), `ConnectionClient`, `TcpConnect.WithTimeout`.
- Produces: `public sealed class ControlLockKeeper : IDisposable { ControlLockKeeper(KnownMachine machine, X509Certificate2 masterCertificate, int port = ConnectionClient.SessionPort); void Start(); void Dispose(); }` — opens 5901 with `machine.SessionThumbprint`, sends `HoldInputLock`, heartbeats every 3 s, reconnects on drop, `Dispose` closes at once.

- [ ] **Step 1: Write the failing test** with the `ScriptedAgent` loopback helper (as `InstallClientTests`/`UpdateClientTests` use): `Holds_by_sending_HoldInputLock_then_heartbeats` — the scripted Session asserts the first message is `HoldInputLock` and then sees periodic `Heartbeat`s; `Disposing_the_keeper_stops_the_hold` — after `Dispose`, the connection closes.
- [ ] **Step 2: Run, expect FAIL** (no `ControlLockKeeper`).
- [ ] **Step 3: Implement `ControlLockKeeper`** by copying `ScreenLockKeeper`'s thread/reconnect/heartbeat shape and swapping `LockScreen` for `HoldInputLock` (no `StatusChanged` payload needed; a plain hold). Heartbeat interval `TimeSpan.FromSeconds(3)` as `ScreenLockKeeper`.
- [ ] **Step 4: Run the new test (PASS) and the suite (green).**
- [ ] **Step 5: Commit** — `feat: ControlLockKeeper holds the student input lock while controlling via the helper`.

---

### Task 4: Desk helper project + the desktop-follow state machine

**Files:**
- Create: `src/Midview.Desk/Midview.Desk.csproj` (net8.0-windows, `OutputType` Exe, `UseWindowsForms` true; ProjectReference `Midview.Protocol` and `Midview.Session`; `InternalsVisibleTo` the test project)
- Create: `src/Midview.Desk/DesktopFollower.cs`
- Create: `tests/Midview.Desk.Tests/Midview.Desk.Tests.csproj` + `tests/Midview.Desk.Tests/DesktopFollowerTests.cs`
- Modify: `Midview.sln` (add both projects)

**Interfaces:**
- Produces: `public sealed class DesktopFollower { DesktopFollower(Func<IntPtr> currentInputDesktop); bool PollChanged(out IntPtr desktop); }` — pure: `PollChanged` returns `true` and the new handle exactly when the source value differs from the last one it reported (the first poll reports a change). No Win32 inside the pure core; the real `OpenInputDesktop` source is injected by the caller (Task 7).

- [ ] **Step 1: Write the failing test** `DesktopFollowerTests`: feed a scripted sequence of `IntPtr`s — `[A, A, B, B, A]` → `PollChanged` returns true on the 1st (A), 3rd (B), 5th (A) polls and false on the 2nd and 4th (`Reports_change_only_when_the_input_desktop_actually_changes`).
- [ ] **Step 2: Run, expect FAIL** (project/type absent). Create the projects first if needed, then the test.
- [ ] **Step 3: Implement `DesktopFollower`** holding the last reported handle; first poll reports a change.
- [ ] **Step 4: Run the new test (PASS) and the suite (green).**
- [ ] **Step 5: Commit** — `feat: Midview.Desk project and the desktop-follow state machine`.

---

### Task 5: Desk helper — raw-GDI capture bound to the attached desktop

**Files:**
- Create: `src/Midview.Desk/DeskScreenCapture.cs`
- Test: `tests/Midview.Desk.Tests/DeskScreenCaptureTests.cs`

**Interfaces:**
- Consumes: `Midview.Session.IReusableScreenCapture` (reused interface).
- Produces: `public sealed class DeskScreenCapture : IReusableScreenCapture` — captures the **calling thread's current desktop** with a raw display DC (`CreateDC("DISPLAY", …)` + `BitBlt`), **not** WinForms `Graphics.CopyFromScreen`, so it works on whatever desktop the thread is attached to (incl. the secure desktop). Size from `GetSystemMetrics(SM_CXSCREEN/SM_CYSCREEN)`.

- [ ] **Step 1: Write the failing test** (normal desktop, CI-runnable): `Captures_a_bitmap_of_the_primary_screen_size` — `CaptureFrame()` returns a non-null `Bitmap` whose width/height equal `GetSystemMetrics(SM_CXSCREEN/SM_CYSCREEN)`; `TryCaptureInto` returns false for a wrong-sized target. (The secure-desktop capture itself is live-only; see verification.)
- [ ] **Step 2: Run, expect FAIL** (no `DeskScreenCapture`).
- [ ] **Step 3: Implement `DeskScreenCapture`** with `CreateDC`/`CreateCompatibleDC`/`CreateCompatibleBitmap`/`BitBlt`/`GetDeviceCaps`, copying the cursor-overlay approach from `CursorOverlay` only if trivially reusable; otherwise capture without the cursor for stage 1 (note it).
- [ ] **Step 4: Run the new test (PASS) and the suite (green).**
- [ ] **Step 5: Commit** — `feat: raw-GDI desktop capture for the Desk helper`.

---

### Task 6: Desk helper — the screen + input server

**Files:**
- Create: `src/Midview.Desk/DeskServer.cs`
- Test: `tests/Midview.Desk.Tests/DeskServerTests.cs`

**Interfaces:**
- Consumes: `SecureChannel`, `TrustStore`, `MessageType`, `ScreenFrame`, `FrameCredit`, `Midview.Session.FramePipeline`, `Midview.Session.IInputInjector`, `Midview.Protocol.InputEventCodec`, `DesktopFollower`, `DeskScreenCapture`.
- Produces: `public sealed class DeskServer { DeskServer(TcpListener listener, X509Certificate2 agentCertificate, TrustStore masterTrustStore, Func<IScreenCapture> captureForCurrentDesktop, IInputInjector inputInjector, DesktopFollower follower); void Run(CancellationToken); }` — accepts a trusted Master, serves `ScreenStream` (FramePipeline + `FrameCredit`, same cadence as `SessionServer.StreamScreenFrames`), injects `InputEvent`, honors `StartControl`/`StopControl`/`Heartbeat`/`FrameAck`. The capture and inject work runs on threads that `SetThreadDesktop` to `follower`'s current desktop and re-attach when it changes; the real wiring of `SetThreadDesktop` is in Task 7 so this class stays testable on the normal desktop with a fake injector/capture.

- [ ] **Step 1: Write the failing tests** (loopback, normal desktop, fake `IInputInjector`): `Streams_frames_to_a_trusted_master` (after `ScreenStream`, at least one `ScreenFrame` arrives); `Injects_the_masters_input_events` (an `InputEvent` sent after `StartControl` reaches the fake injector decoded correctly); `A_server_cert_that_isnt_the_agent_thumbprint_is_rejected` (a client trusting a different thumbprint fails the handshake and receives nothing). Reuse the SessionServer test patterns.
- [ ] **Step 2: Run, expect FAIL** (no `DeskServer`).
- [ ] **Step 3: Implement `DeskServer`** by following `SessionServer`'s accept/handshake/stream/receive shape, trimmed to screen + input only (no chat/files/class folder/lock), with one capture thread and one receive loop; `captureForCurrentDesktop` yields a `DeskScreenCapture` for the thread's desktop. Keep both worker threads windowless.
- [ ] **Step 4: Run the new tests (PASS) and the suite (green).**
- [ ] **Step 5: Commit** — `feat: the Desk helper's screen-and-input server on its own port`.

---

### Task 7: Desk helper — entry point (DPI, identity, bind, desktop-follow)

**Files:**
- Create: `src/Midview.Desk/Program.cs`
- Test: live-only (no CI test; see verification). Optionally a tiny `PortArgument` parser unit if an arg is added.

**Interfaces:**
- Consumes: `DeskServer`, `DesktopFollower`, `DeskScreenCapture`, `Midview.Session.SendInputInjector`, `AgentIdentity`/the Agent certificate path, `TrustStore.FromFile(trusted-masters.json)`, `AgentServerWorker`-style port constant.
- Produces: the running helper: binds port 5902 exclusive-address-use, loads the **Agent** certificate (admin-only key) and the trusted-masters store, and runs `DeskServer` with a `DesktopFollower(OpenInputDesktopHandle)` and `SetThreadDesktop` re-attach on each reported change.

- [ ] **Step 1: DPI first.** `Program.Main` calls `System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` as its very first statement (comment why, as `Session/Program.cs:11`). Keep it here, not in `DeskScreenCapture`.
- [ ] **Step 2: Wire identity + bind.** Load the Agent certificate from the admin-only key (same path `AgentIdentity` uses) and `TrustStore.FromFile(AgentServerWorker.TrustedMastersPath)`; create `new TcpListener(IPAddress.Any, 5902)` with `ExclusiveAddressUse = true`; construct the real `OpenInputDesktop`/`SetThreadDesktop`/`GetThreadDesktop` interop and the `DesktopFollower` over it; run `DeskServer`.
- [ ] **Step 3: Desktop re-attach.** On `follower.PollChanged`, `SetThreadDesktop` the capture and inject threads to the new desktop and re-create the capture. (Poll a few times a second.)
- [ ] **Step 4: Build.** `"C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe" build src/Midview.Desk/Midview.Desk.csproj`. (Behavior is verified live — see the final verification task.)
- [ ] **Step 5: Commit** — `feat: the Desk helper entry point (DPI, Agent identity, port 5902, desktop-follow)`.

---

### Task 8: Agent — launch the helper into the console session as SYSTEM

**Files:**
- Create: `src/Midview.Agent/ConsoleSystemProcessLauncher.cs` (implements `ISessionProcessLauncher`)
- Test: `tests/Midview.Agent.Tests/ConsoleSystemProcessLauncherTests.cs` (gating only; the real launch is live)

**Interfaces:**
- Consumes: `ISessionProcessLauncher`, `UserSessionProcessLauncher.SomeoneLoggedOn()`.
- Produces: `public sealed class ConsoleSystemProcessLauncher : ISessionProcessLauncher { ConsoleSystemProcessLauncher(string deskExecutablePath); bool TryLaunch(out int processId); bool IsRunning(int processId); }` — duplicates the Agent's own process token (`OpenProcessToken` → `DuplicateTokenEx` primary), sets `TokenSessionId = WTSGetActiveConsoleSessionId()`, and `CreateProcessAsUser` with `STARTUPINFO.lpDesktop = "winsta0\\default"` and `CREATE_NO_WINDOW`; returns false when nobody is logged on.

- [ ] **Step 1: Write the failing test** `Launch_is_refused_when_nobody_is_logged_on` — with `WTSGetActiveConsoleSessionId` returning none (factor the "someone logged on" check behind an injectable delegate, defaulting to `UserSessionProcessLauncher.SomeoneLoggedOn`), `TryLaunch` returns false and starts nothing. (The successful SYSTEM launch is live-only.)
- [ ] **Step 2: Run, expect FAIL** (no launcher).
- [ ] **Step 3: Implement `ConsoleSystemProcessLauncher`** with the token-duplication interop above; guard on the logged-on delegate; require it run from SYSTEM (document the SeTcbPrivilege dependency in a comment). Reuse the P/Invoke signatures already in `UserSessionProcessLauncher` where identical.
- [ ] **Step 4: Run the gating test (PASS) and the suite (green).**
- [ ] **Step 5: Commit** — `feat: launch the Desk helper into the console session as SYSTEM`.

---

### Task 9: Agent — supervise the helper and open the firewall port

**Files:**
- Modify: `src/Midview.Agent/Program.cs` (register a second `SessionSupervisor` with `ConsoleSystemProcessLauncher`; the Desk exe sits beside `Midview.Agent.exe`, like `Midview.Session.exe`)
- Modify: `src/Midview.Agent/MidviewAgentWorker.cs` (tick also calls `helperSupervisor.EnsureRunning()`)
- Modify: `src/Midview.Agent/AgentServerWorker.cs` (`DeskInboundRuleName` + add to `InboundRuleNames`; open it with `EnsureInboundFirewallRuleExists(DeskInboundRuleName, DeskPort)`; `public const int DeskPort = 5902`)
- Test: `tests/Midview.Agent.Tests/` (firewall-list fact)

**Interfaces:**
- Consumes: `SessionSupervisor` (reused), `ConsoleSystemProcessLauncher` (Task 8), `AgentServerWorker.InboundRuleNames`.
- Produces: `AgentServerWorker.DeskPort = 5902` and a third entry in `InboundRuleNames`; the worker keeps the helper running while someone is logged on.

- [ ] **Step 1: Write the failing test** `InboundRuleNames_includes_the_desk_port_rule` — `AgentServerWorker.InboundRuleNames` contains the new Desk rule name (and `DeskPort == 5902`). (Supervision reuses the already-tested `SessionSupervisor`; the live launch is verified live.)
- [ ] **Step 2: Run, expect FAIL**.
- [ ] **Step 3: Implement** the firewall constant + rule name + startup open (mirror the Agent/Session port lines), register the second supervisor in `Program.cs`, and call `EnsureRunning()` in the worker tick.
- [ ] **Step 4: Run the new test (PASS) and the suite (green).**
- [ ] **Step 5: Commit** — `feat: supervise the Desk helper and open its firewall port`.

---

### Task 10: Ship Midview.Desk.exe, and stop it wherever the Session is stopped

Packaging plus the helper's stop/restart lifecycle (the Task 9 review surfaced the latter: a SYSTEM helper survives a service stop/restart/crash/update, holds port 5902 and locks `Midview.Desk.exe`, so a new helper loops on the exclusive bind and an update/uninstall can't replace the file).

**Files:**
- Modify: `installer/Gerar-Instalador.ps1` (publish `Midview.Desk` into the `aluno` folder, after `Midview.Session`)
- Modify: `src/Midview.Protocol/NetworkUpdate.cs` (add `Midview.Desk.exe` to `RequiredFiles`)
- Modify: `src/Midview.Agent/ServiceControl.cs` (`SessionProcesses.KillAll` also kills `Midview.Desk`)
- Modify: `installer/scripts/Comum.ps1` (`Stop-Midview` also stops `Midview.Desk`)
- Modify: `src/Midview.Agent/MidviewAgentWorker.cs` (kill any stray `Midview.Desk` once before the supervision loop, so a restart/crash doesn't loop on the exclusive bind)
- Test: `tests/Midview.Protocol.Tests/NetworkUpdateTests.cs`

**Interfaces:**
- Consumes: the built `Midview.Desk` (Task 7), `SessionProcesses.KillAll` (ServiceControl.cs:402), `Stop-Midview` (Comum.ps1:60), `MidviewAgentWorker` (Task 9 wiring).
- Produces: the `aluno` folder contains `Midview.Desk.exe` (auto-listed in `midview-build.json`, installed beside `Midview.Agent.exe`); `NetworkUpdate.RequiredFiles` includes it; every place that stops the Session also stops the helper; a restarting Agent clears a stray helper before the supervisor relaunches.

- [ ] **Step 1: Write the failing test** `NetworkUpdateTests.RequiredFiles_includes_the_helper` — `NetworkUpdate.RequiredFiles` contains `"Midview.Desk.exe"`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement packaging** — add `"Midview.Desk.exe"` to `NetworkUpdate.RequiredFiles`; in `Gerar-Instalador.ps1`, add `Publish-Project 'Midview.Desk' (Join-Path $programs 'aluno')` right after the `Midview.Session` publish (comment: like the Session it needs the full Windows Desktop runtime, so it publishes after it; the manifest and installer pick it up from the folder automatically).
- [ ] **Step 4: Implement the helper stop** — `SessionProcesses.KillAll` also kills every `Midview.Desk` process (same loop/exception handling as for `Midview.Session`; update its comment); `Stop-Midview` adds `'Midview.Desk'` to its `Get-Process -Name` list; `MidviewAgentWorker` kills any stray `Midview.Desk` ONCE before entering the supervision loop (so a leftover from a previous Agent instance — which the pid-less `SessionSupervisor` can't adopt — is cleared before the first `EnsureRunning`). If `SessionProcesses.KillAll` is the natural place for the Agent-side kill, reuse it.
- [ ] **Step 5: Run the tests (PASS) and the suite (green).** Keep `UninstallCleanupTests` and any kill/stop tests green; add/extend a test that `SessionProcesses.KillAll`'s target set includes `Midview.Desk` if it can be asserted without launching real processes (e.g. the process-name list is a constant you can check); otherwise note it as live-verified. Optionally run `installer/Gerar-Instalador.ps1` locally and confirm `dist\programa\aluno\Midview.Desk.exe` exists and is in `midview-build.json` (manual).
- [ ] **Step 6: Commit** — `build: ship the Desk helper and stop it wherever the Session is stopped`.

---

### Task 11: Master — prefer 5902 with fail-closed fallback; hold the lock

**Files:**
- Create: `src/Midview.Master/ControlEndpoint.cs` (pure selection)
- Modify: `src/Midview.Master/MainWindow.xaml.cs:~470-485` (the control-open path), `src/Midview.Master/ViewControlWindow.xaml.cs` (accept the endpoint; run `ControlLockKeeper` on the helper path; show a compatibility note on the Session path), `src/Midview.Master/ConnectionClient.cs` (a bounded `Connect` overload using `TcpConnect.WithTimeout`)
- Test: `tests/Midview.Master.Tests/ControlEndpointTests.cs`

**Interfaces:**
- Consumes: `KnownMachine` (`AgentThumbprint`, `SessionThumbprint`), `ConnectionClient`, `ControlLockKeeper` (Task 3), `ConnectionClient.AgentPort`/`SessionPort`, `AgentServerWorker.DeskPort` value (5902 as a Master-side constant).
- Produces: `public enum ControlEndpoint { Helper, Session }` and `public static class ControlChoice { static (ControlEndpoint Endpoint, int Port, string Thumbprint) Choose(KnownMachine m, bool helperReachable); }` — `Helper`/5902/`AgentThumbprint` when `AgentThumbprint` is non-empty **and** `helperReachable`; otherwise `Session`/5901/`SessionThumbprint`. `ViewControlWindow` gains a `ControlEndpoint` parameter; on `Helper` it starts a `ControlLockKeeper`; on `Session` it shows a small note "Controle em modo compatível: não responde à tela do UAC." and behaves as today.

- [ ] **Step 1: Write the failing tests** `ControlEndpointTests`: `A_paired_machine_with_a_reachable_helper_chooses_5902_and_the_agent_thumbprint`; `No_agent_thumbprint_chooses_the_session_endpoint` (empty/null `AgentThumbprint` → Session/5901/SessionThumbprint regardless of reachability); `An_unreachable_helper_falls_back_to_the_session`.
- [ ] **Step 2: Run, expect FAIL** (no `ControlChoice`).
- [ ] **Step 3: Implement `ControlChoice.Choose`** (pure). Add `ConnectionClient.Connect` bounded overload (wrap `TcpConnect.WithTimeout`, ~2 s). In `MainWindow` control-open: try the helper with a bounded connect when `AgentThumbprint` is non-empty; on success pass `ControlEndpoint.Helper` to `ViewControlWindow` and start a `ControlLockKeeper`; on failure/empty, connect 5901 as today and pass `ControlEndpoint.Session`. Dispose the keeper when the window closes.
- [ ] **Step 4: Run the new tests (PASS) and the suite (green).**
- [ ] **Step 5: Commit** — `feat: control prefers the UAC-capable helper with a fail-closed fallback to the Session`.

---

### Task 12: Live verification on a real lab machine (teacher)

Not CI-testable; the teacher runs it. After Task 11, build the installer/network-update package, update one lab machine, then:

- [ ] Control a student machine from the Master ("Controlar"). Confirm the screen and keyboard/mouse work on the normal desktop and that the student input is locked.
- [ ] Trigger a UAC prompt on the student machine (run an installer that elevates). Confirm the teacher still **sees** the UAC secure desktop and can **type** into it (type the admin password live and proceed).
- [ ] Confirm capture is full-screen (not cropped) on a 125%/150% monitor.
- [ ] Confirm an older/un-paired machine (no `AgentThumbprint`) still controls via the Session with the "modo compatível" note and no error.
- [ ] Report back; file any gap as a follow-up before stage 2.
