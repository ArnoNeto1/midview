package io.github.arnoneto1.midview.agent.power

import io.github.arnoneto1.midview.agent.protocol.PowerSchedule
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId

class ScheduledScreenOffTest {
    @get:Rule val folder = TemporaryFolder()

    // No summer time: always UTC-3.
    private val saoPaulo = ZoneId.of("America/Sao_Paulo")
    private val berlin = ZoneId.of("Europe/Berlin")

    private val weekdays18h = PowerSchedule(1000, PowerSchedule.WEEKDAYS, 7 * 60, 18 * 60)

    // 2026-11-13 is a Friday.
    private fun sp(text: String): Instant = OffsetDateTime.parse("$text-03:00").toInstant()

    private fun next(schedule: PowerSchedule?, now: Instant, zone: ZoneId = saoPaulo) = ScreenOffTimes.next(schedule, now, zone)

    private fun due(schedule: PowerSchedule?, now: Instant, zone: ZoneId = saoPaulo) = ScreenOffTimes.dueAt(schedule, now, zone)

    @Test fun nextIsLaterToday() =
        assertEquals(sp("2026-11-13T18:00:00"), next(weekdays18h, sp("2026-11-13T10:00:00")))

    @Test fun nextSkipsTheWeekend() {
        // At the time itself it is already the next one.
        assertEquals(sp("2026-11-16T18:00:00"), next(weekdays18h, sp("2026-11-13T18:00:00")))
        assertEquals(sp("2026-11-16T18:00:00"), next(weekdays18h, sp("2026-11-13T19:00:00")))
        assertEquals(sp("2026-11-16T18:00:00"), next(weekdays18h, sp("2026-11-15T23:59:00")))
    }

    @Test fun aSingleDayWhoseTimePassedIsNextWeek() {
        val wednesdays = PowerSchedule(1, 0b0001000, null, 12 * 60)
        assertEquals(sp("2026-11-25T12:00:00"), next(wednesdays, sp("2026-11-18T12:00:01")))
        assertEquals(sp("2026-11-18T12:00:00"), next(wednesdays, sp("2026-11-18T11:59:59")))
    }

    @Test fun nextCrossesMidnight() {
        val halfPastMidnight = PowerSchedule(1, PowerSchedule.EVERY_DAY, null, 30)
        assertEquals(sp("2026-11-14T00:30:00"), next(halfPastMidnight, sp("2026-11-13T23:50:00")))
    }

    @Test fun nothingToTurnOffHasNoNextTime() {
        assertNull(next(null, sp("2026-11-13T10:00:00")))
        assertNull(next(weekdays18h.copy(shutdownMinute = null), sp("2026-11-13T10:00:00")))
        assertNull(next(weekdays18h.copy(days = 0), sp("2026-11-13T10:00:00")))
    }

    @Test fun summerTimeKeepsTheWallClock() {
        val sixPm = PowerSchedule(1, PowerSchedule.EVERY_DAY, null, 18 * 60)
        // Saturday 18:00 in winter time, Sunday 18:00 in summer time: 23 hours apart.
        assertEquals(
            Instant.parse("2026-03-29T16:00:00Z"),
            next(sixPm, Instant.parse("2026-03-28T17:00:00Z"), berlin),
        )
        assertEquals(
            Instant.parse("2026-10-25T17:00:00Z"),
            next(sixPm, Instant.parse("2026-10-24T16:00:00Z"), berlin),
        )
    }

    @Test fun aTimeTheClocksJumpOverIsTheMomentAfter_AndOneThatComesTwiceIsTheFirst() {
        val halfPastTwo = PowerSchedule(1, PowerSchedule.EVERY_DAY, null, 2 * 60 + 30)
        // 02:00 -> 03:00: 02:30 does not exist; 03:30 summer time it is.
        assertEquals(Instant.parse("2026-03-29T01:30:00Z"), next(halfPastTwo, Instant.parse("2026-03-29T00:00:00Z"), berlin))
        // 03:00 -> 02:00: 02:30 comes twice; the first, still in summer time.
        assertEquals(Instant.parse("2026-10-25T00:30:00Z"), next(halfPastTwo, Instant.parse("2026-10-24T23:00:00Z"), berlin))
    }

