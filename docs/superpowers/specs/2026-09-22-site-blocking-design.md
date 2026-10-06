# Midview — Site Blocking (Design)

Date: 2026-09-22
Status: Implemented and validated (end-to-end on Aluno-25/Aluno_23: domain
block/unblock, full-internet block/unblock, and reboot-survival reconciliation
all confirmed working)

## Context

Second feature sub-project for Midview, built on the already-implemented
core architecture (`Midview.Protocol`, `MidviewAgent`, `MidviewSession`,
`MidviewMaster`). Reimplements Treezeview's site-blocking feature as
independent code, sharing no source with Treezeview (a GPL fork).

**Revision note:** the first draft of this spec incorrectly assumed
Treezeview's firewall layer blocks a specific domain's resolved IP, as a
second layer alongside the hosts file. Reading the actual
`FirewallController.cpp`/`HostsFileManager.cpp` showed these are two
independent features, not two layers of the same one:

1. **Full internet block** (`FirewallController`): an all-or-nothing
   toggle — block all outbound traffic except the local network and
   Midview's own control connections. Used for something like "block
   everything during a test."
2. **Specific domain block list** (`HostsFileManager`): a managed set of
   domain names redirected to `127.0.0.1` via the hosts file, each expanded
   to both its bare and `www.`-prefixed form. Used for "block this
   particular site" (e.g. YouTube) while leaving the rest of the internet
   alone.

Confirmed with the project owner: both are wanted, kept as the two separate
features they actually are.

## Requirement established

