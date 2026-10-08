# Lab configuration (browser and Windows lockdowns)

## Context

The owner keeps a hand-run `.bat` that configures the lab computers: Chrome
administrator policies (clear browsing data on exit, lock down settings and
personalization, block downloads, force guest mode, force-install an
ad-blocking extension), a Windows wallpaper lock, and blocking Microsoft
Edge. Each computer is set up by running the script on it.

This brings those into Midview, applied from the teacher's console to the
whole class or one machine, reapplied on boot, and reported back so the
teacher can see it took. It reuses the exact shape of the existing
site-blocking feature: a desired state persisted in `%ProgramData%\Midview`,
a command handler that applies and reconciles it, and Set/Get/Status
messages.

## Goals

- Apply each `.bat` function from the console as a one-click button, to the
  class or one machine, with a per-machine result.
- Survive a reboot: the Agent reapplies the desired state at startup.
- Let the teacher verify what is actually applied on each machine (read the
  keys back, not just trust the last command).
- Take effect at once: the student's Chrome is closed on apply.
- A reset that returns a machine to the factory default.
- Mixed versions during a network update keep working.

## Non-goals

- No per-student or per-group policies: the whole class (or one machine)
  gets the same thing, like the other features.
- No new policies beyond the `.bat`'s. No scheduling of them.
- Edge is blocked by the program-blocking mechanism Midview already has, not
  by the `.bat`'s Image-File-Execution-Options "debugger" trick (that is the
  technique malware uses to disable security tools, and antivirus flags it).

## The functions

Each is an independent on/off policy. "Blindagem" writes several keys at
once; the teacher still sees one button. Names below are the console labels.

- **Limpeza ao fechar** — Chrome clears history, downloads, cookies,
  passwords and autofill when it closes (`ClearBrowsingDataOnExitList`).
- **Blindagem total** — locks Chrome down: settings, personalization and
  the customize/theme pages blocked (`URLBlocklist`), add-person, sign-in,
  profile picker and NTP background off, incognito off
  (`IncognitoModeAvailability=1`), a fixed Google homepage with the home
  button shown, and the Windows wallpaper locked
  (`ActiveDesktop\NoChangingWallPaper`).
- **Bloquear downloads** — `DownloadRestriction=3`.
- **Modo visitante** — Chrome only opens as a guest
  (`BrowserGuestModeEnforced=1`).
- **Bloqueador de anúncios** — force-installs the chosen extension
  (`ExtensionInstallForcelist` + `ExtensionSettings` for id
  `cfhdojbkjhnklbpkdaibdccddilifddb`). It installs itself the next time
  Chrome opens; no manual step, unlike the `.bat`.
- **Bloquear Edge** — ends `msedge.exe` whenever it starts, through the
  program-blocking enforcer (see below).
- **RESET geral** — removes everything the above applied, under the `HKLM`
  policy keys Midview uses, and lifts the Edge block: the machine returns to
  default. (See the `HKCU` note under "Compatibility and safety".)

The console shows each as a pair (ativar / desativar) plus RESET, inside the
new window.

## Where it lives

Mirrors site blocking exactly.

- **`Midview.Protocol`**
  - `LabConfig` — the desired state: a `SavedAt` (Unix seconds on the
    teacher's PC, newest wins, as `AppBlockPolicy` and the power schedule
    do) and one bool per function above. `Encode`/`TryDecode`
    (format-versioned, validated), and the payload of the three messages.
  - `MessageType` additions: `SetLabConfig = 51`, `GetLabConfig = 52`,
    `LabConfigStatus = 53`.
