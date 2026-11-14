package io.github.arnoneto1.midview.agent.power

import io.github.arnoneto1.midview.agent.protocol.PowerSchedule
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId
import java.time.ZonedDateTime

// When the teacher's "Agendar" turns the screen off, in the phone's own
// local time (the schedule's minutes are each device's local time, as on a
// PC). Days and times go through java.time, so a change to or from summer
// time moves nothing: a time that does not exist that day (the clocks jump
// over it) is the moment after the jump, and one that comes twice is the
// first.
object ScreenOffTimes {
    // A phone that was asleep through the time (an alarm Android put off)
    // and rings shortly after still turns its screen off; much later it
    // doesn't (ScheduledShutdown.MaxLateness on a PC).
    val MAX_LATENESS: Duration = Duration.ofMinutes(30)

    // The first scheduled time strictly after now; null when the schedule
    // turns nothing off (no time, or no day).
    fun next(schedule: PowerSchedule?, now: Instant, zone: ZoneId): Instant? {
        if (schedule == null || !schedule.turnsScreenOff) return null
        val today = now.atZone(zone).toLocalDate()
        // Today again next week, for a single day whose time has passed.
        for (offset in 0L..7L) {
            val at = at(schedule, today.plusDays(offset), zone) ?: continue
            if (at.isAfter(now)) return at
        }
        return null
    }

    // The scheduled time now answers: at or before now, at most
    // MAX_LATENESS ago (yesterday's too, for an alarm that rang past
    // midnight); null when none.
    fun dueAt(schedule: PowerSchedule?, now: Instant, zone: ZoneId): Instant? {
        if (schedule == null || !schedule.turnsScreenOff) return null
        val today = now.atZone(zone).toLocalDate()
        return listOf(today, today.minusDays(1))
            .mapNotNull { at(schedule, it, zone) }
            .firstOrNull { !it.isAfter(now) && Duration.between(it, now) <= MAX_LATENESS }
    }

    private fun at(schedule: PowerSchedule, date: LocalDate, zone: ZoneId): Instant? {
        val minute = schedule.shutdownMinute ?: return null
        if (!schedule.isScheduledOn(date.dayOfWeek)) return null
        return ZonedDateTime.of(date, LocalTime.of(minute / 60, minute % 60), zone).toInstant()
    }
}

// Follows the schedule (PowerScheduleStore) on the phone: one alarm at the
// next time (arm; null cancels it), set again after it rings, when the
// teacher sends a schedule, at every start of the agent (after the phone
// restarts, too) and when the clock or the time zone changes. When it rings,
// the screen goes off and locks as for the teacher's "Desligar"
// (screenOff, PowerCountdown): with the student warned for
// SHUTDOWN_WARNING_SECONDS while the screen is on, at once while it is off
// (nobody is looking, and a warning would only sound in the drawer).
// No Android classes: now and zone are the clock.
class ScheduledScreenOff(
    private val store: PowerScheduleStore,
    private val arm: (Instant?) -> Unit,
    private val screenOff: (delaySeconds: Int) -> Pair<Boolean, String>,
    private val screenOn: () -> Boolean,
    // Calls off the warning screenOff started (PowerCountdown.cancelScheduled);
    // true when one was counting down.
    private val cancelScreenOff: () -> Boolean = { false },
    private val now: () -> Instant = Instant::now,
    private val zone: () -> ZoneId = ZoneId::systemDefault,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    private val gate = Any()

    // The scheduled time last acted on: an alarm that rings twice for it
    // (a start of the agent catching a ring up) acts once.
    private var lastDone: Instant? = null

    // The teacher sent a schedule: the screen off it started for a time no
    // longer in it (cleared, or another time) is called off, the student's
    // warning with it; then the alarm follows the new schedule.
    fun changed() {
        val running = synchronized(gate) { lastDone }
        if (running != null && ScreenOffTimes.dueAt(store.current, now(), zone()) != running && cancelScreenOff()) {
            log("The schedule changed: its screen off for $running was called off.", null)
        }
        rearm()
    }

    // The alarm at the next time, or none.
    fun rearm() {
        val at = ScreenOffTimes.next(store.current, now(), zone())
        try {
            arm(at)
        } catch (e: RuntimeException) {
            log("Couldn't set the power schedule's alarm", e)
        }
    }

    // The alarm rang: the screen goes off if a scheduled time is due, then
    // the alarm is set for the next one.
    fun rang() {
        val due = ScreenOffTimes.dueAt(store.current, now(), zone())
        val act = due != null && synchronized(gate) { (due != lastDone).also { if (it) lastDone = due } }
        if (act) {
            val delay = if (screenOn()) PowerSchedule.SHUTDOWN_WARNING_SECONDS else 0
            val (done, detail) = try {
                screenOff(delay)
            } catch (e: RuntimeException) {
                log("The scheduled screen off failed", e)
                false to (e.message ?: e.javaClass.simpleName)
            }
            log("Scheduled screen off for $due: $done, $detail", null)
        }
        rearm()
    }
}