Replicate Treezeview's current behavior for both features exactly: no
scheduling, no domain categories (the "always blocked category list"
implied by Treezeview's `baseDomains`/`liveDomains` split in
`HostsFileManager::reconcile` is not part of this — Midview keeps a single
flat set of currently-blocked domains, since categories were explicitly
descoped earlier in this project's brainstorming).

## Architecture

Both features must keep working even when no one is logged into the
student machine (e.g. a block applied before the student logs in), so the
command path cannot go through `MidviewSession` (which only exists while a
user is logged in). Instead, `MidviewAgent` gets its own TCP+TLS listener,
on a separate port from `MidviewSession`'s, reusing the same
`Midview.Protocol.SecureChannel`/`TrustStore`/`MessageFramer` machinery
already built — no new protocol layer, just a second listener.

`MidviewAgent` is a long-running background service the teacher configures
once. It generates its identity certificate once and persists it to disk
(`%ProgramData%\Midview\agent-identity.pfx`), reusing the same certificate
across restarts so the teacher never has to re-enter a new thumbprint after
a service restart or reboot. (`MidviewSession` originally generated a fresh
certificate on every run; it now uses a persisted one too, which the Agent
creates — see the review notes below.)

## Components

- **New protocol messages** (`Midview.Protocol.MessageType`):
  `BlockInternet`, `UnblockInternet` (no payload — full on/off toggle), and
  `BlockDomain`, `UnblockDomain` (a domain name as payload — adds/removes
  one domain from the managed block list).
- **`AgentIdentity`** (`MidviewAgent`): loads the persisted certificate from
  disk, generating and saving one on first run.
- **`SiteBlockState`** (`MidviewAgent`): persists both pieces of state to
  `%ProgramData%\Midview\site-block-state.json` — whether the full internet
  block is currently on, and the current set of specifically-blocked
  domains — so both can be reapplied if the Agent restarts or something
  else resets the hosts file or firewall rules.
- **`HostsFileManager`** (`MidviewAgent`): given the current full set of
  blocked domains, rewrites a clearly-marked block within
  `C:\Windows\System32\drivers\etc\hosts` to match — expanding every domain
  to both its bare and `www.`-prefixed forms (blocking `example.com` also
  blocks `www.example.com` and vice versa) — and flushes the DNS cache
  (`ipconfig /flushdns`) afterward so changes take effect immediately. This
  is a full reconcile-to-desired-state operation, not an incremental
  add/remove of hosts file lines: it always rewrites the whole managed
  block from the current complete domain set.
- **`FirewallController`** (`MidviewAgent`): applies or removes the
  full-internet-block firewall rules — allow the local network, allow
  outbound to `MidviewAgent`'s and `MidviewSession`'s own listening ports
  (so Midview itself keeps working while blocked), then block all other
  outbound traffic. Because Windows Firewall evaluates block rules before
  allow rules regardless of order, "block all other" is done by setting
  the profile-wide default outbound policy to block
  (`firewallpolicy blockinbound,blockoutbound`), not by adding a block-all
  rule. Unblocking removes the allow rules and restores Windows' default
  policy (`blockinbound,allowoutbound`); it does not restore whatever
  policy the machine had before, if that was different.
- **`AgentServer`** (`MidviewAgent`): listens on its own port, authenticates
  incoming Master connections via the same trust-store pattern as
  `MidviewSession`'s `SessionServer`, and handles the four message types by
  updating `FirewallController` or (`HostsFileManager` +
  `SiteBlockState`) as appropriate.
- **`MidviewMaster`** gains a way to send any of the four commands to an
  Agent and get an acknowledgement back — a short-lived connection per
  command (connect, send, get ack, disconnect), unlike the long-lived
  view/control connection to a Session.

## Data flow

**Full internet block/unblock:** teacher sends `BlockInternet` or
`UnblockInternet` from `MidviewMaster` → short-lived TLS connection to the
target's `MidviewAgent` → Agent applies/removes the firewall rules via
`FirewallController` → persists the new on/off state in `SiteBlockState` →
acknowledges → connection closes.

**Domain block/unblock:** teacher sends `BlockDomain`/`UnblockDomain` with
one domain name → Agent adds/removes that domain in its in-memory blocked-
domain set → calls `HostsFileManager` to rewrite the hosts file to match
the *entire* current set (not just the one changed domain) → persists the
updated set in `SiteBlockState` → acknowledges → connection closes.

**At `MidviewAgent` startup** (service start, crash restart, or reboot), it
loads `SiteBlockState` and reapplies both pieces of state: reruns
`HostsFileManager` with the persisted domain set, and reapplies the
firewall rules via `FirewallController` if the full block was on — the same
persist-and-reconcile pattern already used for the input lock in the core
sub-project.

## Error handling

- **Reconciliation at startup** (above) is the primary defense against the
  hosts file or firewall rules getting reset by something outside
  Midview's control.
- **`FirewallController` failures** (e.g. the Windows Firewall service
  being unavailable) are reported back to the teacher via the
  acknowledgement rather than silently succeeding — unlike a single failed
  domain block, a failed full-internet-block is exactly the kind of thing
  a teacher needs to know didn't actually happen (e.g. before starting a
  test).
- **Unblocking is always attempted for every rule/entry**, even if the
  corresponding block only partially succeeded when first applied —
  unblock shouldn't require remembering which specific rule succeeded.

## Testing

- Domain expansion (bare ↔ `www.`), the hosts-file reconcile-to-desired-
  state logic, `SiteBlockState` JSON persistence, and `AgentServer`'s
  message handling are all ordinary unit tests, no real hosts file or
  firewall involved.
- Actually modifying the hosts file and creating/removing firewall rules
  can only be meaningfully validated live, on a real machine. The
  full-internet-block is worth extra care live: test it from a second
  machine (Aluno-25) rather than the machine you're working from, since a
  bug there could cut off *all* internet access on whatever machine runs
  it, same reasoning as the input lock's two-machine testing requirement.

## Teacher's window (changed 2026-10-05)

The four buttons first lived in the "Adicionar / testar máquina" window,
one machine at a time and asking for the Agent's thumbprint by hand. They
now have their own `SiteBlockWindow`, opened by "Bloquear sites..." in the
toolbar (the whole class) or in a tile's menu (one machine), like every
other feature: "Bloquear a internet" / "Liberar a internet", and a site
with "Bloquear o site" / "Liberar o site", sent to every chosen machine at
once, with one line per machine of what happened. The Agent's thumbprint
comes from the classroom list ("Importar máquinas instaladas" fills it
in); a machine without one is told to be imported again. The old window
became "Configurações avançadas" (this PC's identity, and a machine added
or edited by hand), at the end of the toolbar.

