# Midview — Block Programs (Design)

Date: 2026-09-27
Status: Implemented 2026-10-04; checked on a real Windows machine in CI
(`bloqueio.yml`), not yet live in a lab

## Context

After "close programs" (`2026-09-24-close-programs-design.md`), which ends
what is open once, the teacher wants to keep chosen programs from running
until the block is lifted. Decided with the owner:

1. **Two modes, chosen each time:**
   - **Block list** ("Bloquear só os escolhidos"): only the listed programs
     are ended when they start.
   - **Allow only** ("Permitir só os escolhidos"): everything the student
     starts is ended, except Windows itself, Midview and the listed
     programs.
2. **Choosing programs:** from the ones open across the class ("Google
   Chrome (chrome.exe) — aberto em 12 de 18 máquinas"), or by typing a name.
3. **Duration:** until the teacher lifts it, surviving reboots and the
   teacher's PC being off, like site blocking.

Applies to the whole class or one machine, like every other feature.

## Architecture

| Who | Does |
|---|---|
| Agent (SYSTEM service) | Keeps the policy on disk, enforces it, answers the Master |
| Session (the student) | Lists the student's open programs for the teacher's window |
| Master | Window to choose and apply; remembers the class policy; resends it |

**Enforcement is in the Agent.** A standing block can't depend on the
Session, which the student can end (the Agent relaunches it only every few
seconds). The Agent runs before anyone logs in, survives reboots and can't
be stopped by the student.

The Agent is SYSTEM, so what it may end is fenced in before any policy rule
is looked at (next section). The Session is used only to list programs:
that list only suggests names; nothing is blocked without the teacher
choosing it.

## What the Agent may end (the safety fence)

A process is a candidate only if **all** of these hold:

1. It runs in an interactive session (session id ≠ 0).
2. Its owner is the user logged on to that session (never SYSTEM,
   LocalService, NetworkService, the DWM/UMFD accounts, or another user).
3. It is not elevated (a program an administrator opened "as
   administrator" is never touched; an administrator at the machine keeps
   control).
4. Its name is not in the protected set, and it isn't Midview.

The protected set (`ProtectedPrograms`, in Protocol, shared with
close-programs so the two never drift) is never ended in either mode, even
if a teacher types one of its names (the window refuses them):

- Shell and session: explorer, ApplicationFrameHost,
  StartMenuExperienceHost, ShellExperienceHost, SearchHost, SearchApp,
  TextInputHost, LockApp, ctfmon, sihost, taskhostw, RuntimeBroker,
  dllhost, conhost, fontdrvhost, dwm, csrss, winlogon, wininit, services,
  lsass, smss, svchost, userinit, LogonUI.
- Accessibility: Narrator, osk, Magnify, AtBroker, Utilman.
- Consent and security UI: consent, CredentialUIBroker,
  SecurityHealthSystray, SecurityHealthHost.
- Error reporting: WerFault, WerFaultSecure.
- Midview: any name starting with "Midview".

Most of these already fail rule 2 (they aren't the student's); the set is
defense in depth and keeps the desktop alive if another rule is wrong.

## Identifying a program

A program is named by its **normalized exe name**: trimmed, the file name
only (a pasted path is accepted), lower-case invariant, ".exe" added when
missing, and exactly `[a-z0-9 ._()+-]{1,60}\.exe` (no wildcards, no path,
no control characters). "Chrome", "CHROME.EXE" and
`C:\...\chrome.exe` are all `chrome.exe`.

For a running process the Agent reads, once per image file (cached by
path, size and write time): the file name, the version resource's
`OriginalFilename`, and the file's owner.

- **Block list:** the process is ended if its file name **or** its
  `OriginalFilename` is on the list. A copy of a listed program under
  another file name is still recognized when it carries its version
  information.
- **Allow only:** the process is allowed if it is "Windows itself" (below)
  or it matches the list — by file name, and, when the file was created by
  the student (owned by them rather than installed by an administrator),
  also by `OriginalFilename`. So an installed program is allowed by its
  name, and a file the student brought in is allowed only if it really is
  that program.

**"Windows itself"** (allow only): the image is under `%SystemRoot%` and
the file is owned by TrustedInstaller, SYSTEM or Administrators. Some
folders under `%SystemRoot%` are writable by standard users; files a
student puts there are owned by the student, so they don't count. Plus a
short list of Windows runtime helpers outside that folder, admin-installed:
`msedgewebview2.exe` (used by Windows and Office panes).

Store apps installed under `Program Files\WindowsApps` are not "Windows
itself": in allow-only mode they are allowed only if listed (the new
Notepad is `notepad.exe`, Calculator `CalculatorApp.exe`). A process whose
image path can't be read is left alone.

