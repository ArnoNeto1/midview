# Midview — Classroom Console (Design)

Date: 2026-09-22
Status: Approved for planning

## Context

Today, `MidviewMaster` only handles one student machine at a time: the
teacher types a host address and two certificate thumbprints by hand into
plain text fields before clicking Connect. That's fine for the manual
testing done so far, but the teacher manages a lab of 25+ computers and
needs to see all of them at a glance and jump into full control of any
one — the model Veyon's own console uses (a grid of live thumbnails,
click one to open full control), which the teacher explicitly asked to
match.

This spec covers the classroom console: a persisted list of known
machines the teacher builds up once, a lightweight low-frequency preview
feed for the grid (distinct from the existing full-fidelity control
feed), and a new main window that replaces today's single-machine manual
entry as Master's primary view.

## Goals

- The teacher registers each machine once (name + host address +
  Session/Agent thumbprints), validated by successfully connecting to it
  first. The list persists across Master restarts.
- The main window shows one thumbnail per registered machine, refreshed
  every few seconds, without materially loading either side's CPU/network
  the way 25 simultaneous full-fidelity control streams would.
- Clicking a thumbnail opens the existing `ViewControlWindow` full-control
  experience unchanged — this spec adds a preview path alongside it, it
  does not change how full control itself works.
- A machine that's offline or unreachable shows a clear placeholder
  instead of a stale or broken image.

## Non-goals

- No auto-discovery of machines on the network (explicitly declined in
  favor of manual registration, matching Veyon).
- No change to the full-control protocol, tile-diff pipeline, or input
  handling built and validated earlier today.
- No multi-monitor or multi-teacher-viewing-the-same-machine handling —
  out of scope until it's actually needed.

## Prerequisite fix: persisted Session identity

`MidviewSession` currently generates a fresh self-signed certificate on
every launch (`SelfSignedCertificate.Create(Environment.MachineName)` in
`Program.cs`), by original design, since only one interactive control
session at a time was ever in scope. A persisted machine list breaks if
the thumbprint it stored goes stale every time a student's computer
restarts — the exact bug class fixed today for the Agent and Master's own
identities.

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

## Architecture

### Machine registry

A new `KnownMachine` record (`Midview.Master`):
`{ Name, HostAddress, SessionThumbprint, AgentThumbprint }` (AgentThumbprint
optional — a machine can be registered for screen viewing without
site-blocking, or vice versa isn't meaningful since Agent is always
running, but Session might not be reachable).

Persisted as a JSON array at `%LOCALAPPDATA%\Midview\known-machines.json`
via a `KnownMachinesFile` (`Load`/`Save`), plain per-user local file — this
list is the teacher's own classroom roster, not a security boundary, so it
doesn't need the ACL treatment `trusted-masters.json` gets.

**Registration flow:** the teacher uses today's existing manual-entry
Connect flow to verify a machine actually works (host address + Session
thumbprint, Connect succeeds), then clicks a new "Save to classroom list"
button, naming it. This reuses the connection the teacher just proved
works instead of a blind data-entry form, and keeps today's manual flow
available for one-off/unregistered machines. The Agent thumbprint field
is captured as-is (whatever the teacher has typed there, if anything) —
saving to the roster doesn't require separately testing a site-blocking
command, since Connect only proves the Session side works.

### Lightweight preview feed

Reusing the full interactive protocol (tile-diffs, ~15 fps, full
resolution) for 25+ simultaneous connections would be far more network
and CPU load than a thumbnail needs. Two new message types
(`Midview.Protocol.MessageType`): `RequestThumbnail`, `ThumbnailFrame`.

- `SessionServer` handles `RequestThumbnail` independently of
  `StartControl`/`isControlling` state — a preview must work even when no
  one is actively controlling that machine. On request, it captures one
  frame (reusing `IScreenCapture`), downscales to a fixed small size (e.g.
  320×180) with `Graphics.DrawImage`, JPEG-encodes once via
  `TileCodec.EncodeTile`-style logic (a single "tile" the size of the
  whole downscaled image, not the 64px tile grid), and replies with one
  `ThumbnailFrame` message. No tile-diffing, no capture thread, no
  heartbeat/input-lock involvement at all — this path never touches
  `InputLockManager` or `FramePipeline`.
- `Midview.Master` keeps one lightweight `SecureChannel` connection open
  per registered machine (not one-shot reconnects — repeating a full TLS
  handshake every few seconds for 25 machines is needless overhead), each
  on its own timer sending `RequestThumbnail` and waiting for the
  `ThumbnailFrame` reply, independent of every other machine's timer.
  Suggested cadence: every 3–5 seconds — frequent enough to feel live,
  far below what would compete with 25 machines' worth of full-control
  traffic if the teacher later opens one of them.
- A machine that fails to connect (offline, unreachable, wrong
  thumbprint) shows a placeholder tile with its name and an
  offline/error indicator instead of a stale image; the polling timer
  keeps retrying at the same cadence rather than giving up permanently.

### Main window

A new grid view (`ItemsControl` + `WrapPanel`, one cell per
`KnownMachine`) becomes Master's primary window. Each cell shows the
machine's name, its latest thumbnail (or an offline placeholder), and
opens `ViewControlWindow` on click using that machine's stored host
address and thumbprints — exactly the connection today's manual Connect
button already builds, just without the teacher re-typing anything.

Today's manual-entry fields (host address, thumbprints, Connect button)
move to a secondary "Add/test a machine" view reachable from the main
grid, rather than disappearing — they're still how a machine gets
validated before being saved to the roster, and still useful for
one-off/unregistered connections. The site-blocking controls
(Block/Unblock Internet/Domain) move there too, scoped to whichever
machine is currently selected/being tested, rather than being permanently
on-screen fields disconnected from the grid.

Right-clicking a tile offers "Detalhes e bloqueio de sites..." (opens the
Add/test view prefilled with that machine, which is what scopes the
site-blocking controls to it) and "Remover da lista". Saving a machine
whose host address is already in the list replaces that entry. An
unreadable `known-machines.json` is moved aside to
`known-machines.json.corrupt` (and reported in the status bar) instead
of crashing Master or being silently overwritten by the next save.

## Error handling

- A `RequestThumbnail` failure (capture error, encode error) on the
  Session side should not crash or disrupt anything else that session is
  doing — same defensive pattern as every other message handler fixed
  today. Logged via the existing `Console.WriteLine` pattern, no reply
  sent; the Master-side poll for that machine simply times out and shows
  the offline placeholder until the next cycle.
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

## Testing

- `SessionIdentity` gets the same load-or-create test coverage as
  `AgentIdentity`/`MasterIdentity`.
- `KnownMachinesFile` gets the same load/save test coverage as
  `TrustedMastersFile`.
- The `RequestThumbnail`/`ThumbnailFrame` handling in `SessionServer` gets
  a loopback test confirming it responds correctly whether or not a
  control session is concurrently active, mirroring the existing
  `SessionServerTests.cs` pattern.
- `KnownMachinesFile` tests live in a new `tests/Midview.Master.Tests`
  project (Master had none). With it in place, `MachinePreviewPoller`
  also gets two loopback tests — a thumbnail from a reachable Session,
  `null` rather than an exception when nothing is listening. The grid UI
  itself (`MainWindow`, `AddMachineWindow`, `MachineTileViewModel`) is
  validated by live testing, consistent with `ViewControlWindow`.
