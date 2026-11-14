package io.github.arnoneto1.midview.agent.power

import io.github.arnoneto1.midview.agent.apps.CloseNotice
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class PowerCountdownTest {
    // A clock and a scheduler the test moves by hand.
    private var clock = 1_000_000L
    private val tasks = mutableListOf<Pair<Long, () -> Unit>>()

    private fun advance(millis: Long) {
        val until = clock + millis
        while (true) {
            val next = tasks.filter { it.first <= until }.minByOrNull { it.first } ?: break
            tasks.remove(next)
            clock = next.first
            next.second()
        }
        clock = until
    }

    // What the student sees, in order ("hide" for a hide).
    private val seen = mutableListOf<String>()
    private val headlines = mutableListOf<String>()
    private val alerts = mutableListOf<Boolean>()
    private val notice = object : CloseNotice {
        override fun show(headline: String, text: String, alert: Boolean) {
            seen += text
            headlines += headline
            alerts += alert
        }
        override fun hide() {
            seen += "hide"
        }
    }

    // What the phone did, in order.
    private val done = mutableListOf<String>()
    private var owner = true
    private var forceLock = true
    private var restartFails: RuntimeException? = null
    private var screenOffFails: RuntimeException? = null
    private val power = object : PhonePower {
        override fun canRestart() = owner
        override fun canTurnScreenOff() = owner && forceLock
        override fun restart() {
            restartFails?.let { throw it }
            done += "restart"
        }
        override fun turnScreenOff() {
            screenOffFails?.let { throw it }
            done += "screen off"
        }
    }
    private val countdown = PowerCountdown(power, notice, schedule = { delay, task -> tasks += (clock + delay) to task }, now = { clock })

    // "Agendar" cleared during its warning: the screen stays on, the warning goes.
    @Test fun theSchedulesOwnWarningIsCalledOff() {
        countdown.requestScheduled(300)
        assertTrue(countdown.cancelScheduled())
        assertEquals("hide", seen.last())
        advance(301_000)
        assertTrue(done.isEmpty())
        assertFalse(countdown.cancelScheduled())
    }

    // The teacher's own "Desligar" is never called off by the schedule.
    @Test fun theTeachersDesligarIsNotCalledOffByTheSchedule() {
        countdown.request(PowerCountdown.SHUTDOWN, 60)
        assertFalse(countdown.cancelScheduled())
        advance(60_000)
        assertEquals(listOf("screen off"), done)
    }

    // The teacher's "Desligar" over the schedule's warning takes it over.
    @Test fun theTeachersDesligarTakesTheSchedulesOver() {
        countdown.requestScheduled(300)
        countdown.request(PowerCountdown.SHUTDOWN, 60)
        assertFalse(countdown.cancelScheduled())
        advance(60_000)
        assertEquals(listOf("screen off"), done)
    }

    @Test fun aRestartWithAMinuteWarnsTheStudentThenRestarts() {
        val answer = countdown.request(PowerCountdown.RESTART, 60)

        assertEquals(true to "reinicia em 1 minuto", answer)
        assertEquals(listOf("O professor vai reiniciar o celular em 1:00. Salve o que está fazendo."), seen)
        assertEquals("O celular reinicia em 1:00", headlines.single())

        advance(1_000)
        assertEquals("O professor vai reiniciar o celular em 0:59. Salve o que está fazendo.", seen.last())
        advance(58_000)
        assertEquals("O professor vai reiniciar o celular em 0:01. Salve o que está fazendo.", seen.last())
        assertTrue(done.isEmpty())

        advance(1_000)
        assertEquals(listOf("restart"), done)
        assertEquals(PowerCountdown.RESTARTING, seen.last())
        assertEquals(60, seen.count { it.startsWith("O professor vai reiniciar") })
        // The first notice and the restart come over the app; the ticks only change the time.
        assertEquals(listOf(true) + List(59) { false } + true, alerts)
    }

    @Test fun shuttingDownTurnsTheScreenOffAfterTheWarning() {
        val answer = countdown.request(PowerCountdown.SHUTDOWN, 30)

        assertEquals(true to "apaga a tela em 30 segundos (o celular continua ligado)", answer)
        assertEquals("O professor vai apagar a tela em 0:30.", seen.single())
        assertEquals("A tela apaga em 0:30", headlines.single())

        advance(29_000)
        assertEquals("O professor vai apagar a tela em 0:01.", seen.last())
        assertTrue(done.isEmpty())

        advance(1_000)
        // The warning goes before the screen does: nothing to read on it.
        assertEquals("hide", seen.last())
        assertEquals(listOf("screen off"), done)
        assertTrue(tasks.isEmpty())
    }

    @Test fun shuttingDownNowTurnsTheScreenOffBeforeAnswering() {
        val answer = countdown.request(PowerCountdown.SHUTDOWN, 0)

        assertEquals(true to PowerCountdown.SCREEN_OFF, answer)
        assertEquals(listOf("screen off"), done)
        assertEquals(listOf("hide"), seen)
    }

    // The answer has to reach the teacher before the phone goes.
    @Test fun restartingNowWaitsForTheAnswerToGoOut() {
        val answer = countdown.request(PowerCountdown.RESTART, 0)

        assertEquals(true to PowerCountdown.RESTARTING_NOW, answer)
        assertEquals(listOf(PowerCountdown.RESTARTING), seen)
        assertTrue(done.isEmpty())

        advance(PowerCountdown.AFTER_ANSWER_MILLIS)
        assertEquals(listOf("restart"), done)
    }

    @Test fun cancelCallsOffTheCountdown() {
        countdown.request(PowerCountdown.RESTART, 60)
        advance(10_000)

        assertEquals(true to PowerCountdown.RESTART_CANCELLED, countdown.request(PowerCountdown.CANCEL, 0))
        assertEquals("hide", seen.last())

        advance(120_000)
        assertTrue(done.isEmpty())
        assertEquals("hide", seen.last())
    }

    @Test fun cancelCallsOffTheScreenOff() {
        countdown.request(PowerCountdown.SHUTDOWN, 60)

        assertEquals(true to PowerCountdown.SCREEN_OFF_CANCELLED, countdown.request(PowerCountdown.CANCEL, 0))
        advance(120_000)
        assertTrue(done.isEmpty())
    }

    @Test fun aRestartNowCanStillBeCalledOffInItsMoment() {
        countdown.request(PowerCountdown.RESTART, 0)

        assertEquals(true to PowerCountdown.RESTART_CANCELLED, countdown.request(PowerCountdown.CANCEL, 0))
        advance(10_000)
        assertTrue(done.isEmpty())
    }

    @Test fun cancelWithNothingPendingSaysSo() {
        assertEquals(true to PowerCountdown.NOTHING_PENDING, countdown.request(PowerCountdown.CANCEL, 0))
        assertTrue(seen.isEmpty())

        // Nor after the countdown is over.
        countdown.request(PowerCountdown.SHUTDOWN, 5)
        advance(5_000)
        assertEquals(true to PowerCountdown.NOTHING_PENDING, countdown.request(PowerCountdown.CANCEL, 0))
    }

    // The teacher's last word wins: one action, at the new time.
    @Test fun aNewRequestReplacesTheCountdown() {
        countdown.request(PowerCountdown.SHUTDOWN, 60)
        advance(10_000)
        countdown.request(PowerCountdown.RESTART, 30)

        advance(29_000)
        assertTrue(done.isEmpty())
        assertEquals("O professor vai reiniciar o celular em 0:01. Salve o que está fazendo.", seen.last())
        advance(1_000)
        assertEquals(listOf("restart"), done)
        advance(120_000)
        assertEquals(listOf("restart"), done)
    }

    @Test fun nowInTheMiddleEndsTheCountdown() {
        countdown.request(PowerCountdown.RESTART, 60)
        advance(10_000)

        assertEquals(true to PowerCountdown.SCREEN_OFF, countdown.request(PowerCountdown.SHUTDOWN, 0))
        advance(120_000)
        assertEquals(listOf("screen off"), done)
    }

    @Test fun aPhoneThatIsNotTheDeviceOwnerWarnsNobody() {
        owner = false

        assertEquals(false to PowerCountdown.NOT_DEVICE_OWNER, countdown.request(PowerCountdown.RESTART, 60))
        assertEquals(false to PowerCountdown.NOT_DEVICE_OWNER, countdown.request(PowerCountdown.SHUTDOWN, 0))
        assertTrue(seen.isEmpty())
        assertTrue(tasks.isEmpty())
    }

    // Made Device Owner before force-lock was listed: until its next restart.
    @Test fun withoutTheForceLockPolicyOnlyRestartWorks() {
        forceLock = false

        assertEquals(false to PowerCountdown.RESTART_FIRST, countdown.request(PowerCountdown.SHUTDOWN, 60))
        assertTrue(seen.isEmpty())
        assertEquals(true to "reinicia em 1 minuto", countdown.request(PowerCountdown.RESTART, 60))
    }

    @Test fun aScreenOffAndroidRefusesIsSaid() {
        screenOffFails = SecurityException("no force-lock")

        assertEquals(false to PowerCountdown.SCREEN_OFF_REFUSED, countdown.request(PowerCountdown.SHUTDOWN, 0))
    }

    // During a call Android does not restart: the warning goes, the phone stays.
    @Test fun aRestartAndroidRefusesLeavesThePhoneAsItIs() {
        restartFails = IllegalStateException("ongoing call")
        countdown.request(PowerCountdown.RESTART, 5)

        advance(5_000)
        assertTrue(done.isEmpty())
        assertEquals("hide", seen.last())
    }

    @Test fun theClockAndTheTimeReadAsOnAPc() {
        assertEquals("reinicia em 5 minutos", countdown.request(PowerCountdown.RESTART, 300).second)
        assertEquals("apaga a tela em 1 minuto e 30 segundos (o celular continua ligado)", countdown.request(PowerCountdown.SHUTDOWN, 90).second)
        assertEquals("O celular reinicia em 1:05", PowerCountdown.headline(true, 64_001))
        assertEquals("A tela apaga em 0:00", PowerCountdown.headline(false, 0))
    }
}
