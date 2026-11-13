package io.github.arnoneto1.midview.agent.protocol

import java.time.DayOfWeek
import java.util.Locale

// The lab's power timetable ("Agendar"), as src/Midview.Protocol/PowerSchedule.cs:
// days (bit n set = .NET's DayOfWeek n, bit 0 Sunday), and the minutes after
// midnight to turn on and off, in each device's own local time; null = not
// scheduled. savedAt (Unix seconds, on the teacher's PC) decides between two
// versions: the newest wins.
//
// MessageType.SET_POWER_SCHEDULE's payload (PAYLOAD_LENGTH bytes,
// big-endian): savedAt (int64), days, wakeMinute and shutdownMinute (uint16
// each, 0xFFFF for none). A phone only follows the shutdown, by turning its
// screen off and locking it (no app can turn a phone off, nor on).
data class PowerSchedule(val savedAt: Long, val days: Int, val wakeMinute: Int?, val shutdownMinute: Int?) {
    fun isScheduledOn(day: DayOfWeek): Boolean = days and (1 shl dayBit(day)) != 0

    // Whether there is anything for a phone to do: a time to turn off, on some day.
    val turnsScreenOff: Boolean get() = shutdownMinute != null && days and EVERY_DAY != 0

    val isValid: Boolean
        get() = days and EVERY_DAY.inv() == 0 && isValidMinute(wakeMinute) && isValidMinute(shutdownMinute)

    fun encode(): ByteArray {
        require(isValid) { "invalid power schedule" }
        val payload = ByteArray(PAYLOAD_LENGTH)
        for (i in 0 until 8) payload[i] = (savedAt ushr (56 - 8 * i)).toByte()
        payload[8] = days.toByte()
        putMinute(payload, 9, wakeMinute)
        putMinute(payload, 11, shutdownMinute)
        return payload
    }

    companion object {
        const val PAYLOAD_LENGTH = 13
        const val WEEKDAYS = 0b0111110
        const val EVERY_DAY = 0b1111111

        // How long before the screen goes off the student is warned
        // (PowerSchedule.ShutdownWarningSeconds: a PC warns as long).
        const val SHUTDOWN_WARNING_SECONDS = 5 * 60

        const val MINUTES_PER_DAY = 24 * 60
        private const val NOT_SCHEDULED = 0xFFFF

        // Monday first, the way a school week reads.
        private val WEEK_ORDER = listOf(
            DayOfWeek.MONDAY to "seg", DayOfWeek.TUESDAY to "ter", DayOfWeek.WEDNESDAY to "qua",
            DayOfWeek.THURSDAY to "qui", DayOfWeek.FRIDAY to "sex", DayOfWeek.SATURDAY to "sáb",
            DayOfWeek.SUNDAY to "dom",
        )

        // java.time counts Monday 1 to Sunday 7; the wire, Sunday 0 to Saturday 6.
        fun dayBit(day: DayOfWeek): Int = day.value % 7

        // Null when the payload is not a valid schedule.
        fun decode(payload: ByteArray): PowerSchedule? {
            if (payload.size != PAYLOAD_LENGTH) return null
            var savedAt = 0L
            for (i in 0 until 8) savedAt = (savedAt shl 8) or (payload[i].toLong() and 0xFF)
            val schedule = PowerSchedule(savedAt, payload[8].toInt() and 0xFF, minuteAt(payload, 9), minuteAt(payload, 11))
            return schedule.takeIf { it.isValid }
        }

        fun formatTime(minute: Int): String = String.format(Locale.ROOT, "%02d:%02d", minute / 60, minute % 60)

        // "seg a sex", "todos os dias", "seg, qua e sex", "sáb e dom", as the Master says them.
        fun describeDays(days: Int): String {
            if (days and EVERY_DAY == EVERY_DAY) return "todos os dias"
            val set = WEEK_ORDER.withIndex().filter { (_, entry) -> days and (1 shl dayBit(entry.first)) != 0 }
            if (set.isEmpty()) return "nenhum dia"
            val isRun = set.last().index - set.first().index == set.size - 1
            if (isRun && set.size >= 3) return "${set.first().value.second} a ${set.last().value.second}"
            val names = set.map { it.value.second }
            return if (names.size == 1) names[0] else "${names.dropLast(1).joinToString(", ")} e ${names.last()}"
        }

        private fun isValidMinute(minute: Int?) = minute == null || minute in 0 until MINUTES_PER_DAY

        private fun minuteAt(payload: ByteArray, at: Int): Int? {
            val value = ((payload[at].toInt() and 0xFF) shl 8) or (payload[at + 1].toInt() and 0xFF)
            return if (value == NOT_SCHEDULED) null else value
        }

        private fun putMinute(payload: ByteArray, at: Int, minute: Int?) {
            val value = minute ?: NOT_SCHEDULED
            payload[at] = (value shr 8).toByte()
            payload[at + 1] = value.toByte()
        }
    }
}
