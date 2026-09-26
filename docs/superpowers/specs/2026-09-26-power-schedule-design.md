# Midview — Scheduled Power On and Off (Design)

Date: 2026-09-26
Status: Implemented 2026-09-26 (not yet validated live)

## Context

The teacher can shut the lab down (`2026-09-24-shutdown-design.md`) and turn
it on (`2026-09-26-wake-on-lan-design.md`) by hand. This lets them set a
timetable once — e.g. on at 07:00 and off at 18:00, Monday to Friday — and
have it happen without anyone doing anything.

## Design

- **One timetable for the lab** (`PowerSchedule`, in Protocol): the days
  (one bit per `DayOfWeek`), a wake time and a shutdown time (minutes after
  midnight, each optional), and `SavedAt`, when the teacher saved it. The
  same days apply to both. Times are each computer's own local time.
- **Shutdown: each machine does it on its own** (`ScheduledShutdown`,
  `ScheduledShutdownWorker`, in the Agent). The Agent keeps the timetable
  in `ProgramData\Midview\power-schedule.json` and checks the clock every
  20 s. When the shutdown time falls between two checks on a scheduled day,
  it runs `shutdown /s /f /t 300` with a "Desligamento programado" warning,
  the same forced shutdown as the teacher's, 5 minutes' notice. So the lab
  turns off whether or not the teacher's PC is on.
  - A machine turned on after the time doesn't shut down straight away (the
    first check compares with when the Agent started); one that was asleep
    through the time and wakes within 30 minutes still does.
  - During the warning, "Desligar a turma..." > "Cancelar desligamento"
    calls it off, as it does the teacher's own.
  - An unreadable file means no schedule: not shutting down is the safe
    side.
- **Power on: the teacher's PC does it** (`WakeTask`, in the Master). A
  machine that's off can't wake itself. Saving a wake time registers a
  Windows scheduled task, for the teacher's own account (no administrator):
  at the wake time on the chosen days it runs `Midview.Master.exe
  --ligar-turma 07:00`, which sends the classroom list's magic packets (as
  `WakeWindow` does, without a window) and exits. The task is set to wake
  the PC from sleep and to run late if the PC was off; the Master only
  wakes the lab when it's at most 1 hour late, so turning the PC on in the
  afternoon doesn't. Every run leaves a line in
  `%LOCALAPPDATA%\Midview\ligar-turma.log`, which the schedule window shows.
  So the teacher's PC must be on, or asleep, with the teacher logged in to
  Windows, at the wake time. Registering uses `schtasks /Create /XML`, with
  the definition built by `WakeTask.BuildXml`.
- **Getting the timetable to the machines.** `MessageType.SetPowerSchedule`
  (13-byte payload) gives it to an Agent; it answers with what it will do
  ("desliga sozinha às 18:00 (seg a sex)"). The newest `SavedAt` wins, so
  two teachers' Masters don't keep overwriting each other: an Agent keeps a
  newer timetable than the one it's sent, and says so. Saving sends it to
  every machine in the list at once; each tile's poller also sends it the
  first time it sees the machine on and again after each change, so a
  machine that was off when the teacher saved gets it later on its own.
- **Master UI:** "Agendar ligar/desligar..." (toolbar) opens
  `ScheduleWindow`: the days, "Ligar às" and "Desligar às" (each with a
  checkbox and a time, e.g. 07:00), what each needs, and "Salvar
  agendamento". It saves `PowerScheduleFile`
  (`%LOCALAPPDATA%\Midview\power-schedule.json`), sets up the wake task
  and sends the timetable, then shows one line for the wake task and one
  per machine. Unticking both and saving clears the timetable everywhere.
  `App` now opens `MainWindow` itself, so that `--ligar-turma` can run
  without one.

## Out of scope

Different days for turning on and off, more than one timetable (e.g. per
machine or per shift), waking from a machine other than the teacher's PC,
and holidays.

## Testing

- `PowerScheduleTests` (Protocol): round trip, no times, wrong length, a
  time past midnight, an eighth day, the days, parsing and formatting
  times, describing the days.
- `ScheduledShutdownTests` (Agent): when it's due (between two checks, not
  before or again after, not on a day off, not straight after being turned
  on late, asleep through it, across midnight, no shutdown time); `Check`
  runs `shutdown.exe` with the scheduled warning; the timetable survives a
  restart; an older one is kept out; a newer one without a shutdown clears
  it; an invalid request; an unreadable file; `SetPowerSchedule` through a
  real loopback `AgentServer`.
- Master: `WakeTaskTests` (the task XML; creating from a Unicode file;
  Windows refusing; removing an existing task, or nothing when there's
  none; when a late run still wakes the lab; the command-line run and its
  log line; too late sends nothing; and, on Windows only, registering and
  removing a real task), `PowerScheduleFileTests`, and a `ThumbnailPoller`
  test against a fake Agent (sent once, and again when it changes).
- The scheduled shutdown and wake on real machines are live-tested only.