## Out of scope

Domain categories/lists beyond a flat set of currently-blocked domains,
scheduling, a "list currently blocked domains" query feature (not
requested), and any UI beyond `MidviewMaster` being able to send the four
commands.

## Review notes (2026-09-22, after implementation; updated 2026-09-23)

Checked against the implemented code. The first version of these notes
listed some fixes as done before they were actually committed, and still
listed as open some that were already fixed; this version reflects the code
as of 2026-09-23.

- **Fixed: startup reconcile works in both directions.**
  `ReconcileAtStartup` reapplies the block when `InternetBlocked` is true,
  and otherwise always runs `ApplyUnblock`. Trade-off: that resets the
  machine's default outbound policy to Windows' default
  (`blockinbound,allowoutbound`) on every boot, even when nothing was ever
  blocked. (An earlier draft of these notes described an
  `IsBlockPresent` check to avoid that; it was never implemented.)
- **Fixed: startup errors no longer stop the listener.** The hosts-file and
  firewall reconcile run in their own try/catch, and so does loading
  `site-block-state.json`. An unreadable state file (e.g. truncated by a
  power cut) is renamed to `site-block-state.json.corrupt` and the Agent
  starts with nothing blocked. `SiteBlockState.Save` writes to a temp file,
  flushes it to disk and swaps it in, so a power cut can no longer leave a
  truncated file in the first place.
- **Fixed: domain payload is validated and normalized** (`DomainNormalizer`):
  trimmed, lowercased, scheme/path/query/port stripped, and anything that
  isn't a plain DNS hostname rejected (so a newline can't inject hosts
  entries, and `YouTube.com`/`https://youtube.com/watch?...` are the same
  entry as `youtube.com`).
- **Fixed: data-directory permissions.** `AgentDataDirectory.EnsureSecure`
  runs on every Agent start, not only when it creates the directory:
  SYSTEM and Administrators get full control, Users get read-only (so
  `MidviewSession`, running as a standard user, can read
  `trusted-masters.json` and `session-identity.pfx`). `agent-identity.pfx`
  gets its own protected ACL (SYSTEM and Administrators only) on every
  start. `AgentLog` no longer calls `EnsureSecure` and never throws.
- **Fixed: firewall rules no longer pile up.** `MidviewAgentInbound` (and
  the new `MidviewSessionInbound`, for port 5901 — Session can't add its
  own rule as a standard user) is deleted before being re-added on every
  start, and `ApplyBlock` deletes any existing `MidviewSiteBlock_*` rules
  before adding them.
- **Fixed: LAN allow rule no longer assumes `192.168.0.0/16`** — it uses
  `remoteip=LocalSubnet`.
- **Fixed: outbound allow on ports 28900/5901 is scoped to the local
  subnet**, so a student can't reach an internet server on either port
  during a full block.
- **Fixed: one stalled connection no longer blocks every command.** Any
  machine on the LAN could open a TCP connection to port 28900 and send
  nothing, which blocked the (then single-threaded, timeout-less) listener
  indefinitely — including the teacher's unblock. Each connection is now
  handled on its own task with a 10 s socket timeout, and
  `SiteBlockCommandHandler` serializes the commands themselves.

### Known limitations (by design)

- Only the bare and `www.` forms are blocked. Other subdomains
  (`m.youtube.com`, `music.youtube.com`), alternate domains (`youtu.be`)
  and CDNs stay reachable. The hosts file has no wildcard support.
- Tabs that are already open may keep working after a domain block.
  Browsers keep their own DNS cache and open connections (including QUIC),
  and `ipconfig /flushdns` doesn't clear those.
- Only `127.0.0.1` entries are written. Adding `::1` entries would also
  block the IPv6 route.
- Windows Defender can flag or revert hosts-file changes for some
  well-known domains. Startup reconciliation re-applies them on the next
  Agent start, but not immediately.