    @Test fun dueAtTheTimeAndForHalfAnHourAfter() {
        assertEquals(sp("2026-11-13T18:00:00"), due(weekdays18h, sp("2026-11-13T18:00:00")))
        assertEquals(sp("2026-11-13T18:00:00"), due(weekdays18h, sp("2026-11-13T18:30:00")))
        assertNull(due(weekdays18h, sp("2026-11-13T18:30:01")))
        assertNull(due(weekdays18h, sp("2026-11-13T17:59:59")))
        // Not on a Saturday.
        assertNull(due(weekdays18h, sp("2026-11-14T18:05:00")))
        assertNull(due(null, sp("2026-11-13T18:00:00")))
    }

    @Test fun dueCrossesMidnight() {
        val tenToMidnight = PowerSchedule(1, PowerSchedule.WEEKDAYS, null, 23 * 60 + 50)
        // Friday's, rung on Saturday.
        assertEquals(sp("2026-11-13T23:50:00"), due(tenToMidnight, sp("2026-11-14T00:10:00")))
    }

    // The follower, with a clock of its own and every call kept.
    private var clock = sp("2026-11-13T10:00:00")
    private var screenIsOn = true
    private val armed = mutableListOf<Instant?>()
    private val offs = mutableListOf<Int>()
    private val logged = mutableListOf<String>()
    private val store by lazy { PowerScheduleStore(File(folder.root, "power-schedule.txt"), changed = { follower.rearm() }) }
    private val follower: ScheduledScreenOff by lazy {
        ScheduledScreenOff(
            store,
            arm = { armed += it },
            screenOff = { offs += it; true to "ok" },
            screenOn = { screenIsOn },
            now = { clock },
            zone = { saoPaulo },
            log = { text, _ -> logged += text },
        )
    }

    @Test fun aScheduleTakenArmsTheAlarm_AndAClearedOneCancelsIt() {
        store.handle(weekdays18h.encode())
        assertEquals(listOf(sp("2026-11-13T18:00:00")), armed)
        store.handle(PowerSchedule(2000, PowerSchedule.WEEKDAYS, null, null).encode())
        assertEquals(listOf(sp("2026-11-13T18:00:00"), null), armed)
    }

    @Test fun theAlarmTurnsTheScreenOffWithTheWarning_AndArmsTheNextTime() {
        store.handle(weekdays18h.encode())
        clock = sp("2026-11-13T18:00:02")
        follower.rang()
        assertEquals(listOf(PowerSchedule.SHUTDOWN_WARNING_SECONDS), offs)
        assertEquals(sp("2026-11-16T18:00:00"), armed.last())
    }

    @Test fun withTheScreenOffItGoesOffAtOnce() {
        store.handle(weekdays18h.encode())
        clock = sp("2026-11-13T18:00:02")
        screenIsOn = false
        follower.rang()
        assertEquals(listOf(0), offs)
    }

    @Test fun aTimeIsActedOnOnce() {
        store.handle(weekdays18h.encode())
        clock = sp("2026-11-13T18:00:02")
        follower.rang()
        clock = sp("2026-11-13T18:01:00")
        follower.rang()
        assertEquals(1, offs.size)
    }

    @Test fun anAlarmWithNothingDueOnlyArmsAgain() {
        store.handle(weekdays18h.encode())
        // Rang far too late (the phone was off), and on a day without the time.
        clock = sp("2026-11-13T19:00:00")
        follower.rang()
        clock = sp("2026-11-14T18:00:00")
        follower.rang()
        assertTrue(offs.isEmpty())
        assertEquals(sp("2026-11-16T18:00:00"), armed.last())
    }

    @Test fun aRefusedAlarmIsLogged() {
        val refusing = ScheduledScreenOff(
            store, arm = { throw SecurityException("no alarms") }, screenOff = { true to "ok" }, screenOn = { true },
            now = { clock }, zone = { saoPaulo }, log = { text, _ -> logged += text },
        )
        store.handle(weekdays18h.encode())
        refusing.rearm()
        assertEquals(listOf("Couldn't set the power schedule's alarm"), logged)
    }
}
