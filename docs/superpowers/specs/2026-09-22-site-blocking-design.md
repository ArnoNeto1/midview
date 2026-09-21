# Midview — Site Blocking (Design)

Date: 2026-09-22
Status: Approved for planning

## Context

Second feature sub-project for Midview, built on the already-implemented
core architecture (`Midview.Protocol`, `MidviewAgent`, `MidviewSession`,
`MidviewMaster`). Reimplements Treezeview's site-blocking feature — the
teacher blocks or unblocks specific domains on a student machine — as
independent code, sharing no source with Treezeview (a GPL fork).

## Requirement established

Replicate Treezeview's current site-blocking behavior exactly: block/unblock
specific domains by teacher command, no scheduling, no categories. No new
requirements beyond what already exists.

## Architecture

Site blocking must keep working even when no one is logged into the student
machine (e.g. blocking a domain before the student logs in), so the command
path cannot go through `MidviewSession` (which only exists while a user is
logged in). Instead, `MidviewAgent` gets its own TCP+TLS listener, on a
separate port from `MidviewSession`'s, reusing the same
`Midview.Protocol.SecureChannel`/`TrustStore`/`MessageFramer` machinery
already built — no new protocol layer, just a second listener.

Unlike `MidviewSession`, which generates a fresh identity certificate every
run (acceptable there since the teacher reconnects interactively each time),
`MidviewAgent` is a long-running background service the teacher configures
once. It generates its identity certificate once and persists it to disk
(`%ProgramData%\Midview\agent-identity.pfx`), reusing the same certificate
across restarts so the teacher never has to re-enter a new thumbprint after
a service restart or reboot.

## Components

- **New protocol messages** (`Midview.Protocol.MessageType`): `BlockSite`,
  `UnblockSite`, each carrying a domain name as payload.
- **`AgentIdentity`** (`MidviewAgent`): loads the persisted certificate from
  disk, generating and saving one on first run.
- **`SiteBlockState`** (`MidviewAgent`): persists the current set of blocked
  domains to `%ProgramData%\Midview\blocked-domains.json`, so it can be
  reapplied if the Agent restarts or the hosts file is reset by something
  else (antivirus, a Windows update).
- **`HostsFileManager`** (`MidviewAgent`): adds/removes domain entries in
  `C:\Windows\System32\drivers\etc\hosts`. Expands every domain to both its
  bare and `www.`-prefixed forms (blocking `example.com` also blocks
  `www.example.com` and vice versa), and flushes the DNS cache
  (`ipconfig /flushdns`) after every change so blocks take effect
  immediately.
- **`FirewallController`** (`MidviewAgent`): adds/removes a Windows Firewall
  rule blocking the domain's resolved IP addresses as a second layer,
  since hosts-file blocking alone can be bypassed by a browser that ignores
  the hosts file (e.g. one using DNS-over-HTTPS) or by connecting to the IP
  directly.
- **`AgentServer`** (`MidviewAgent`): listens on its own port, authenticates
  incoming Master connections via the same trust-store pattern as
  `MidviewSession`'s `SessionServer`, and handles `BlockSite`/`UnblockSite`
  messages by updating `HostsFileManager`, `FirewallController`, and
  `SiteBlockState` together.
- **`MidviewMaster`** gains a way to send a `BlockSite`/`UnblockSite` command
  to an Agent and get an acknowledgement back — a short-lived connection per
  command (connect, send, get ack, disconnect), unlike the long-lived
  view/control connection to a Session.

## Data flow

Teacher issues a block/unblock command from `MidviewMaster` → short-lived
TLS connection to the target's `MidviewAgent` → Agent updates
`HostsFileManager` (hosts file + DNS flush), `FirewallController` (firewall
rule), and `SiteBlockState` (persisted JSON) together → sends an
acknowledgement back → connection closes.

At `MidviewAgent` startup (service start, restart after a crash, or a full
reboot), it loads `SiteBlockState` from disk and reapplies every entry to
`HostsFileManager` and `FirewallController` — this is what makes a block
survive a reboot or recover from something else resetting the hosts file,
mirroring the same persist-and-reconcile pattern already used for the input
lock in the core sub-project.

## Error handling

- **Reconciliation at startup** (described above) is the primary defense
  against the hosts file or firewall rules getting reset by something
  outside Midview's control.
- **Firewall rule failures** (e.g. a malformed domain, or the Windows
  Firewall service being unavailable) are logged but don't block the hosts
  file change from still being applied — hosts-file blocking alone is
  still useful even if the firewall layer fails for one domain.
- **Unblock always removes both layers**, even if the corresponding block
  only partially succeeded when it was first applied (e.g. hosts file
  entry written but firewall rule failed) — unblock shouldn't require
  remembering which layer actually succeeded.

## Testing

- Domain expansion (bare ↔ `www.`), `SiteBlockState` JSON persistence, and
  the `AgentServer` message handling logic are all ordinary unit tests, no
  real hosts file or firewall involved.
- Actually modifying the hosts file and creating firewall rules can only be
  meaningfully validated live, on a real machine. Lower risk than the input
  lock (a bad rule doesn't lock anyone out of anything), but still real
  system state — do this live, with the project owner present, same as the
  input lock validation.

## Out of scope

Everything other than domain block/unblock: scheduling, categories, a
"list currently blocked domains" query feature (not requested), and any
UI beyond `MidviewMaster` being able to send the two commands.
