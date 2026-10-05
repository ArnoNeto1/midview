# Midview — Core Architecture & Remote Screen Engine (Design)

Date: 2026-09-21
Status: Implemented 2026-09-21 (partly validated live: viewing confirmed, plan
Task 16 Steps 1–3; remote control, the input lock and its heartbeat unlock,
Steps 4–7, not yet recorded as checked — and the lock has since moved into the
Agent, 2026-09-23)

## Context

Treezeview (the existing project at `C:\Users\Neto\Desktop\Treezeview`) is a
GPLv2 fork of Veyon, built for a Brazilian public school's computer lab. Its
copyleft license means it can never be relicensed as closed-source or sold
under an exclusive license — any derivative of GPL code must stay GPL, and
relicensing would require consent from every original Veyon copyright holder,
which is not practical.

Midview is a from-scratch rewrite, sharing no source code with Veyon or
Treezeview, so that its owner holds full copyright and can license it however
they choose in the future (including closed-source or commercial). It will
eventually reach feature parity with Treezeview: remote screen view/control
with input locking, site/content blocking, broadcast text messages, private
chat, closing all programs, and file distribution.

This document covers only the first sub-project: the core process
architecture and the remote screen view/control engine, since it is both the
foundation everything else depends on and by far the hardest single piece to
build. Later sub-projects (site blocking, messaging, chat, file distribution,
close-programs) each get their own design doc once this foundation exists.

## Requirements established

- **Platform**: Windows only. No Linux support, now or planned.
- **Network**: local school LAN only. No internet access, no NAT traversal,
  no relay infrastructure.
- **Remote screen quality**: smooth, video-like updates during live
  view/control (comparable to the current Veyon-based experience) — not an
  occasional-snapshot experience.
- **Licensing goal**: zero reused GPL code. Third-party dependencies must be
  permissively licensed (MIT/BSD/Apache-2.0) or, if LGPL (e.g. a UI toolkit),
  usable in a closed-source product via dynamic linking.
- **Team size**: effectively solo development (the project owner, working
  with Claude Code). Favor approaches that are simple to build and maintain
  over approaches that would need a larger team.

## Approach chosen

**Language/runtime: C#/.NET.** Chosen over C++/Qt (familiar from Treezeview,
but slower and riskier to build solo, and still requires writing screen
capture/input injection from scratch in raw Win32 either way) and over Rust
(safer and faster, but a much steeper learning curve and smaller Windows
desktop ecosystem for a solo effort). .NET is MIT-licensed with no relinking
obligations at all, has first-class, well-documented Windows APIs for
everything this project needs, and is the most productive option for a small
team.

## Architecture

Three processes, replacing Veyon's four (service, server, worker, master):

- **MidviewAgent** — Windows Service, always running as SYSTEM. Installs
  itself, starts at boot, and launches `MidviewSession` inside whichever
  desktop session a user is logged into. Supervises `MidviewSession` and
  restarts it if it crashes. Later sub-projects (site blocking) add their
  logic here, since that work needs no desktop access.
- **MidviewSession** — one process per interactive user session, launched by
  `MidviewAgent`. Owns screen capture, input injection, the input-lock
  mechanism, and the network listener that accepts connections from
  `MidviewMaster`. Replaces Veyon's server+worker split: with .NET's
  async/task model, concurrent features are handled safely within one
  process instead of needing separate OS processes per feature.
- **MidviewMaster** — the teacher's console GUI. For this sub-project: a
  minimal computer list plus a view/control window, enough to validate the
  full pipeline end-to-end. The fuller console UI (matching Treezeview's
  toolbar-based layout, which the project owner wants to keep as a design
  reference) is built out alongside later sub-projects' features.

