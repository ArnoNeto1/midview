# Remote Control Through UAC — Stage 2 Implementation Plan (one-click fill)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Let the teacher fill the UAC admin-password field on a remotely-controlled machine with one click, by saving the lab admin password in the Master (encrypted) and having the SYSTEM helper type it on the secure desktop.

**Architecture:** Builds entirely on stage 1 (`Midview.Desk` helper on port 5902, Agent-cert trust gate, the `ControlLockKeeper`). The Master stores the admin password DPAPI-encrypted under `MasterData.Folder`; a "Preencher a senha de administrador" button exists ONLY when controlling on the 5902 **Helper** endpoint (fail-closed), and sends a new helper-only credential message over that TLS channel. The helper types the password with `KEYEVENTF_UNICODE` + `VK_RETURN` on its desktop-attached worker thread.

**Tech Stack:** C#/.NET 8, Windows; the existing `SecureChannel`/`Message` wire protocol; Windows DPAPI via `System.Security.Cryptography.ProtectedData`.

**Spec:** `docs/superpowers/specs/2026-10-27-remote-control-through-uac-design.md` (§6 one-click fill; decided: plain DPAPI CurrentUser, the lab UAC already shows the admin account so fill = password + Enter).

## Global Constraints

- Build/test only with the SDK at `C:\Users\Neto\AppData\Local\Microsoft\dotnet\dotnet.exe`. Full suite: `"C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe" test Midview.sln`.
- Never name "Veyon". No `Co-Authored-By` trailer; no `git --no-verify`. English imperative commit subjects (no `feat:` prefix).
- Keep files under 500 lines; validate input at boundaries; build + full suite green before each commit.
- **Security (load-bearing):** the admin password travels ONLY over the 5902 Helper `_client` (never the 5901 Session), is never written to disk except as a DPAPI-CurrentUser blob under `MasterData.Folder`, and is never logged, echoed to the Master status/screen, or put on the clipboard. Hold the plaintext in memory only for the moment of the push and clear the buffer after. (.NET `string` can't be reliably zeroed — carry the password as a `byte[]`/`char[]` through the Master send path and clear it; the helper must decode to a string to type, an unavoidable residual noted in the spec.)
- New user-facing strings are Brazilian Portuguese.
- Pre-existing `xUnit1031` warnings in `SecureChannelTests.cs`/`SessionServerTests.cs` are not new.

## Review Focus

- **The password must never leave on the 5901 Session or be logged.** The fill button exists only on the Helper endpoint; the credential message is only ever sent on the Helper `_client`. → Task 4 test `The_fill_action_is_absent_on_the_session_endpoint` + a grep/review that no log/echo carries it.
- **The helper injects the password only while controlling and only from a trusted Master.** A credential message before `StartControl`, after `StopControl`, or from an untrusted peer types nothing (the handshake already rejects untrusted). → Task 3 tests.
- **Unicode typing of real passwords:** symbols, accented/non-ASCII, and non-BMP (surrogate-pair) characters type as code units, finishing with Enter — not VK codes. → Task 2 pure test over the keystroke builder.
- **DPAPI decrypt failure** (blob from another Windows user, or corrupt) must not crash the Master and must fall back to "set the password again", never a plaintext leak. → Task 4 test.
- **At-rest scope is CurrentUser, never LocalMachine**, under `MasterData.Folder`; nothing plaintext on disk. → Task 4 test asserts the stored bytes are not the plaintext and decrypt only as the same user.

---

### Task 1: Protocol — the credential-fill message

**Files:**
- Create: `src/Midview.Protocol/CredentialFill.cs`
- Modify: `src/Midview.Protocol/MessageType.cs` (add `FillAdminPassword = 90` with a comment in the HoldInputLock style)
- Test: `tests/Midview.Protocol.Tests/CredentialFillTests.cs`

**Interfaces:**
- Produces: `MessageType.FillAdminPassword`; `public static class CredentialFill { static byte[] Encode(ReadOnlySpan<byte> passwordUtf8); static bool TryDecode(byte[] payload, out byte[] passwordUtf8); const int MaxBytes = 512; }` — payload is just the UTF-8 password bytes, length-capped; `TryDecode` rejects empty/oversize. (A helper-only, Master→helper message; the comment says the helper types it into the live UAC prompt and answers nothing, and that it is only ever sent over the 5902 Agent-authenticated channel.)

- [ ] **Step 1: Write the failing test** `CredentialFillTests`: `Encode_then_TryDecode_round_trips` (incl. a non-ASCII password); `TryDecode_rejects_empty_and_oversize` (0 bytes and `MaxBytes+1`).
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** `CredentialFill` + `MessageType.FillAdminPassword = 90`.
- [ ] **Step 4: Run the test (PASS) and the full suite (green).**
- [ ] **Step 5: Commit** — `Add the credential-fill message for one-click UAC password entry`.

---

### Task 2: Injector — Unicode typing + Enter

**Files:**
- Create: `src/Midview.Session/UnicodeKeystrokes.cs` (pure INPUT builder)
- Modify: `src/Midview.Session/IInputInjector.cs` (add `void TypeText(string text, bool pressEnter)`), `src/Midview.Session/SendInputInjector.cs` (implement it via `UnicodeKeystrokes` + `SendInput`)
- Test: `tests/Midview.Session.Tests/UnicodeKeystrokesTests.cs`

**Interfaces:**
- Produces: `IInputInjector.TypeText(string text, bool pressEnter)` (added to the interface; `SendInputInjector` implements it). `public static class UnicodeKeystrokes { static <INPUT-array> Build(string text, bool pressEnter); }` — a pure function returning the `SendInput` INPUT structs: for each UTF-16 code unit a key-down+key-up with `KEYEVENTF_UNICODE` and `wScan = codeUnit` (`wVk = 0`); a non-BMP char is its two surrogate code units (two INPUT pairs); when `pressEnter`, a trailing `VK_RETURN` down+up (VK, not Unicode). Expose the INPUT type/builder at a visibility the test can read (internal + `InternalsVisibleTo` already set for Session.Tests, or make `UnicodeKeystrokes` public returning a testable shape).
- Consumes: the existing `SendInput` P/Invoke + `INPUT`/`KEYBDINPUT` structs in `SendInputInjector.cs`.

- [ ] **Step 1: Write the failing test** `UnicodeKeystrokesTests`: `Builds_a_unicode_down_up_pair_per_code_unit` (an ASCII+accented string → 2 INPUTs per char, `KEYEVENTF_UNICODE` set, `wScan` = the code unit, `wVk` = 0); `A_non_BMP_char_becomes_two_surrogate_units`; `Enter_is_appended_as_VK_RETURN_when_asked` (and absent when not).
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** `UnicodeKeystrokes.Build` and wire `SendInputInjector.TypeText` to send them with one `SendInput` call; add `TypeText` to `IInputInjector`. (The real `SendInput` is live-only; only the builder is unit-tested.)
- [ ] **Step 4: Run the test (PASS) and the full suite (green)** — confirm existing `IInputInjector` implementers/fakes still compile (add `TypeText` to any test fake).
- [ ] **Step 5: Commit** — `Type Unicode text and Enter for the UAC password fill`.

---

### Task 3: Helper — type the password on FillAdminPassword

**Files:**
- Modify: `src/Midview.Desk/DeskServer.cs` (handle `FillAdminPassword` in the control receive loop; enqueue a type action onto the desktop-attached worker)
- Test: `tests/Midview.Desk.Tests/DeskServerTests.cs`

**Interfaces:**
- Consumes: `CredentialFill.TryDecode` (Task 1), `IInputInjector.TypeText` (Task 2), the existing worker/queue in `DeskServer` that already drains `InputEvent`s on the desktop-attached thread.
- Produces: while `isControlling`, a `FillAdminPassword` message decodes the password and enqueues a "type this + Enter" action onto the SAME desktop-bound worker thread that injects input (so it types on the secure desktop); the worker calls `inputInjector.TypeText(password, pressEnter: true)` and clears the decoded buffer after. Ignored when not controlling; a malformed payload drops the connection (as the `InputEvent` path does). The helper answers nothing.

- [ ] **Step 1: Write the failing tests** (loopback, fake `IInputInjector` recording `TypeText`): `Fills_the_password_on_the_worker_after_StartControl` (a `FillAdminPassword` after `StartControl` reaches the fake `TypeText` with the exact password and `pressEnter: true`); `A_fill_before_StartControl_types_nothing`; `A_malformed_fill_drops_the_connection`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** the `FillAdminPassword` case: gate on `isControlling`, `CredentialFill.TryDecode`, enqueue onto the worker (reuse the input queue with a small action union, or a parallel action queue drained in the same loop), `TypeText(..., pressEnter: true)`, clear the buffer. Keep the worker the only injector thread.
- [ ] **Step 4: Run the tests (PASS) and the full suite (green).**
- [ ] **Step 5: Commit** — `Let the helper type the admin password into the UAC prompt`.

---

### Task 4: Master — store the password (DPAPI) and the gated fill button

**Files:**
- Modify: `src/Midview.Master/Midview.Master.csproj` (add `System.Security.Cryptography.ProtectedData`)
- Create: `src/Midview.Master/AdminPasswordStore.cs` (DPAPI CurrentUser under `MasterData.Folder`)
- Modify: `src/Midview.Master/ViewControlWindow.xaml` + `.xaml.cs` (a "Preencher a senha de administrador" button, visible only on the Helper endpoint while controlling; a set/change-password prompt; send `FillAdminPassword` on the Helper `_client`)
- Test: `tests/Midview.Master.Tests/AdminPasswordStoreTests.cs`

**Interfaces:**
- Consumes: `ControlEndpoint.Helper` (stage 1), the Helper `_client` (the 5902 `ConnectionClient`), `MessageType.FillAdminPassword` + `CredentialFill.Encode`, `ProtectedData.Protect/Unprotect(..., DataProtectionScope.CurrentUser)`.
- Produces: `public sealed class AdminPasswordStore { AdminPasswordStore(string folder); bool HasPassword { get; } void Save(byte[] passwordUtf8); byte[]? Load(); void Clear(); }` — `Save` writes `ProtectedData.Protect(password, null, CurrentUser)` to `<folder>/admin-password.bin`; `Load` returns the decrypted UTF-8 bytes or null (null on a missing/corrupt/foreign-user blob — never throws out); the caller clears the returned buffer after use. `ViewControlWindow` gains: a fill button shown only when `_endpoint == Helper && _controlling`; clicking loads the password (prompting to set it if absent), sends `new Message(MessageType.FillAdminPassword, CredentialFill.Encode(bytes))` on the Helper `_client`, clears the buffer; the password is never logged or shown.

- [ ] **Step 1: Write the failing test** `AdminPasswordStoreTests` (temp folder): `Save_then_Load_round_trips`; `The_stored_bytes_are_not_the_plaintext` (the file content != the UTF-8 password); `Load_returns_null_when_absent`; `Load_returns_null_on_a_corrupt_blob_without_throwing`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement** the package reference, `AdminPasswordStore`, and the `ViewControlWindow` button + set-password prompt + gated send. The button's visibility mirrors the compat-note logic (Helper + controlling). The set-password prompt is a simple modal (a `PasswordBox`); store via `AdminPasswordStore.Save`. Never put the password in a `string` field that outlives the send; clear buffers.
- [ ] **Step 4: Run the store tests (PASS) and the full suite (green).** The button/send/prompt are live/visual (Task 5).
- [ ] **Step 5: Commit** — `Save the lab admin password and fill the UAC prompt with one click`.

---

### Task 5: Live verification (teacher)

Not CI-testable. After the installer is rebuilt and one lab machine updated:
- [ ] Control a machine through the helper; trigger a UAC prompt; set the admin password in the Master once; click "Preencher a senha de administrador"; confirm the password lands in the UAC field and Enter submits it (the lab prompt already shows the admin account).
- [ ] Confirm the fill button is ABSENT when controlling an older machine (Session/5901 compat mode).
- [ ] Confirm nothing shows the password in the Master UI/logs.
- [ ] Report; file any gap before release.
