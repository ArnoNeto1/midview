# Midview — Classroom Console (Design)

Date: 2026-09-22
Status: Implemented 2026-09-23 (not yet validated live — see "Implementation
notes" at the end for where the implementation differs from this design)

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

`MidviewSession` used to generate a fresh self-signed certificate on
every launch (`SelfSignedCertificate.Create(Environment.MachineName)` in
`Program.cs`), by original design, since only one interactive control
session at a time was ever in scope. A persisted machine list breaks if
the thumbprint it stored goes stale every time a student's computer
restarts — the exact bug class fixed earlier for the Agent and Master's own
identities. (It also meant the teacher couldn't connect at all once the
Agent auto-launched Session without a console: the new thumbprint was
printed where nobody could see it.)

**Done (2026-09-23).** Session's identity is persisted at
`%ProgramData%\Midview\session-identity.pfx` (`SessionIdentity` in
`Midview.Protocol`). It is **created by the Agent**, not by Session: Session
runs as the logged-in student, a standard user, and `%ProgramData%\Midview`
is read-only for Users (`AgentDataDirectory.EnsureSecure`), so Session can
read the file but never write it. The Agent calls
`SessionIdentity.EnsureExists` on every start, before `SessionSupervisor`
launches Session, and logs the thumbprint to
`%ProgramData%\Midview\agent.log` for the teacher to register. Session
loads it with `SessionIdentity.Load`; if it doesn't exist yet (Session run
by hand on a machine where the Agent never ran), Session falls back to a
temporary certificate and says so on its console.

The private key is readable by the student on purpose: Session runs as that
student, so anything Session can read, the student can too. All it allows is
impersonating that same machine's screen to the teacher.

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
  `SessionServer`, handled by `SessionServer.Run`'s existing
  accept-loop-per-client model exactly like any other pair of concurrent
  clients today. Running both at once means that machine captures a
  small extra low-res frame every few seconds on top of full control's
  own feed — a deliberately accepted minor inefficiency (YAGNI) rather
  than adding coordination logic to pause the preview poll while full
  control is open, unless it proves to actually matter in practice.

## Testing

- `SessionIdentity` gets the same load-or-create test coverage as
  `AgentIdentity`/`MasterIdentity`.
- `KnownMachinesFile` gets the same load/save test coverage as
  `TrustedMastersFile`.
- The `RequestThumbnail`/`ThumbnailFrame` handling in `SessionServer` gets
  a loopback test confirming it responds correctly whether or not a
  control session is concurrently active, mirroring the existing
  `SessionServerTests.cs` pattern.
- The grid UI and per-machine polling loops are WPF-level code with no
  existing test coverage precedent in this project (`ViewControlWindow`
  itself has none) — validated by live testing, not new automated tests,
  consistent with how the rest of the UI layer has been handled.

## Implementation notes (2026-09-23)

Where the implementation differs from, or fills in, the design above:

- **Connection kind is decided by the first message.** The error-handling
  section above assumed `SessionServer` serves any number of concurrent
  clients. Since the stall/timeout fixes, it serves **one controlling
  Master at a time** (the input lock and injected input belong to the
  machine), so a preview connection served the same way would hold that
  slot permanently and full control could never get in. `SessionServer`
  now reads the first message after the TLS handshake: `RequestThumbnail`
  makes it a preview connection (no slot, no capture thread, no
  `FramePipeline`, no `InputLockManager`); anything else is a control
  connection, served exactly as before. `ViewControlWindow` already sends
  `StartControl` first, so the control protocol is unchanged.
