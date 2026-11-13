package io.github.arnoneto1.midview.agent.power

import io.github.arnoneto1.midview.agent.identity.StoredText
import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.PowerSchedule
import java.io.File
import java.io.IOException
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter

// The phone's side of the teacher's "Agendar" (SetPowerSchedule), as the PC
// Agent's ScheduledShutdown (src/Midview.Agent/ScheduledShutdown.cs): the
// newest savedAt wins, so two teachers' programs don't keep overwriting each
// other's; kept in the app's storage (in the phone's own format), so it
// holds after a restart. changed is told of every schedule taken (the alarm
// is set again: ScheduledScreenOff). zone formats the time a kept schedule
// was saved, for the teacher.
class PowerScheduleStore(
    private val file: File,
    private val changed: () -> Unit = {},
    private val zone: () -> ZoneId = ZoneId::systemDefault,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    private val lock = Any()

    // Null: none was ever taken (or the file could not be read).
    @Volatile var current: PowerSchedule? = load()
        private set

    // From a teacher this phone trusts (the CommandHandler checked).
    fun handle(payload: ByteArray): Pair<Boolean, String> {
        val schedule = PowerSchedule.decode(payload) ?: return false to INVALID
        synchronized(lock) {
            val held = current
            if (held != null && schedule.savedAt < held.savedAt) {
                val savedAt = DateTimeFormatter.ofPattern("dd/MM HH:mm").format(Instant.ofEpochSecond(held.savedAt).atZone(zone()))
                return true to "$KEPT_NEWER, salvo em $savedAt: ${describe(held)}"
            }
            if (schedule != held) {
                try {
                    file.absoluteFile.parentFile?.mkdirs()
                    writeAtomically(file, encodeStored(schedule))
                } catch (e: IOException) {
                    log("Couldn't save the power schedule", e)
                    return false to NOT_SAVED
                }
                current = schedule
            }
        }
        changed()
        return true to describe(schedule)
    }

    // Unreadable means no schedule (a phone that doesn't turn its screen off
    // on its own is the safe side); a corrupt file is set aside.
    private fun load(): PowerSchedule? {
        if (!file.exists()) return null
        val bytes = try {
            file.readBytes()
        } catch (e: IOException) {
            log("Couldn't read the power schedule, following none", e)
            return null
        }
        return decodeStored(bytes) ?: run {
            log("The power schedule file is corrupt, following none; kept aside as .corrupt", null)
            file.renameTo(File(file.path + ".corrupt"))
            null
        }
    }

    companion object {
        const val INVALID = "pedido inválido"
        const val NOT_SAVED = "não foi possível gravar o agendamento no celular"
        const val KEPT_NEWER = "mantido o agendamento mais novo"
        const val NOTHING_SCHEDULED = "sem desligamento programado"

        // A wake time alone: a phone has nothing to do with it.
        const val WAKE_ONLY = "sem desligamento programado (ligar não é possível no celular)"

        private const val KIND = "power-schedule"

        // What the phone does with it, for the teacher's list of results.
        fun describe(schedule: PowerSchedule): String {
            val minute = schedule.shutdownMinute
            return when {
                minute != null && schedule.turnsScreenOff ->
                    "apaga e trava a tela às ${PowerSchedule.formatTime(minute)} (${PowerSchedule.describeDays(schedule.days)})"
                schedule.wakeMinute != null && schedule.days != 0 -> WAKE_ONLY
                else -> NOTHING_SCHEDULED
            }
        }

        // StoredText "power-schedule": savedAt (Unix seconds), days (the
        // wire's bits), wake and shutdown (minutes after midnight; absent
        // for none).
        fun encodeStored(schedule: PowerSchedule): ByteArray =
            StoredText.encode(
                KIND,
                buildList {
                    add("savedAt" to schedule.savedAt.toString())
                    add("days" to schedule.days.toString())
                    schedule.wakeMinute?.let { add("wake" to it.toString()) }
                    schedule.shutdownMinute?.let { add("shutdown" to it.toString()) }
                },
            )

        // Null for a file that is not this format, or not a valid schedule.
        fun decodeStored(bytes: ByteArray): PowerSchedule? {
            val stored = StoredText.decode(KIND, bytes) ?: return null
            val savedAt = stored.first("savedAt")?.toLongOrNull() ?: return null
            val days = stored.first("days")?.toIntOrNull() ?: return null
            val wake = stored.first("wake")?.let { it.toIntOrNull() ?: return null }
            val shutdown = stored.first("shutdown")?.let { it.toIntOrNull() ?: return null }
            return PowerSchedule(savedAt, days, wake, shutdown).takeIf { it.isValid }
        }
    }
}
