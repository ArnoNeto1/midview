# Midview — Update Over the Network (Design)

Date: 2026-09-26
Status: Design reviewed (five independent critics and a judge); implemented
2026-09-26; checked on a real Windows machine in CI (`atualizacao.yml`), not
yet live in a lab

## Context

Updating the lab today means regenerating the package and walking to every
machine with the USB stick to run `Instalar-Aluno.cmd` again. This lets the
teacher push a new version of the student program (Agent + Session) from
`MidviewMaster` to machines that already have Midview.

Decided with the owner: **update only**. The first install, and the first
update of an Agent from before this feature, still take one visit with the
USB stick. Nothing new is trusted over the network: an update is accepted
only from a Master the machine already trusts (`trusted-masters.json`,
mutual TLS with pinned thumbprints), the same Masters that can already shut
it down or block its internet. The owner accepted that this extends what a
trusted Master can do, from commands to replacing the program that runs as
SYSTEM.

The rule the design guarantees: **the service always points at a complete,
checked build, and an update that was interrupted or not confirmed is
undone by the build that was already running.**

## Versions side by side, never copied over

- A USB install puts the program in `C:\Program Files\Midview` (the service's
  binary path). Updates go to `%ProgramFiles%\Midview.update\<attempt id>\`,
  one complete folder per attempt, and the update **switches the service's
  binary path** (`ChangeServiceConfig`, then a registry flush) to the new
  folder. Nothing is renamed, copied over or deleted while applying;
  rolling back is switching the path back.
- `Midview.update` is always under `%ProgramFiles%` (students can't create
  folders there), created by the Agent with its permissions set at
  creation: SYSTEM and Administrators full, Users read and execute (the
  Session is launched from there as the student), inherited. An existing
  one that is a reparse point, or not owned by SYSTEM or Administrators, is
  refused.
- Old builds are cleaned up at the next offer: every build folder that is
  neither the current nor the previous one, including
  `Program Files\Midview` once it's neither. Best effort: a folder something
  still runs from is left for next time.

## What identifies a version: the build manifest

- `Gerar-Pacote.ps1` writes `programa\aluno\midview-build.json`:
  `{"build": "2026-09-26 19:40 a1b2c3d", "updateProtocol": 1, "files": [...]}`.
  `build` is a readable label (date and git commit); `files` lists every
  file of the student program.
- `PackageFingerprint`: SHA-256 over the manifest and exactly the files it
  lists (sorted, case-insensitive relative paths; each file's path, length
  and SHA-256). Files not in the list (e.g. a Session log) are ignored; a
  listed file that's missing, or a reparse point, fails.
- The Master fingerprints the package's `programa\aluno`; the Agent
  fingerprints its running build once per start, in the background.
- A package without a manifest, or with `updateProtocol` < 1, can't be pushed:
  "pacote antigo demais para atualizar pela rede".

## Protocol (Agent port 28900, a trusted Master only)

- `UpdateOffer` (96 bytes): a random attempt id (16), the fingerprint (32),
  the zip's size (int64) and SHA-256 (32), the uncompressed total (int64).
  Answered with `UpdateAccepted` (send it), or `CommandAck` + `CommandResult`:
  `(true, "já está nesta versão")` or `(false, reason)`.
- Then `FileChunk`s up to exactly the offered size, answered with one
  `CommandAck` + `CommandResult` as soon as the size and SHA-256 check out:
  `(true, "recebida")` or `(false, reason)`. Extracting and applying then
  happen without the connection, reported through the status.
- `GetUpdateStatus` (empty, or an attempt id) → `UpdateStatus` (UTF-8 JSON):
  the installed build's fingerprint and label (or that it's still being
  computed) and the last attempt `{id, state, detail, updatedAt}`, with state
  `receiving`, `verifying`, `applying`, `provisional`, `rollbackPending`
  (switching back failed; the boot recovery does it), `committed`,
  `rolledBack` or `failed`.
- An Agent from before this answers `CommandAck` to either (its default for
  an unknown command): "Midview anterior à atualização pela rede: atualize
  uma vez com o pendrive".
- Caps: zip ≤ 512 MiB, uncompressed ≤ 1 GiB, ≤ 5000 entries.

## Agent: receiving (`UpdateReceiver`)

On `UpdateOffer`:

- One update at a time: an in-process flag, plus `ProgramData\Midview\
  update\update.lock` opened with `FileShare.None` (Windows releases it if
  the holder dies, the installer scripts can check it). Refused while held:
  "outra atualização já está em andamento". The lock, the record and the
  confirmation live in `ProgramData\Midview\update\`, whose protected ACL
  (set at creation and on every start) lets only SYSTEM and Administrators
  in: the data directory lets Users read, and a student who can open a file
  for reading can hold it open without sharing, which would block the lock
  or the record's replacement forever.
- Refused while the last attempt is unfinished (`applying`, `provisional`,
  `rollbackPending`): "reinicie a máquina".
- Refused when the machine is about to shut down: the schedule is due
  within 20 minutes, or its shutdown (which can start up to 30 minutes
  late, on a machine that woke up after the time) or a delayed
  shutdown/restart from the teacher is still counting down.
- "Já está nesta versão" when the offered fingerprint is the installed one.
- Old builds cleaned up; refused if a folder named after the attempt id
  still exists (only a kept build can: never extracted over); then refused
  unless the drive has zip + uncompressed total + 500 MB free.
- The attempt is recorded (`receiving`) and accepted.

Receiving: into `Midview.update\incoming-<id>.zip`, counting and hashing. A
write failure keeps reading and discarding up to the offered size, then
refuses (a refusal sent mid-stream would be lost to a reset). Short, long or
wrong hash: refused.

Then, without the connection (`verifying`): extract into
`Midview.update\<id>\`. Every entry must be a relative path, with no `..`,
drive, `:` or reserved name, resolving inside the folder; entries and bytes
actually written are capped; every file is flushed. It must be exactly the
manifest's files, include `Midview.Agent.exe` and `Midview.Session.exe`, and
fingerprint to the offered value. Any failure: `failed` with the reason, the
attempt's files deleted.

Then the receiver launches the applier from **its own, running build**
(`<running folder>\Midview.Agent.exe --apply-update`), hands over the lock,
and waits up to 60 s for the record to say `applying`. Otherwise it kills
the applier, waits for it to exit and reads the record again: still
`verifying` means nothing was switched (the applier records `applying`
before touching the service), so the attempt is `failed` and its files
deleted; anything else means the applier got further, and the receiver
starts `--recover-update` to put the old build back.

Every step forward is recorded before it's taken; a state that can't be
written stops the attempt. A reader holds the record only while reading it,
and the swap is retried for 2 s (Windows won't replace an open file), so a
status question never makes a save fail.

## Agent: applying (`--apply-update`, `UpdateApplier`)

Runs from the build that is already installed and working, as SYSTEM, as a
separate process that outlives the service; its parameters come from the
record in `ProgramData\Midview\update\update-state.json` (written as temp file,
flush, move), none on the command line. The whole body is one try/finally
with an overall deadline, so every path ends with the service pointing at a
complete build and running.

1. Take the lock; record `applying` (if that can't be written, stop:
   nothing is switched).
2. Register a one-shot boot task (SYSTEM): `<old build>\Midview.Agent.exe
   --recover-update`.
3. Switch the binary path to the new build (flushed to disk with
   `RegFlushKey`); record `provisional` (if that can't be written, roll
   back now).
4. Restart the service: stop it through the SCM and wait for its process to
   exit (killed after 60 s); end every `Midview.Session` (in all logon
   sessions, a leftover one keeps port 5901); start it (already started by
   the SCM's own restart-on-failure is fine). SCM restarts during this can
   only start a complete build: the path was switched first.
5. Confirmation: the new Agent, when a trusted Master asks for the status of
   **this attempt** while it's `provisional`, writes
   `ProgramData\Midview\update\update-confirmed.json` (students can't
   open that folder).
   The applier commits when that appears within 5 minutes and the service
   kept the same process the whole time. A crash (new process, or stopped)
   or no confirmation: roll back.
6. Commit: `committed`, the boot task deleted, the old build kept as the
   previous one. Roll back: switch the path back, restart the same way,
   check the old build stays up for 30 s, record `rolledBack` with the
   reason, delete the boot task. If switching back fails: record
   `rollbackPending` and keep the boot task.

The boot task is deleted only once a final state is on disk; until then
the next boot finishes the job (a commit that can't be recorded is then
rolled back: safe, and the teacher can update again).

At boot, `--recover-update` (from the old build): if the last attempt isn't
`committed`, `rolledBack` or `failed`, switch back to the old build, restart
the service and record "interrompida; voltou para a versão anterior". The
task deletes itself once that's recorded (or there was nothing to do); if
it fails, it stays for the next boot.

This makes a machine safe through a power cut, a scheduled shutdown or an
applier crash at any step. A new build that doesn't start, can't serve a
trusted Master (e.g. trusts nobody, TLS broken), crashes, or doesn't know
the update protocol never confirms and is rolled back. Trade-off: a fix to
the applier itself only takes effect one update later.

`ProgramData\Midview` (identity, trusted Masters, schedule) is never
touched: thumbprints don't change, and an update doesn't change which
teachers a machine accepts (that still takes the USB installer).

## Agent: related changes

- `Program.cs` dispatches on the first argument; an unknown one is logged
  and exits with an error instead of starting a second, console-mode Agent.
- The listener retries every few seconds when it can't start (e.g. a
  student's program holds port 28900 while the service was stopped),
  instead of giving up until the next restart.
- Anything a new build needs is done by the Agent at startup and is safe to
  repeat (as the data directory and firewall rules already are); a network
  update never runs the installer.
- The flags `--apply-update` and `--recover-update`, the record's JSON, the
  lock path and the status payloads are a contract between versions.

## Installer and uninstaller

- `Instalar-Aluno.ps1` waits (up to 2 minutes) while an update holds the
  lock, sets the service's path through the SCM (`Win32_Service.Change`;
  a plain registry write isn't seen until a reboot), then deletes the boot
  task, the update record and `Program Files\Midview.update`.
- `Desinstalar-Aluno.ps1` waits for the lock the same way, runs
  `--uninstall-cleanup` from the exe the service actually points at
  (falling back to `-Destino`), and removes `Midview.update`, the boot task
  and the update record too.

## Master: `UpdateWindow`

From "Instalar nos alunos...", after choosing the package folder: "5.
Atualizar pela rede" → "Atualizar as máquinas pela rede...".

- Shows the package's build label, read again before every check and
  update (the package may have been rebuilt in the same folder); the build
  pushed must be the one the machines were checked against. "Verificar versões" asks every machine:
  "já nesta versão", "instalada: 20/09 (a1b2c3) → pacote: 26/09 (d4e5f6)",
  "mais nova que o pacote", offline, certificate refused, "Midview anterior
  (atualize uma vez com o pendrive)", "sem a identidade do Agent (importe a
  ficha de novo)". Port 28900 always uses the Agent's thumbprint, never the
  Session's.
- "Atualizar": checks versions first, then confirms with what will happen:
  the students can keep working; the teacher loses view and control of
  those machines for 1 to 3 minutes; keyboard/mouse locks are lifted; open
  messages and chats close; don't turn the machines off until it finishes;
  the teachers each machine accepts don't change. An extra confirmation
  when a machine has a newer build than the package.
- One machine first; the others only after it commits, at most 4 transfers
  at a time, each polled on its own every 5 s with its attempt id until its
  own attempt is `committed` (✓), `rolledBack`/`failed` (✗ with the reason),
  or the deadline (the applier's limits plus a margin, about 10 minutes)
  passes ("ainda sem resposta — use Verificar versões depois"). After the
  upload, a refused or dropped connection means "reiniciando...", not an
  error. The first rollback stops new machines from starting.
- "Parar" starts no new machines (those already applying finish). Closing
  the window mid-run asks first and warns that machines not yet confirmed
  go back to their previous version. The temporary zip is always deleted.

## Security

- Who: only trusted Masters (unchanged trust list and TLS pinning).
- In transit: TLS; the zip's SHA-256 and the fingerprint bind the applied
  files to the offer and catch a corrupt transfer.
- No code signing yet: a compromised Master, or a tampered package folder,
  can push anything, as a tampered USB stick can today. Archive checks
  (contained paths, caps) protect against a corrupt package, not against
  the trusted sender.
- Students can't take part: the build folders, the record, the
  confirmation and the lock live where standard users can't write, and the
  confirmation requires a trusted Master's authenticated request.

## Out of scope

The first install over the network, updating an Agent older than this
feature, updating the teacher's Master (Instalar-Professor does that), code
signing, scheduled or partial (delta) updates, changing the trust list.