## Enforcement loop (`AppBlockEnforcer`)

- While a policy is active, every second: list processes, evaluate only
  the ones not seen before (by id and start time), end the ones the policy
  forbids. On a policy change, every candidate is evaluated again at once,
  so what is already open is ended too.
- With no policy (mode None), the loop does nothing.
- Ending: re-check the start time (a reused id is never ended by mistake),
  then end the process. A failure is logged and not retried for 30 s.
- A blocked program therefore runs for up to about a second before it is
  ended. Out of scope: preventing it from starting at all (AppLocker/SRP
  depend on the Windows edition and are system-wide).
- Everything is isolated from the listener: an enforcement error never
  stops the Agent from answering the teacher (so a lift always gets
  through).

## What the student sees

When something is ended, the Agent shows a system message in the
student's session (`WTSSendMessage`, no waiting, closes by itself after
10 s): "O professor bloqueou este programa: Google Chrome." At most once
per program every 30 s, so a program that is started again and again
doesn't flood the screen. The name shown is the version resource's
`FileDescription`, capped at 64 characters, or the exe name.

## Protocol

Next free message type is 29.

- `SetAppBlock = 29` (Agent): payload `AppBlockPolicy`; answered with one
  `CommandAck` + `CommandResult`.
- `GetAppBlock = 30` (Agent, no payload) → `AppBlockStatus = 31`: the
  policy this machine follows (`AppBlockPolicy` payload).
- `ListOpenPrograms = 32` (Session, no payload) → `OpenPrograms = 33`.

`AppBlockPolicy`, big-endian: format version (1 byte, = 1), `SavedAt`
(int64, Unix seconds on the teacher's PC), mode (1 byte: None = 0,
BlockList = 1, AllowOnly = 2), count (uint16, at most 200), then each name
as length (uint8) + UTF-8 of a normalized exe name. Decoding rejects an
unknown version or mode, None with names, a name that isn't normalized,
duplicates, a protected name, and trailing bytes.

- **Newest wins** (as with the power schedule): the Agent keeps the policy
  with the larger `SavedAt`; an older one is acknowledged with "mantido o
  bloqueio mais novo, salvo em dd/MM HH:mm: ...". Equal `SavedAt` with the
  same content is an idempotent success.
- **Lifting** is `SetAppBlock` with mode None and a newer `SavedAt`, so an
  old block resent later never undoes a lift.
- **Allow only with an empty list** is valid: only Windows and Midview run.

`OpenPrograms`: format version (1), count (uint16, at most 100), then per
program: exe name (uint8 length + UTF-8, normalized), display name (uint8
length + UTF-8, at most 64 characters), window count (uint16). The Session
builds it from the same window filter close-programs uses
(`AppClosePolicy`: visible, unowned, titled, never the shell or Midview).
The Master validates it all: a report that doesn't decode is "resposta
inválida" for that machine.

**Older versions.** An older Agent answers `CommandAck` with no detail to
the new commands: "o Midview dessa máquina é de uma versão anterior:
atualize pela rede ou com o pendrive". An older Session would treat an
unknown first message as a control connection, so the Master only asks a
machine's Session for its programs after that machine's Agent answered
`GetAppBlock` (same package, same version).

## Agent state