**Resolved:** checked the Interception driver's actual license files
(`licenses/non-commercial-usage/LGPL 3.0.txt` and a separate
`licenses/commercial-usage/` folder containing paid-license documents, in
`github.com/oblitum/Interception`) — it is dual-licensed: free under LGPL 3.0
for non-commercial use only, and requires purchasing a commercial license for
commercial use. Since selling this software is an explicit goal, and buying a
third-party license is a business decision outside an engineer's authority to
make unilaterally, Midview does not use the Interception driver.
`WindowsInputDeviceController` (see the implementation plan) instead
disables/enables the keyboard and mouse device classes via the public,
no-license-required `SetupDi*` Win32 APIs (Device Manager's own
enable/disable mechanism) — weaker than a live driver-level filter, but
requires no third-party dependency at all. It runs in `MidviewAgent`, not
`MidviewSession`, since those APIs need administrator rights (see "Input
lock ownership" under Error handling & robustness).

## Network protocol & remote screen pipeline

- **Transport**: TCP with TLS (via .NET's built-in `SslStream`). No relay or
  NAT traversal needed since it's LAN-only.
- **Authentication**: the teacher's console holds a private key; each
  `MidviewAgent`/`MidviewSession` trusts its public key — the same trusted-key
  concept Treezeview already uses for site-block commands, reimplemented
  independently.
- **Message framing**: a simple custom length-prefixed binary protocol
  (message type + payload). Not VNC/RFB-compatible on purpose — no need for
  compatibility with generic VNC clients, so a simpler protocol is
  sufficient and easier to build correctly.
- **Screen capture**: Windows Graphics Capture API.
- **Change-only updates**: each new frame is diffed against the previous one
  by tile; only changed tiles are JPEG-encoded and sent, not the whole
  screen — this is what makes smooth updates achievable without needing a
  full video codec.
- **Input events**: mouse/keyboard actions in the `MidviewMaster` control
  window are serialized as small messages and applied on the student side via
  the Windows `SendInput` API. The same message path also drives the
  input-lock mechanism validated in Treezeview.

## Error handling & robustness

Directly informed by the stuck-input-lock bug chased at length in
Treezeview, where an unlock message could fail to arrive or be silently
dropped for several different reasons:

- **Heartbeat-based auto-unlock**: `MidviewSession` expects a periodic
  heartbeat from a connected `MidviewMaster` while input is locked. If no
  heartbeat arrives for a short timeout (network drop, teacher's PC
  freezing, any cause at all), it unlocks automatically — this does not
  depend on any specific "goodbye" message being sent or received correctly,
  closing off this entire class of bug regardless of its cause.
- **Process crash recovery**: if `MidviewSession` crashes while input is
  locked, `MidviewAgent` detects the crash and restarts `MidviewSession`.
  **Correction (2026-09-23):** the original assumption here, that "the OS
  releases the resources it held", doesn't hold for the device-disable lock
  Midview actually uses: disabling a device is persistent system state, not
  a process resource, and it survives reboots too. See "Input lock
  ownership" below.
- **Input lock ownership (2026-09-23)**: disabling devices needs
  administrator rights, and `MidviewSession` runs as the logged-in student
  (a standard user), so it can't do it itself — every call silently failed
  and the lock never locked anything. `MidviewAgent` (SYSTEM) now owns
  `WindowsInputDeviceController`; `MidviewSession` asks for lock/unlock over
  a local named pipe (`\\.\pipe\MidviewInputLock`, one request byte, one
  reply byte — `InputLockPipe` in `Midview.Protocol`). The heartbeat logic
  stays in `MidviewSession`'s `InputLockManager`. The Agent re-enables all
  keyboard/mouse devices (1) as soon as the pipe connection of the Session
  holding the lock closes, however it closed (crash, kill, logoff); (2) every
  time it starts, which is at boot, before anyone can log in — so a power cut
  mid-lock can't leave the logon screen without a keyboard or mouse; and
  (3) when the service stops. The client checks that the pipe's server runs
  in session 0 (services only), so a student's process squatting the pipe
  name while the Agent restarts isn't mistaken for the Agent.
- **Service supervision**: `MidviewAgent` supervises `MidviewSession` the same
  way for any crash, not just ones related to input-lock.

## Testing approach

- Protocol logic (message framing, tile-diff computation, encoding) gets
  ordinary automated unit tests — none of it depends on a real screen or
  network connection.
- Screen capture, input injection, and the input-lock mechanism can only be
  meaningfully validated with live testing on real hardware (the project
  owner's PC and the "Aluno-25" test machine), the same way Treezeview's
  equivalent features were validated.

## Out of scope for this sub-project

Everything other than the core architecture and remote screen engine: site
blocking, text messages, private chat, close-all-programs, file
distribution/collection, and the full console UI beyond a minimal
view/control window. Each gets its own design doc once this foundation is
built and working.