- **A click only selects; watching and controlling are separate**
  (changed 2026-10-05, after the first live run: clicking a tile locked
  the student's keyboard and mouse at once, with no way to just look).
  Clicking a tile, left or right, selects it (a highlighted border); the
  toolbar's "Ver a tela" and "Controlar" act on the selected machine, and
  the tile's menu has the same two. Watching opens `ViewControlWindow`
  sending a `Heartbeat` first instead of `StartControl`, which starts the
  screen stream without locking anything, and it sends no input.
  Controlling opens it as before. The window's "Controlar" / "Parar de
  controlar (só ver)" button switches on the same connection with
  `StartControl` / `StopControl`.
- **The student sees when they're controlled** (added 2026-10-05): while
  their keyboard and mouse are locked, a red strip across the top of their
  screen says "Este computador está sendo controlado pelo professor"
  (`WinFormsControlNotice`). It follows control's hold on the lock
  (`InputLockManager` shows and hides it), so it goes however control
  ends: "Parar de controlar", the window closing, the heartbeat timeout,
  the connection dropping; a screen lock, which also locks input, shows no
  strip (`2026-10-05-screen-lock-design.md`). It never takes focus or
  clicks (layered, transparent, no-activate, a tool window kept out of
  Alt+Tab), so the teacher's clicks reach what's under it and "Fechar
  programas" leaves it alone; a notice that fails never affects the lock
  (`ControlNoticeTests`). Watching only shows nothing. The Session needed no change: its control
  connection always streamed from the first message and only locked and
  injected after `StartControl` (`SessionServerTests`: watching only
  neither locks nor injects; watching, then controlling, then watching
  again locks and releases).
- **Capture failure replies with an empty `ThumbnailFrame`** instead of no
  reply. With no reply, the Master's poll can only time out, and a reply
  that arrives late would then pair with the next request — so the Master
  would have to reconnect on every poll while a screen is locked. The
  empty reply keeps requests and replies paired, and lets the tile say
  "Tela indisponível" (machine up, screen not capturable — e.g. Win+L)
  rather than lumping it in with offline.
- **Tile states:** "Conectando..." (first poll), the live thumbnail,
  "Tela indisponível", "Sem usuário logado" (connection refused: the machine
  is on but nothing listens on 5901 — Session only runs inside a login),
  "Offline" (connect timeout / unreachable), "Certificado recusado"
  (either side rejected the other's certificate: wrong Session thumbprint
  here, or this Master missing from the machine's `trusted-masters.json`),
  "Sem resposta" (anything else, e.g. a reply timeout). Any failure clears
  the last thumbnail. Later: "Nome não encontrado na rede" when a machine
  saved by computer name doesn't resolve, and the same statuses (in
  `FailureText`) now also word every window's per-machine results and
  connection errors, instead of Windows' or .NET's own messages; only an
  unexpected error keeps its technical text there.
- **Timing:** poll every 3 s; connect timeout 5 s (Windows takes ~2 s to
  report a refused connection, and "refused" is what distinguishes "nobody
  logged in" from "offline"); reply timeout 10 s. Any failure drops the
  connection and the next poll starts a fresh one. Each machine's poller
  (`ThumbnailPoller`) runs on its own background thread with its own
  persistent connection.
- **Thumbnail size:** fits within 320×180 keeping the screen's aspect ratio
  (a fixed 320×180 would distort 4:3 or 16:10 lab monitors), JPEG
  quality 70, never scaled up (`ThumbnailRenderer`).
- **Roster behavior:** saving a name that already exists (case-insensitive)
  replaces that entry in place instead of adding a second tile; tiles keep
  the order machines were added in. Right-click on a tile: "Testar /
  bloquear sites..." opens `AddMachineWindow` prefilled with that machine
  (its stored host/thumbprint count as already verified, so renaming it or
  adding its Agent thumbprint doesn't require reconnecting — changing host
  or Session thumbprint does), and "Remover da lista da sala" asks for
  confirmation first. This is how the site-blocking controls are "scoped
  to the selected machine".
- **`known-machines.json`** is written with the same temp-file + flush +
  swap as the Agent's `site-block-state.json`; an unreadable file is kept
  aside as `known-machines.json.corrupt` and the grid starts empty, with
  the status bar saying so. Entries missing a required field are dropped
  on load.
- **UI text is Portuguese**, including the manual-entry/site-blocking view
  that moved into `AddMachineWindow` (it was English in the old
  `MainWindow`).
- **Tests:** a new `Midview.Master.Tests` project covers `KnownMachinesFile`
  and `ThumbnailPoller` (loopback against a real `SessionServer`: receives
  thumbnails, keeps polling, wrong thumbprint, nothing listening, machine
  coming online later). Like `Midview.Session.Tests`, it needs Windows
  (WindowsDesktop runtime) to run. The grid and `AddMachineWindow`
  themselves remain live-tested only, as planned.