- `AppBlockState`: `ProgramData\Midview\app-block.json`, written as temp
  file, flush, move; an unreadable file is renamed `.corrupt` and nothing
  is blocked (the direction the teacher can always fix remotely).
- `AppBlockCommandHandler`: validates, applies newest wins, saves, hands the
  policy to the enforcer; a save failure answers `(false, ...)` and keeps
  the previous policy. Descriptions: "bloqueando 3 programa(s)",
  "permitindo só o Windows e 2 programa(s)", "nenhum programa bloqueado".
- At start, the saved policy is loaded and enforced.
- Uninstall (`--uninstall-cleanup`) deletes the state file; reinstalling
  with the USB installer keeps it (as with site blocking).
- Interplay: network updates change Midview's paths, not its names; close
  programs, the input lock and site blocking are independent.

## Master

`AppBlockWindow`, from the toolbar ("Bloquear programas da turma...") and
the tile menu ("Bloquear programas..."):

1. **Estado atual:** `GetAppBlock` to every target when the window opens,
   grouped ("Nenhum programa bloqueado em 17 máquinas", "Bloqueando
   chrome.exe em 2 máquinas (salvo em 26/09 14:05)", "Sem resposta:
   Aluno-12").
2. **Modo:** "Bloquear só os programas escolhidos — o resto continua
   funcionando." / "Permitir só os programas escolhidos — todo o resto é
   fechado ao abrir (o Windows e o Midview continuam funcionando)."
3. **Programas abertos na turma:** from `ListOpenPrograms`, one row per exe
   with checkbox, "Google Chrome (chrome.exe)" and "aberto em 12 de 18
   máquinas" (only machines that answered count), plus "Atualizar lista".
   Display name: the most common one reported for that exe.
4. **Adicionar pelo nome**, with inline refusals ("nome inválido (use o
   nome do arquivo, por exemplo chrome.exe)", "esse programa faz parte do
   Windows ou do Midview e não pode ser bloqueado").
5. **Aplicar bloqueio** (with a confirmation; allow only warns that
   everything else is closed, unsaved work included) and **Liberar tudo**.
6. One result line per machine, as in `CloseAppsWindow`.

The class policy is saved in `%LOCALAPPDATA%\Midview\app-block.json` and
`ThumbnailPoller` resends it on first sight of each machine and after each
change, like the power schedule, so a machine that was off gets it. A
one-machine policy is sent once and kept by that Agent, but isn't resent.
The tile shows a small badge ("🔒 3 bloqueados", "🔒 só 2 permitidos")
from the poller's `GetAppBlock`.

## Recovery

- The teacher (or any trusted Master) lifts it remotely: the listener is
  never subject to the block.
- The USB uninstaller removes the service and the state.
- A reboot keeps the block (that's the requirement), but the machine,
  logon, desktop and accessibility all work, so the teacher can lift it.

## Out of scope

Preventing a program from starting at all; matching by hash or publisher;
Store apps by package identity; schedules; blocking sites (site blocking
exists); per-machine policies remembered by the Master; icons in the list.

## Tests

- Protocol: `AppBlockPolicy` (round trip per mode, caps, invalid inputs,
  normalization, protected names), `OpenPrograms` report.
- Agent: the decision rules as pure functions over fake process records
  (fence, block list, allow only, "Windows itself", student-owned files,
  protected names, reused ids); `AppBlockState`; the command handler
  (newest wins, lift, save failure); `SetAppBlock`/`GetAppBlock` over a
  real loopback `AgentServer`; uninstall cleanup.
- Session: the open-programs lister over fake windows; `SessionServer`
  answers while another Master controls.
- Master: aggregation, texts, the policy file, the poller's resend and
  badge against a fake Agent.
- Real Windows (the e2e job): install; create a standard local user; start
  a program as that user; block it and check it is ended within a few
  seconds and again when restarted; allow only: a listed program survives
  and another one is ended, while Windows programs keep running; restart
  the service and check the block holds; lift and check a program stays
  open; uninstall and check the state is gone. The first run also records
  whether the runner's session qualifies (session id, elevation), which
  decides how the program is started for the test.
