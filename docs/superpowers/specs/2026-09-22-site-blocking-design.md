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

Unlike `MidviewSession`, which generates a fresh identity certificate every
run (acceptable there since the teacher reconnects interactively each time),
`MidviewAgent` is a long-running background service the teacher configures
once. It generates its identity certificate once and persists it to disk
(`%ProgramData%\Midview\agent-identity.pfx`), reusing the same certificate
across restarts so the teacher never has to re-enter a new thumbprint after
a service restart or reboot.

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

## Out of scope

Domain categories/lists beyond a flat set of currently-blocked domains,
scheduling, a "list currently blocked domains" query feature (not
requested), and any UI beyond `MidviewMaster` being able to send the four
commands.

## Review notes (2026-09-22, after implementation)

Checked against the implemented code. Items marked **open** are gaps in the
current implementation, not yet fixed.

- **Fixed: startup reconcile now works in both directions.** When
  `InternetBlocked` is false, `ReconcileAtStartup` checks whether any
  `MidviewSiteBlock_*` rule exists (`IFirewallController.IsBlockPresent`,
  via `netsh ... show rule`) and runs `ApplyUnblock` if so. Only checking
  for leftover rules, instead of always unblocking, avoids resetting the
  machine's default outbound policy on every boot. Remaining gap: if the
  rules were removed by hand but the block-outbound policy was left
  behind, this isn't detected.
- **Fixed: startup errors no longer stop the listener.** Hosts-file and
  firewall reconcile each run in their own try/catch, log through
  `AgentLog`, and the listener starts either way. An unreadable
  `site-block-state.json` is renamed to `site-block-state.json.corrupt`
  and the Agent starts with nothing blocked.
- **Open: domain payload is not validated or normalized.** The Master
  sends the raw text box contents and the Agent writes them straight into
  the hosts file. This means: a newline in the payload injects arbitrary
  hosts entries; `YouTube.com` and `youtube.com` are different set entries
  (unblocking one leaves the other); a trailing space or a pasted URL
  (`https://youtube.com/watch?...`) blocks nothing; an empty payload writes
  `127.0.0.1 ` and `127.0.0.1 www.`. The Agent should trim, lowercase,
  strip any scheme/path/port, and reject anything that isn't a valid
  hostname.
- **Fixed: data-directory permissions.** `AgentDataDirectory.EnsureSecure`
  now enforces the ACL on every Agent start, not only when it creates the
  directory: SYSTEM and Administrators get full control, Users get
  read-only (so `MidviewSession`, running as a standard user, can read
  `trusted-masters.json`). `agent-identity.pfx` gets its own protected
  ACL (SYSTEM and Administrators only) right after it's loaded or
  created. `AgentLog` no longer calls `EnsureSecure` and never throws.
- **Minor: firewall rules pile up.** `netsh ... add rule` doesn't check for
  duplicates, so every Agent start adds another `MidviewAgentInbound`
  rule, and every block (including each startup reapply) adds another set
  of `MidviewSiteBlock_*` rules. Delete-by-name removes all copies, so
  it's harmless but untidy.
- **Minor: LAN allow rule assumes `192.168.0.0/16`.** On a lab network in
  another range (e.g. `10.x`), the LAN isn't reachable during a full
  block. Midview's own ports still work.
- **Minor: outbound allow on ports 28900/5901 applies to any remote
  host.** During a full block, a student could reach an internet server
  listening on either port. Scoping `remoteip` to the local subnet would
  close this.

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
