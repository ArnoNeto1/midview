package io.github.arnoneto1.midview.agent.apps

import io.github.arnoneto1.midview.agent.sites.FakeSiteGateway
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class CloseCountdownTest {
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

    private var locked = false
    private var closings = 0
    private val gateway = FakeSiteGateway()
    private val catalog = object : AppCatalog {
        override fun launchable() = setOf("com.android.chrome", "com.miui.calculator")
        override fun protected() = emptySet<String>()
        override fun suspended() = emptySet<String>()
        override fun isInstalled(pkg: String) = true
    }
    private val unlockTasks = mutableListOf<() -> Unit>()
    private val closer = AppCloser(
        AppSuspender(catalog, gateway), gateway, "io.github.arnoneto1.midview.agent",
        reapplyBlock = {}, isScreenLocked = { locked }, goHome = { closings++ }, killBackground = {}, pause = {},
        runLater = { _, task -> unlockTasks += task },
    )
    private val countdown = CloseCountdown(closer, notice, schedule = { delay, task -> tasks += (clock + delay) to task }, now = { clock })

    @Test fun oneMinuteCountsDownThenCloses() {
        val answer = countdown.request(60)

        assertEquals(true to "aluno avisado; os apps fecham em 1 minuto", answer)
        assertEquals(listOf("O professor vai fechar os apps em 1:00. Salve o que está fazendo."), seen)
        assertEquals(0, closings)

        advance(1_000)
        assertEquals("O professor vai fechar os apps em 0:59. Salve o que está fazendo.", seen.last())
        advance(58_000)
        assertEquals("O professor vai fechar os apps em 0:01. Salve o que está fazendo.", seen.last())
        assertEquals(0, closings)

        advance(1_000)
        assertEquals(1, closings)
        assertEquals(CloseCountdown.DONE, seen.last())
        assertEquals(60, seen.count { it.startsWith("O professor vai fechar") })
        // The first notice and the closing come over the app; the ticks only change the time.
        assertEquals(listOf(true) + List(59) { false } + true, alerts)
        assertEquals("Os apps fecham em 1:00", headlines.first())
        assertEquals("Os apps fecham em 0:01", headlines[59])
        assertEquals(CloseCountdown.DONE_HEADLINE, headlines.last())

        advance(CloseCountdown.DONE_LINGER_MILLIS)
        assertEquals("hide", seen.last())
        assertTrue(tasks.isEmpty())
    }

    @Test fun thirtySecondsSaysSo() {
        assertEquals(true to "aluno avisado; os apps fecham em 30 segundos", countdown.request(30))
        assertEquals("O professor vai fechar os apps em 0:30. Salve o que está fazendo.", seen.single())
        advance(30_000)
        assertEquals(1, closings)
    }

    // "Agora": closed on the spot, and the student is told for a moment.
    @Test fun nowClosesAtOnceAndSaysSo() {
        assertEquals(true to AppCloser.CLOSED, countdown.request(0))
        assertEquals(1, closings)
        assertEquals(listOf(CloseCountdown.DONE), seen)
        advance(CloseCountdown.DONE_LINGER_MILLIS)
        assertEquals(listOf(CloseCountdown.DONE, "hide"), seen)
    }

    // A second command replaces the first: one closing, at the new time.
    @Test fun aNewCommandReplacesTheCountdown() {
        countdown.request(60)
        advance(10_000)
        countdown.request(30)
        assertEquals("O professor vai fechar os apps em 0:30. Salve o que está fazendo.", seen.last())
        assertTrue("the new countdown comes over the app again", alerts.last())

        advance(30_000)
        assertEquals(1, closings)
        advance(60_000)
        assertEquals(1, closings)
        assertEquals("hide", seen.last())
    }

    @Test fun nowDuringACountdownEndsIt() {
        countdown.request(60)
        advance(5_000)
        countdown.request(0)
        assertEquals(1, closings)
        advance(120_000)
        assertEquals(1, closings)
        assertFalse(seen.drop(seen.indexOf(CloseCountdown.DONE)).any { it.startsWith("O professor vai fechar") })
    }

    // Under the teacher's lock screen: the countdown runs (the student is
    // warned), and at its end the closing waits for the lock to go; the
    // notice goes away, and nothing claims the apps were closed.
    @Test fun underTheLockTheCountdownRunsAndTheClosingWaitsForTheUnlock() {
        locked = true
        assertEquals(true to "aluno avisado; os apps fecham em 30 segundos", countdown.request(30))
        advance(30_000)

        assertEquals(0, closings)
        assertTrue(closer.isPending)
        assertEquals("hide", seen.last())
        assertFalse(CloseCountdown.DONE in seen)

        locked = false
        closer.unlocked()
        unlockTasks.single()()
        assertEquals(1, closings)
        assertFalse(closer.isPending)
    }

    @Test fun nowUnderTheLockSaysNothingToTheStudent() {
        locked = true
        assertEquals(true to AppCloser.AFTER_LOCK, countdown.request(0))
        assertEquals(listOf("hide"), seen)
    }

    @Test fun aPhoneThatIsNotTheDeviceOwnerWarnsNobody() {
        gateway.owner = false
        assertEquals(false to AppCloser.NOT_DEVICE_OWNER, countdown.request(60))
        assertTrue(seen.isEmpty())
        assertTrue(tasks.isEmpty())
    }

    @Test fun theClockReadsAsThePcBanner() {
        assertEquals("1:00", CloseCountdown.clock(60_000))
        assertEquals("0:59", CloseCountdown.clock(58_001))
        assertEquals("0:01", CloseCountdown.clock(1))
        assertEquals("0:00", CloseCountdown.clock(0))
        assertEquals("1:05", CloseCountdown.clock(65_000))
        assertEquals("30 segundos", CloseCountdown.delayText(30))
        assertEquals("1 minuto", CloseCountdown.delayText(60))
        assertEquals("1 minuto e 30 segundos", CloseCountdown.delayText(90))
        assertEquals("2 minutos e 1 segundo", CloseCountdown.delayText(121))
    }
}