- **`Midview.Agent`**
  - `LabConfigState` — the persisted desired state
    (`lab-config-state.json`), atomic temp-file swap, corruption-tolerant,
    exactly like `SiteBlockState` (an unreadable file starts as "nothing
    applied", the safe direction the teacher can always fix).
  - `ChromePolicyWriter` / `WindowsPolicyWriter` — write and remove the
    registry keys for each function (via `Microsoft.Win32.Registry`, already
    used in the Agent). Each function has an `Apply` and a `Remove`; `Remove`
    deletes the keys whether or not this run set them, so RESET and the
    "desativar" buttons always clean up. A `Verify` reads the keys back and
    says whether the function is really in force.
  - `LabConfigCommandHandler` — like `SiteBlockCommandHandler`: one gate,
    applies the state's functions, saves, and `ReconcileAtStartup` reapplies.
    On every apply it closes the student's Chrome (below) and updates the
    enforcer's Edge block.
  - The existing `AppBlockEnforcer` gains an "also always end these"
    set, fed by `LabConfigCommandHandler`: `msedge.exe` when Bloquear Edge
    is on, empty otherwise. A process is ended if the teacher's app-block
    policy forbids it **or** it is in this set. This is the reuse the owner
    asked for; the teacher's own program-block list is untouched.
  - Wired in `MidviewAgentWorker` / `AgentServer` next to app blocking;
    `GetLabConfig` answers `LabConfigStatus` built from `Verify` (what is
    actually applied, not just desired), and an Agent from before this
    answers `CommandAck`, so the console can show "Midview antigo".
- **`Midview.Master`**
  - A new toolbar button in the **Configurar** group, just before "Máquinas
    da sala": **"Painel do laboratório"** (`OpensOptions`), opening a window
    with the function buttons, applied to the class. Matching entries in a
    machine's right-click menu apply to one machine. A `LabConfigClient`
    mirrors `AppBlockClient` (GetStatus / Apply over the Agent port 28900),
    and the window shows each machine's verified state and per-machine
    results, reusing the result layout the other class actions use.

## Closing the student's Chrome on apply

Some Chrome policies only take effect when Chrome restarts (the `.bat` kills
`chrome.exe` for this reason). On every `SetLabConfig` apply, the Agent ends
`chrome.exe` in the student's session, best-effort, reusing
`WindowsProcessInspector` (the Agent already ends student processes for
program blocking). It first shows a short notice in that session through the
inspector's `Notify` ("O professor atualizou as configurações; o Chrome foi
reiniciado."). Failing to show the notice or to find Chrome never fails the
command — the policy is still written and will apply on the next Chrome
launch. These lab machines run Chrome ephemerally under these presets, so
little is lost.

## Verification

`GetLabConfig` returns a `LabConfigStatus` the Agent builds by reading the
keys back (`Verify`), so the console shows, per machine, which functions are
truly in force and which the Agent couldn't confirm — the teacher's "está
rodando certo nos PCs" check. The machine tile keeps its existing block
badge; the Painel window carries the detailed list.

## Compatibility and safety

- State is written atomically and tolerates corruption; the Agent reapplies
  the desired state at boot, in both directions (applying and removing), as
  site blocking does after its one-directional bug.
- RESET clears the `HKLM` policy paths Midview uses (the whole Chrome policy
  subtree, the wallpaper lock, the taskbar lock), so it also clears what an
  earlier hand-run script left under those machine-wide keys. It does not
  touch `HKCU`: Midview only ever writes `HKLM` (Chrome reads these
  machine-wide, for every user), and the Agent runs as SYSTEM, whose `HKCU`
  is not the student's — nothing Midview wrote lives in a student's hive, so
  walking `HKEY_USERS` is out of scope.
- **`ClearChromeProfiles` never follows a link.** It runs as SYSTEM over the
  student's own profile folders, so before the recursive delete it checks
  every segment from the profile root down to `User Data` is a real
  directory and not a reparse point (`ProtectedStudentFolders.IsRealDirectory`,
  the same open-as-itself guard the folder protection uses); a junction the
  student plants to redirect the delete is refused and that account skipped.
- Nothing here can block Midview or the shell: the Edge block goes through
  the enforcer, which already protects the shell, the session's own
  processes and Midview (`ProtectedPrograms`), and `msedge.exe` is not among
  those.
- An Agent from before this ignores the new messages (answers `CommandAck`);
  a Master from before never sends them. The screen stream and every other
  feature are untouched.

## Testing

- `Midview.Protocol.Tests`: `LabConfig` encode/decode round-trip, rejecting
  malformed and out-of-range payloads, newest-wins by `SavedAt`.
- `Midview.Agent.Tests`: `LabConfigState` atomic save and corruption
  handling; `ChromePolicyWriter`/`WindowsPolicyWriter` apply→verify→remove
  against a redirected registry root (a test subtree under `HKCU`, never the
  machine's real policy keys); `LabConfigCommandHandler` applies, saves and
  reconciles; the enforcer ends `msedge.exe` only while Bloquear Edge is on
  and never ends a protected process. Chrome-closing is behind the
  `IProcessInspector` fake, so no test touches a real browser.
- `Midview.Master.Tests`: `LabConfigClient` status/apply over a loopback
  fake Agent, including the old-Agent `CommandAck` path.
- No test writes the machine's real Chrome/Windows policy keys, ends a real
  browser, or touches a real lab machine.

## Open choices (settle while implementing)

- Button label "Painel do laboratório" — easy to rename.
- Whether the Painel window also offers applying to the selected machine
  from inside it, or only class-wide there and per-machine from the tile
  menu (leaning: class-wide in the window, per-machine in the menu, like the
  other actions).

## Revision 2026-10-08 (as built)

After reviewing the owner's full lab-control suite (not just the browser
`.bat`), the scope and a few design points settled as:

- **Functions:** the browser lockdowns and Edge above, plus **Travar a barra
  de tarefas** (a `LabConfig` flag: `NoPinningToTaskbar`, and the icon order
  too when a `TaskbarLayout.xml` is present at
  `%ProgramData%\Midview\`), **Limpar perfis do Chrome**
  (`MessageType.ClearChromeProfiles`: the Agent closes Chrome and deletes
  every user's `User Data` — a one-off action, not saved state), and
  **Bloquear sites de distração** (social networks, games, YouTube) through
  the **site blocking the Agent already has** (the hosts file, so it holds in
  any browser), not a Chrome-only list. Blocking USB storage was considered
  and left out for now.
- **Status (`GetLabConfig`)** returns the configuration the Agent keeps
  applying (its saved desired state), not a fresh registry read-back. The
  Agent only answers a `SetLabConfig` with success once the registry write
  itself succeeded as SYSTEM, so that per-machine ✓/✗ is the "did it take"
  confirmation — the gap the owner hit with the hand-run scripts, where
  nothing reported back.
- **Messages:** `SetLabConfig = 51`, `GetLabConfig = 52`,
  `LabConfigStatus = 53`, `ResetLabConfig = 54`, `ClearChromeProfiles = 55`.
- **Console:** the **"Painel do laboratório"** button sits just before
  "Máquinas da sala" in the Configurar group; the window applies each
  function to the class (or one machine from the tile menu), reads each
  machine's current state ("Ver o que está aplicado"), and shows a
  per-machine result.
- **Why this replaces the suite cleanly:** the Agent already runs as SYSTEM,
  so there is no separate admin account or stored password, no scheduled
  tasks runnable by every user, and no dependency on another program to
  dispatch as administrator — the three real weaknesses of the hand-run
  setup.

## Revision 2026-10-08 (0.0.5: uninstall a program, clear downloads)

Two more actions, in the same shape (Agent as SYSTEM, acts on each machine,
reports a per-machine result):

- **Desinstalar programa.** The machine's menu opens a window that reads the
  installed programs from one machine (`GetInstalledPrograms` →
  `InstalledPrograms`, a `ProgramList` of display names, from the Uninstall
  keys under HKLM, 64- and 32-bit), the teacher marks some, and the Agent
  silently uninstalls them on that machine or the class (`UninstallPrograms`).
  Only a program with a silent uninstall is run -- its `QuietUninstallString`,
  or `msiexec /x {guid} /qn` for an MSI -- run through `cmd.exe /s /c` with a
  5-minute timeout, and the whole process tree is killed if it hangs (a SYSTEM
  service has no desktop, so a window nobody can see would otherwise hang).
  A program without a silent uninstall is listed but reported as not
  removable, never run. UWP (Store) apps are out of scope. `ProgramUninstaller`
  takes an injected registry base and command runner so tests use a throwaway
  HKCU subtree and a fake runner.
- **Apagar os downloads.** Two buttons in Manutenção (`ClearDownloads`, a
  1-byte `DownloadsScope`): everything in each user's Downloads folder, or
  only today's files (walked recursively, by each file's own date, pruning
  folders left empty -- a folder's own timestamp is never used, so an old file
  beside a new one survives). Same reparse-point guard as "Limpar perfis", and
  an unknown/missing scope byte is refused rather than treated as "delete
  all". Messages: `GetInstalledPrograms = 56`, `InstalledPrograms = 57`,
  `UninstallPrograms = 58`, `ClearDownloads = 59`. Old Agents answer
  CommandAck and are shown as "Midview antigo".
