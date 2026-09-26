# Midview — Shut Down and Restart (Design)

Date: 2026-09-24
Status: Implemented 2026-09-24 (not yet validated live)

## Context

At the end of the day, or of a lesson, the teacher shuts down or restarts
the lab from `MidviewMaster`: the whole class or one machine.

## Design

- **The Agent does it, not the Session.** The Agent runs as SYSTEM whether
  or not anyone is logged in, so it can shut down a machine sitting at the
  logon screen (no Session there), and it never depends on the student's
  account having the "shut down the system" right. It's an Agent command
  like site blocking (port 28900, the Agent's thumbprint), so a machine
  needs its Agent thumbprint in the roster. Machines imported from the
  installer always have it; one without it is reported, not attempted.
- **Protocol:** `MessageType.Power`; payload `PowerCommand`: the action
  (`Shutdown`, `Restart` or `Cancel`) and the warning delay in seconds (at
  most 10 minutes). It's answered with one `CommandAck` carrying a
  `CommandResult`. The Agent now answers this command with a detail; the
  site-blocking commands still answer with one byte, the same first byte.
- **How** (`PowerCommandHandler`, `ShutdownCommands`): Windows' own
  `shutdown.exe`, run by the Agent:
  - `/s` (shut down) or `/r` (restart);
  - `/t` with the delay, and `/c` with a Portuguese warning ("O professor
    vai desligar este computador em 1 minuto. Salve seu trabalho agora...")
    that Windows shows the logged-in student with the countdown;
  - `/f`, so when the countdown ends open programs are closed without
    saving. Otherwise one unsaved document would keep an unattended machine
    on all night, stuck at "this app is preventing shutdown";
  - `/d p:0:0`, logged as a planned shutdown.

  `Cancel` is `shutdown /a`. The exit codes become the per-machine result:
  "desliga em 1 minuto", "reiniciando agora", "desligamento cancelado",
  "nenhum desligamento estava agendado", "já há um desligamento agendado
  (cancele antes de pedir outro)", or "o Windows recusou (código N)".
- **Master UI:** "Desligar a turma..." (toolbar) and "Desligar /
  reiniciar..." (a tile's right-click menu) open `PowerWindow`:
  - choose Desligar or Reiniciar, and the wait: warn the student and wait 1
    minute (default), warn and wait 5 minutes, or now without waiting;
  - it asks the teacher to confirm ("Desligar as 12 máquinas da turma em 1
    minuto? O trabalho não salvo... é perdido.");
  - "Cancelar desligamento" calls it off on the same machines during the
    countdown;
  - one line per machine; every machine at once. Errors are worded by
    `FailureText.DescribeAgent`, so a refused connection reads as "O
    Midview não está rodando nessa máquina".
- `ConnectionClient.AgentPort` replaces the Agent port that
  `AddMachineWindow` had its own copy of.

## Out of scope

Logging students off without shutting down, turning machines on (its own
design: `2026-09-26-wake-on-lan-design.md`), scheduled shutdowns (e.g. every
day at 18:00; their own design: `2026-09-26-power-schedule-design.md`), and a
custom warning text.

## Testing

- `PowerCommandTests` (Protocol): round trip, wrong length, unknown
  action, delay past the maximum.
- `PowerCommandHandlerTests` (Agent): the `shutdown.exe` arguments for
  shutting down with a warning, restarting now and cancelling; every exit
  code's result; an invalid request runs nothing; `shutdown.exe` failing to
  start is reported; a `Power` command through a real loopback
  `AgentServer` answers with the handler's result.
- The countdown warning, the forced close and the cancel are live-tested
  only, like `PowerWindow`.
