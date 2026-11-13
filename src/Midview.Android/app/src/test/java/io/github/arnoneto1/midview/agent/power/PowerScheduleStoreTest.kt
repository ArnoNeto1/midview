package io.github.arnoneto1.midview.agent.power

import io.github.arnoneto1.midview.agent.protocol.PowerSchedule
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.time.ZoneId

class PowerScheduleStoreTest {
    @get:Rule val folder = TemporaryFolder()

    private val utc = ZoneId.of("UTC")
    private var changes = 0
    private val file by lazy { File(folder.root, "power-schedule.txt") }

    private fun store() = PowerScheduleStore(file, changed = { changes++ }, zone = { utc })

    private val weekdays18h = PowerSchedule(1000, PowerSchedule.WEEKDAYS, 7 * 60, 18 * 60)

    @Test fun nothingKeptIsNoSchedule() {
        assertNull(store().current)
    }

    @Test fun takesASchedule_KeepsIt_AndSaysWhatThePhoneDoes() {
        val store = store()
        assertEquals(true to "apaga e trava a tela às 18:00 (seg a sex)", store.handle(weekdays18h.encode()))
        assertEquals(weekdays18h, store.current)
        assertEquals(1, changes)
        // After a restart.
        assertEquals(weekdays18h, store().current)
    }

    @Test fun theNewestWins() {
        val store = store()
        store.handle(weekdays18h.copy(savedAt = 2_000_000_000).encode())
        val older = store.handle(weekdays18h.copy(savedAt = 1_000_000_000, shutdownMinute = 17 * 60).encode())
        assertEquals(
            true to "mantido o agendamento mais novo, salvo em 18/05 03:33: apaga e trava a tela às 18:00 (seg a sex)",
            older,
        )
        assertEquals(2_000_000_000L, store.current?.savedAt)
        assertEquals(1, changes)
        // The same one again changes nothing, and is still taken.
        assertTrue(store.handle(weekdays18h.copy(savedAt = 2_000_000_000).encode()).first)
        // A newer one, even clearing the time, replaces it.
        val cleared = PowerSchedule(2_000_000_001, PowerSchedule.WEEKDAYS, null, null)
        assertEquals(true to PowerScheduleStore.NOTHING_SCHEDULED, store.handle(cleared.encode()))
        assertEquals(cleared, store().current)
    }

    @Test fun aWakeTimeAloneIsNothingForAPhone() {
        val wakeOnly = PowerSchedule(1000, PowerSchedule.WEEKDAYS, 7 * 60, null)
        assertEquals(true to PowerScheduleStore.WAKE_ONLY, store().handle(wakeOnly.encode()))
    }

    @Test fun anInvalidRequestChangesNothing() {
        val store = store()
        assertEquals(false to PowerScheduleStore.INVALID, store.handle(byteArrayOf(1, 2, 3)))
        assertNull(store.current)
        assertEquals(0, changes)
    }

    @Test fun aScheduleThatCannotBeSavedIsNotTaken() {
        // The folder it would go in is a file.
        val blocked = File(folder.root, "blocked").apply { writeText("x") }
        val store = PowerScheduleStore(File(blocked, "power-schedule.txt"), changed = { changes++ })
        assertEquals(false to PowerScheduleStore.NOT_SAVED, store.handle(weekdays18h.encode()))
        assertNull(store.current)
        assertEquals(0, changes)
    }

    @Test fun aCorruptFileIsNoScheduleAndIsSetAside() {
        file.writeText("not a schedule")
        assertNull(store().current)
        assertFalse(file.exists())
        assertTrue(File(file.path + ".corrupt").exists())
    }

    @Test fun theStoredFormatRoundTrips() {
        for (schedule in listOf(weekdays18h, PowerSchedule(5, 0, null, null), PowerSchedule(7, PowerSchedule.EVERY_DAY, null, 0))) {
            assertEquals(schedule, PowerScheduleStore.decodeStored(PowerScheduleStore.encodeStored(schedule)))
        }
        assertNull(PowerScheduleStore.decodeStored("midview-power-schedule 2\nsavedAt=1\ndays=200\n".toByteArray()))
        assertNull(PowerScheduleStore.decodeStored("midview-power-schedule 2\nsavedAt=1\ndays=1\nshutdown=x\n".toByteArray()))
    }
}
