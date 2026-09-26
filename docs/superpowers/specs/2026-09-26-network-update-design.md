# Midview — Update Over the Network (Design)

Date: 2026-09-26
Status: Draft (under review)

## Context

Updating the lab today means regenerating the package and walking to every
machine with the USB stick to run `Instalar-Aluno.cmd` again. This lets the
teacher push a new version of the student program (Agent + Session) from
`MidviewMaster` to machines that already have Midview.

Decided with the owner: **update only**. The first install, and the first
update of an Agent from before this feature, still take one visit with the
USB stick. Nothing new is trusted over the network: an update is accepted
only from a Master the machine already trusts (`trusted-masters.json`,
mutual TLS with pinned thumbprints) — the same Masters that can already shut
it down or block its internet. The owner accepted that this extends what a
trusted Master can do, from commands to replacing the program that runs as
SYSTEM.

## Design

### What identifies a version: the package fingerprint

`PackageFingerprint.Compute(directory)` (Protocol): SHA-256 over every file
under the directory, sorted by relative path (ordinal, case-insensitive,
`/` separators, lower-cased), feeding for each file its path, its length and
the SHA-256 of its content. The Master computes it over the package's
`programa\aluno`; the Agent over its own install folder, which the installer
and the update both mirror from that same folder. Equal fingerprints = same
build, whatever the zip's timestamps or the build's version number.

### Protocol (Agent port 28900, a trusted Master only)

- `UpdateOffer` (72 bytes): fingerprint (32), zip size (int64), zip SHA-256
  (32). Answered with either:
  - `UpdateAccepted` (empty): send it; or
  - `CommandAck` + `CommandResult`: `(true, "já está nesta versão")`, or
    `(false, reason)` — an update already under way, not enough disk, too
    big.
- Then `FileChunk`s (`FileTransfer.ChunkSize`) until exactly the offered
  size, answered with one `CommandAck` + `CommandResult`: `(true, "recebida;
  a máquina está se atualizando")` or `(false, reason)`.
- `GetUpdateStatus` (empty) → `UpdateStatus`: the install folder's current
  fingerprint (32) and the last update's outcome (UTF-8, may be empty).
- An Agent from before this answers `CommandAck` `[0]` (an unknown command)
  to either: "Midview anterior à atualização pela rede: atualize uma vez com
  o pendrive".
- Caps: zip ≤ 512 MiB, uncompressed ≤ 1 GiB, ≤ 5000 entries. The student
  program is ~150–200 MB uncompressed.

### Agent: receiving (`UpdateReceiver`, inside `AgentServer`)

- Folders: install folder = the running Agent's own folder (normally
  `C:\Program Files\Midview`); work folder = its sibling `<install>.update`
  (`C:\Program Files\Midview.update`): `incoming.zip`, `new\` (extracted),
  `previous\` (the version being replaced). Both names must start with
  "Midview" (the installer's rule for folders it mirrors into). The work
  folder gets an explicit protected ACL (SYSTEM and Administrators full,
  Users read), so a student can't swap files between verification and
  apply even with a custom install location.
- On `UpdateOffer`: refuse if an update is under way (a named mutex the
  applier holds), if the fingerprint is the install folder's own ("já está
  nesta versão"), if the size is over the cap, or if the drive has less than
  3× the size free.
- Receiving: into a fresh `incoming.zip`, counting bytes and hashing as they
  arrive; a short, long or wrong-hash transfer is refused.
- Extracting into a fresh `new\`: every entry's path must be relative, with
  no `..`, drive, `:` (alternate streams) or reserved device name, and must
  resolve inside `new\`; entry count and actual bytes written are capped.
- `new\` must contain `Midview.Agent.exe` and `Midview.Session.exe`, and its
  fingerprint must equal the offered one (binding what's applied to what was
  offered, and making `UpdateStatus` match after the restart).
- Then: launch the applier, delete `incoming.zip`, answer success.

### Agent: applying (`Midview.Agent.exe --apply-update`, `UpdateApplier`)

Launched by the Agent (so as SYSTEM) from `new\Midview.Agent.exe` — never
from the install folder it replaces — as a separate process that outlives
the service. It holds the update mutex until it exits.

1. Stop the `MidviewAgent` service (≤ 60 s); end any `Midview.Session` /
   `Midview.Agent` process running from the install folder. (Stopping the
   Agent already re-enables keyboard and mouse.)
2. Move the install folder to `previous\` (a rename on the same drive;
   retried for a few seconds while handles close). If it can't, restart the
   service on the old version and record the failure.
3. Copy `new\` into the install folder. On failure: remove the partial
   folder, move `previous\` back, restart the service.
4. Start the service and check its health: Running within 30 s, port 28900
   accepting connections within 60 s, still Running after that.
5. Healthy: record success. Not healthy: stop it, remove the install folder,
   move `previous\` back, start the service, record "a versão nova não
   iniciou; voltou para a anterior".

The outcome goes to `ProgramData\Midview\update-result.json`, which
`GetUpdateStatus` reports. `previous\` stays until the next update.
`ProgramData\Midview` (identity, trusted Masters, state) is never touched,
so thumbprints don't change and nothing needs importing again.

### Master: `UpdateWindow`

From "Instalar nos alunos...", after choosing the package folder: "5.
Atualizar pela rede" → "Atualizar as máquinas pela rede...":

- the package and its build (first hex digits of the fingerprint);
- "Verificar versões": each machine's `UpdateStatus` → "já nesta versão",
  "versão diferente", offline, "Midview anterior (atualize uma vez com o
  pendrive)";
- "Atualizar" (after a confirmation: the Midview on those machines restarts,
  whoever is using them loses the screen for about a minute; do it outside
  class): zip `programa\aluno` once (temp file), then at most 4 machines at a
  time: offer, send (per-machine progress), then ask `GetUpdateStatus` every
  5 s for up to 3 minutes until the fingerprint matches ("✓ atualizada"),
  the last outcome reports a failure ("✗ … voltou para a versão
  anterior"), or time runs out.
- Machines that were offline are simply run again later.

## Security

- Who: only trusted Masters (unchanged trust list and TLS pinning).
- In transit: TLS; plus the zip's SHA-256 and the fingerprint catch a
  truncated or corrupt transfer and bind the applied files to the offer.
- No code signing yet: a compromised Master, or a tampered package folder,
  can push anything — as a tampered USB stick can today.
- A hostile archive can't write outside `new\`, fill the disk or plant
  files for later: contained paths, caps, protected work folder.
- It can't brick a machine: a new version that doesn't start is rolled
  back automatically; the USB installer still works on any state.

## Out of scope

The first install over the network, updating an Agent older than this
feature, updating the teacher's Master (Instalar-Professor does that), code
signing, scheduled or partial (delta) updates.
