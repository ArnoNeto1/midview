package io.github.arnoneto1.midview.agent.power

import io.github.arnoneto1.midview.agent.apps.CloseCountdown
import io.github.arnoneto1.midview.agent.apps.CloseNotice

// What the phone does for the teacher's "Desligar" and "Reiniciar" (Device
// Owner only). Android lets an app restart the phone but never turn it off,
// so "Desligar" turns the screen off and locks it: the phone stays on, and
// the agent with it.
interface PhonePower {
    // Whether it may restart the phone: it is the Device Owner.
    fun canRestart(): Boolean

    // Whether it may turn the screen off: the Device Owner, with the
    // force-lock policy (res/xml/device_admin.xml). A phone made Device Owner
    // before that policy was listed reads it at its next restart.
    fun canTurnScreenOff(): Boolean

    // DevicePolicyManager.reboot: throws IllegalStateException during a call.
    fun restart()

    // DevicePolicyManager.lockNow: the screen goes off, and the lock screen
    // (the teacher's PIN, when there is one) is what the student finds.
    fun turnScreenOff()
}

// The teacher's power command on a phone (MessageType.Power, the PC's
// payload): the action and the warning's time. As on a PC, the student is
// warned with a countdown ("O professor vai reiniciar o celular em 0:59.
// Salve o que está fazendo.", the same notice as before closing the apps),
// and when it is up the phone restarts or its screen goes off. The teacher
// gets the answer right away, as from a PC. "Agora" turns the screen off at
// once (the answer says whether Android did it) or restarts a moment after
// the answer, so it reaches the teacher first. Cancel calls off what still
// counts down. A new request while one counts down replaces it (on a PC,
// Windows refuses a second one; here the teacher's last word wins, as for
// "Fechar programas"). Installing Windows updates has no meaning here.
// The teacher's lock screen changes nothing: the countdown runs under it,
// the restart restarts (and the teacher's program locks the phone again
// once its agent is back), and turning the screen off leaves the lock as it
// was underneath.
// No Android classes: notice draws the warning, schedule runs a task later on
// one thread, now is the clock in milliseconds.
class PowerCountdown(
    private val power: PhonePower,
    private val notice: CloseNotice,
    private val schedule: (delayMillis: Long, task: () -> Unit) -> Unit,
    private val now: () -> Long = System::currentTimeMillis,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    private val gate = Any()

    // Every request gets the next one; a task of an older one does nothing.
    private var generation = 0
    private var deadline = 0L

    // What counts down now (null: nothing).
    private var pending: Boolean? = null

    // The countdown's first notice alerts; the following ones only change the time.
    private var alerted = 0

    // The CommandAck's result. action: the PC's PowerAction (0 shut down,
    // 1 restart, 2 cancel).
    fun request(action: Int, delaySeconds: Int): Pair<Boolean, String> = when (action) {
        CANCEL -> cancel()
        RESTART -> start(restart = true, delaySeconds)
        else -> start(restart = false, delaySeconds)
    }

    private fun cancel(): Pair<Boolean, String> {
        val was = synchronized(gate) {
            generation++
            pending.also { pending = null }
        }
        if (was == null) return true to NOTHING_PENDING
        notice.hide()
        return true to if (was) RESTART_CANCELLED else SCREEN_OFF_CANCELLED
    }

    private fun start(restart: Boolean, delaySeconds: Int): Pair<Boolean, String> {
        if (!power.canRestart()) return false to NOT_DEVICE_OWNER
        if (!restart && !power.canTurnScreenOff()) return false to RESTART_FIRST
        val gen = synchronized(gate) {
            deadline = now() + delaySeconds * 1000L
            // A restart "Agora" can still be called off for its moment.
            pending = if (delaySeconds > 0 || restart) restart else null
            ++generation
        }
        if (delaySeconds <= 0) {
            if (!restart) {
                notice.hide()
                return turnScreenOff()
            }
            notice.show(RESTARTING_HEADLINE, RESTARTING, alert = true)
            // After the answer is on the wire.
            schedule(AFTER_ANSWER_MILLIS) {
                synchronized(gate) {
                    if (gen == generation) {
                        pending = null
                        restartNow()
                    }
                }
            }
            return true to RESTARTING_NOW
        }
        tick(gen, restart)
        val time = CloseCountdown.delayText(delaySeconds)
        return true to if (restart) "reinicia em $time" else "apaga a tela em $time (o celular continua ligado)"
    }

    private fun tick(gen: Int, restart: Boolean) {
        synchronized(gate) {
            if (gen != generation) return
            val left = deadline - now()
            if (left > 0) {
                notice.show(headline(restart, left), running(restart, left), alert = alerted != gen)
                alerted = gen
                // The next change of the clock (it reads whole seconds, rounded up).
                val next = left % 1000L
                schedule(if (next == 0L) 1000L else next) { tick(gen, restart) }
                return
            }
            pending = null
            if (restart) {
                notice.show(RESTARTING_HEADLINE, RESTARTING, alert = true)
                restartNow()
                return
            }
        }
        notice.hide()
        val (done, detail) = turnScreenOff()
        log("Screen turned off after the warning: $done, $detail", null)
    }

    private fun restartNow() {
        try {
            power.restart()
        } catch (e: RuntimeException) {
            // A call going on, or Android said no: the phone stays as it is,
            // and the teacher already has the answer.
            log("The restart the teacher asked for failed", e)
            notice.hide()
        }
    }

    private fun turnScreenOff(): Pair<Boolean, String> = try {
        power.turnScreenOff()
        true to SCREEN_OFF
    } catch (e: RuntimeException) {
        log("Turning the screen off failed", e)
        false to SCREEN_OFF_REFUSED
    }

    companion object {
        // PowerAction (src/Midview.Protocol/PowerCommand.cs).
        const val SHUTDOWN = 0
        const val RESTART = 1
        const val CANCEL = 2

        // The wait between the answer and a restart "Agora", for the answer to reach the teacher.
        const val AFTER_ANSWER_MILLIS = 2_000L

        const val RESTARTING_NOW = "reiniciando agora"
        const val SCREEN_OFF = "tela apagada e travada (o celular continua ligado)"
        const val RESTART_CANCELLED = "reinício cancelado"
        const val SCREEN_OFF_CANCELLED = "cancelado: a tela não vai mais apagar"
        const val NOTHING_PENDING = "nada estava agendado neste celular"
        const val NOT_DEVICE_OWNER = "este celular não reinicia nem apaga a tela"
        const val RESTART_FIRST = "este celular só apaga a tela depois de reiniciar uma vez: use \"Reiniciar\""
        const val SCREEN_OFF_REFUSED = "o Android não deixou apagar a tela"

        const val RESTARTING = "O professor está reiniciando o celular."
        const val RESTARTING_HEADLINE = "Reiniciando"

        // Short, for the top line of the notification, which is cut early.
        fun headline(restart: Boolean, leftMillis: Long) =
            if (restart) "O celular reinicia em ${CloseCountdown.clock(leftMillis)}"
            else "A tela apaga em ${CloseCountdown.clock(leftMillis)}"

        fun running(restart: Boolean, leftMillis: Long) =
            if (restart) "O professor vai reiniciar o celular em ${CloseCountdown.clock(leftMillis)}. Salve o que está fazendo."
            else "O professor vai apagar a tela em ${CloseCountdown.clock(leftMillis)}."
    }
}
