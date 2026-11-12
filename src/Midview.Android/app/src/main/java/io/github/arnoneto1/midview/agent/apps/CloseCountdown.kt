package io.github.arnoneto1.midview.agent.apps

// The student's warning before "Fechar programas" closes the apps, as the
// PC's countdown banner: with a warning time, the notice counts down
// ("O professor vai fechar os apps em 0:59. Salve o que está fazendo.") and
// the apps close when it is up (AppCloser); "Agora" closes at once and says
// so for a moment. The teacher gets the answer right away, as from a PC: the
// closing happens on its own. A new command while one counts down replaces
// it. The pick between asking and forcing has no meaning on a phone: the
// apps always close.
// Under the teacher's lock screen the countdown runs all the same (the
// student was warned for the whole time); at its end the AppCloser waits for
// the lock to go, as for "Agora", and the notice goes away.
// No Android classes: notice draws it (CloseNotice), schedule runs a task
// later on one thread, now is the clock in milliseconds.
class CloseCountdown(
    private val closer: AppCloser,
    private val notice: CloseNotice,
    private val schedule: (delayMillis: Long, task: () -> Unit) -> Unit,
    private val now: () -> Long = System::currentTimeMillis,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    private val gate = Any()

    // Every request gets the next one; a task of an older one does nothing.
    private var generation = 0
    private var deadline = 0L

    // The countdown's first notice alerts (comes over the app); the
    // following ones only change the time.
    private var alerted = 0

    // The CommandAck's result.
    fun request(delaySeconds: Int): Pair<Boolean, String> {
        if (delaySeconds <= 0) {
            val gen = synchronized(gate) { ++generation }
            return closeAndSay(gen)
        }
        if (!closer.canClose()) return false to AppCloser.NOT_DEVICE_OWNER
        val gen = synchronized(gate) {
            deadline = now() + delaySeconds * 1000L
            ++generation
        }
        tick(gen)
        return true to "aluno avisado; os apps fecham em ${delayText(delaySeconds)}"
    }

    private fun tick(gen: Int) {
        synchronized(gate) {
            if (gen != generation) return
            val left = deadline - now()
            if (left > 0) {
                notice.show(headline(left), running(left), alert = alerted != gen)
                alerted = gen
                // The next change of the clock (it reads whole seconds, rounded up).
                val next = left % 1000L
                schedule(if (next == 0L) 1000L else next) { tick(gen) }
                return
            }
        }
        closeAndSay(gen)
    }

    // Closes, then says it for a moment; nothing is said when the closing
    // waits for the lock screen or failed.
    private fun closeAndSay(gen: Int): Pair<Boolean, String> {
        val result = closer.close()
        val (closed, detail) = result
        log("Apps closed after the warning: $closed, $detail", null)
        synchronized(gate) {
            // A newer command has the notice now.
            if (gen != generation) return result
            if (!closed || detail == AppCloser.AFTER_LOCK) {
                notice.hide()
            } else {
                notice.show(DONE_HEADLINE, DONE, alert = true)
                schedule(DONE_LINGER_MILLIS) { synchronized(gate) { if (gen == generation) notice.hide() } }
            }
        }
        return result
    }

    companion object {
        const val DONE = "O professor fechou os apps."
        const val DONE_HEADLINE = "Apps fechados"

        // How long "fechou" stays up, to be read.
        const val DONE_LINGER_MILLIS = 8_000L

        // Short, for the top line of the notification, which is cut early.
        fun headline(leftMillis: Long) = "Os apps fecham em ${clock(leftMillis)}"

        fun running(leftMillis: Long) = "O professor vai fechar os apps em ${clock(leftMillis)}. Salve o que está fazendo."

        // "1:05", rounded up: it reads 0:00 only once the time is really up
        // (as the PC's CountdownText.Clock).
        fun clock(leftMillis: Long): String {
            val seconds = maxOf(0L, (leftMillis + 999L) / 1000L)
            return "${seconds / 60}:${(seconds % 60).toString().padStart(2, '0')}"
        }

        // "30 segundos", "1 minuto", "1 minuto e 30 segundos" (CloseAppsCommand.DelayText).
        fun delayText(delaySeconds: Int): String {
            val minutes = delaySeconds / 60
            val seconds = delaySeconds % 60
            val minutesText = if (minutes == 1) "1 minuto" else "$minutes minutos"
            val secondsText = if (seconds == 1) "1 segundo" else "$seconds segundos"
            return when {
                minutes == 0 -> secondsText
                seconds == 0 -> minutesText
                else -> "$minutesText e $secondsText"
            }
        }
    }
}

// What the student sees: one notice at a time, replaced by each show,
// until hide. headline is the short line, text the whole; alert asks for the
// notice to come over the app again (otherwise only its words change).
interface CloseNotice {
    fun show(headline: String, text: String, alert: Boolean)

    fun hide()
}
