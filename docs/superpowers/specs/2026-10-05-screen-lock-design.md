# Midview — Screen Lock (Design)

Date: 2026-10-05
Status: Implemented 2026-10-05 (not yet validated live)

## Context

The teacher locks the students' screens so they pay attention: on the whole
class or on one machine. Asked for during the first live run; the look was
chosen by the owner and drawn from scratch for Midview.

## Design

- **What the student sees:** one window over every monitor, dark grey
  (64, 64, 64), with a flat light-grey (220, 220, 220) padlock in the middle
  of each screen, no text (`WinFormsScreenLock`). The padlock is drawn, not
  an image, so it stays sharp at any resolution: 5 wide by 6 tall, 28 % of
  the screen's height, the lower half a solid body and the upper half an
  arch half a unit thick. The window is topmost, out of the taskbar and
  Alt+Tab, and refuses to be closed by the user (Alt+F4); Windows shutting
  down still closes it. "Fechar programas" leaves it alone like Midview's
  other windows. The classroom grid's thumbnails show it, so the teacher
  sees at a glance which machines are locked.
- **Keyboard and mouse are locked** for as long, through the same input
  lock as control (see the change below for how it locks). `InputLockManager` now has separate holders:
  control (StartLock/StopLock, kept up by heartbeats, with the "sendo
  controlado" strip) and one hold per screen-lock connection
  (HoldForScreenLock/ReleaseScreenLock). The devices come back only when
  none holds them, so stopping control never undoes a screen lock or the
  other way round, and a control heartbeat timeout never ends a screen
  lock. The strip is for control only: a screen lock shows no strip.
- **Changed after the first live runs (same day):** on ALUNO-25 (PS/2
  keyboard and mouse) the mouse locked but the keyboard didn't, and after
  unlocking the mouse stayed disabled until the machine was restarted --
  not even the Agent restarting during a network update brought it back.
  Windows applies some device changes on such machines only after a
  restart, while reporting success. So input is no longer locked by
  disabling the devices at all: the Session swallows physical keystrokes
  and mouse input with low-level hooks (`LowLevelInputBlocker`), for the
  screen lock and for control alike. Input the Session itself injects while
  the teacher controls the machine is marked injected by Windows and goes
  through. The hooks work with any keyboard and mouse, come off at once,
  and go with the Session however it ends, so a lock can never outlive it.
  The Agent's input-lock pipe is no longer used; its re-enabling of every
  keyboard and mouse at startup stays, for machines that older versions
  left disabled. The taskbar only steps behind a full-screen window that is the
  active one, so the lock window makes itself active (joining the active
  window's input for a moment, as a background program can't otherwise)
  and checks every second that it's still in front. Ctrl+Alt+Del can't be
  blocked without a driver: from its screen the student can still sign
  out, which ends the lock with the session.
- **Protocol:** `MessageType.LockScreen`, the first message of a connection
  that lasts as long as the lock, never taking the control slot. The
  Session holds the input lock, shows the lock screen and answers one
  `CommandAck` ("tela bloqueada"), or refuses ("esta máquina não bloqueia a
  tela", "o teclado e o mouse não puderam ser travados"). The Master then
  sends a `Heartbeat` every 3 s. Several connections may lock one machine
  (two teachers, a reconnect overlapping the old connection): the screen
  stays locked until the last one ends.
- **Never leaves a class locked:** the lock ends with its connection,
  however it ends. "Desbloquear" and the teacher's program closing close it
  at once; a teacher's PC that stops answering is dropped by the Session's
  15 s receive timeout; restarting the student's machine releases
  everything, as the Agent re-enables the devices at every start.
- **Locked again when back:** the Master keeps one `ScreenLockKeeper` per
  locked machine. If its connection is lost (the machine restarted, the
  student logged off and on, the network dropped), it reconnects every 3 s
  and locks again, so restarting is no way out while the teacher keeps the
  class locked. A machine that refuses is retried every 30 s.
- **Master:** "Bloquear telas" and "Desbloquear telas" in the toolbar act
  on the whole class; "Bloquear a tela" and "Desbloquear a tela" in a
  tile's menu on one machine. The status line sums it up: "Telas
  bloqueadas: 12 de 14. Não bloqueou: Aluno-03 (Offline)."
- **Who got out** (added after the live run found Ctrl+Alt+Del still
  works): each locked tile shows "🔒 Tela bloqueada" under its name; one
  that was locked and isn't anymore shows "⚠ Saiu do bloqueio (…)" in red,
  with the reason (signed out: "Sem usuário logado"; restarting:
  "Offline"), until its keeper locks it again; the status line adds
  "Saíram do bloqueio: …" (`ScreenLockText`). Ctrl+Alt+Del itself can't be
  blocked by a program: only a keyboard filter driver such as Interception
  can, which Midview declined for its commercial license (see the core
  design, 2026-09-21). From its screen the student can only end the lock by
  signing out, switching user or restarting, and is locked again on the
  way back in; going back to the desktop finds the lock still on, and Task
  Manager opens behind the lock with input still locked.
- **Older Sessions** take `LockScreen` for a control connection and start
  sending their screen; the Master reports "Midview antigo: sem bloqueio de
  tela" and tries again every 30 s, until the machine is updated.

## Out of scope

A text on the lock screen, locking at a scheduled time,
locking machines added to the grid after the class was locked, and keeping
a class locked after the teacher's program closes.

## Testing

- `InputLockManagerTests`: a screen lock keeps the devices disabled when
  control stops, and control when a screen lock ends; each screen lock is
  released on its own; a heartbeat timeout ends control but not a screen
  lock.
- `ControlNoticeTests`: the strip follows control, not a screen lock.
- `SessionServerTests`: a lock covers the screen and locks input until its
  connection closes; it stays on while control starts and stops; two locks
  keep the screen locked until both end; without a lock screen it's refused
  without locking input.
- `ScreenLockKeeperTests` (Master, over a real loopback `SessionServer`):
  the screen stays locked until the keeper is disposed, past several
  heartbeats; a machine that comes back is locked again; a machine that
  can't lock its screen says why.
- Checked by rendering the lock window to an image on a two-monitor PC: a
  padlock in the middle of each screen. Live-tested only: the window over
  real desktops, topmost over students' programs, and the 15 s unlock when
  the teacher's PC dies.
