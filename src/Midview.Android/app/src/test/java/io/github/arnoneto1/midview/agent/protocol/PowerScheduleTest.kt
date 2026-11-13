package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.DayOfWeek

class PowerScheduleTest {
    // As src/Midview.Protocol/PowerSchedule.cs encodes it: SavedAt 1000,
    // Monday to Friday, no wake time, shutdown at 18:00.
    private val weekdays18h = byteArrayOf(0, 0, 0, 0, 0, 0, 0x03, 0xE8.toByte(), 0x3E, 0xFF.toByte(), 0xFF.toByte(), 0x04, 0x38)

    @Test fun decodesTheMastersPayload() {
        val schedule = PowerSchedule.decode(weekdays18h)
        assertEquals(PowerSchedule(1000, PowerSchedule.WEEKDAYS, null, 18 * 60), schedule)
        assertArrayEquals(weekdays18h, schedule!!.encode())
    }

    @Test fun roundTripsEveryField() {
        val schedule = PowerSchedule(1_790_000_000_123, PowerSchedule.EVERY_DAY, 7 * 60 + 30, 23 * 60 + 59)
        assertEquals(schedule, PowerSchedule.decode(schedule.encode()))
        val none = PowerSchedule(0, 0, null, null)
        assertEquals(none, PowerSchedule.decode(none.encode()))
    }

    @Test fun rejectsWhatIsNotASchedule() {
        assertNull(PowerSchedule.decode(ByteArray(0)))
        assertNull(PowerSchedule.decode(weekdays18h.copyOf(12)))
        assertNull(PowerSchedule.decode(weekdays18h + 0))
        // A day bit past Saturday.
        assertNull(PowerSchedule.decode(weekdays18h.copyOf().also { it[8] = 0x80.toByte() }))
        // 24:00 is not a minute of the day.
        assertNull(PowerSchedule.decode(weekdays18h.copyOf().also { it[11] = 0x05; it[12] = 0xA0.toByte() }))
    }

    @Test fun daysFollowTheWireWithSundayFirst() {
        val sundayOnly = PowerSchedule(0, 0b0000001, null, 0)
        assertTrue(sundayOnly.isScheduledOn(DayOfWeek.SUNDAY))
        assertFalse(sundayOnly.isScheduledOn(DayOfWeek.MONDAY))
        val weekdays = PowerSchedule(0, PowerSchedule.WEEKDAYS, null, 0)
        assertTrue(DayOfWeek.values().filter { it != DayOfWeek.SATURDAY && it != DayOfWeek.SUNDAY }.all(weekdays::isScheduledOn))
        assertFalse(weekdays.isScheduledOn(DayOfWeek.SATURDAY))
        assertFalse(weekdays.isScheduledOn(DayOfWeek.SUNDAY))
    }

    @Test fun turnsTheScreenOffOnlyWithATimeAndADay() {
        assertTrue(PowerSchedule(0, PowerSchedule.WEEKDAYS, null, 0).turnsScreenOff)
        assertFalse(PowerSchedule(0, PowerSchedule.WEEKDAYS, 7 * 60, null).turnsScreenOff)
        assertFalse(PowerSchedule(0, 0, null, 18 * 60).turnsScreenOff)
    }

    @Test fun describesDaysAsTheMasterDoes() {
        assertEquals("seg a sex", PowerSchedule.describeDays(PowerSchedule.WEEKDAYS))
        assertEquals("todos os dias", PowerSchedule.describeDays(PowerSchedule.EVERY_DAY))
        assertEquals("seg, qua e sex", PowerSchedule.describeDays(0b0101010))
        assertEquals("sáb e dom", PowerSchedule.describeDays(0b1000001))
        assertEquals("qua", PowerSchedule.describeDays(0b0001000))
        assertEquals("nenhum dia", PowerSchedule.describeDays(0))
        assertEquals("07:05", PowerSchedule.formatTime(7 * 60 + 5))
    }
}
