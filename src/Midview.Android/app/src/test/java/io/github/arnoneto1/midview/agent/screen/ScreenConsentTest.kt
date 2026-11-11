package io.github.arnoneto1.midview.agent.screen

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

class ScreenConsentTest {
    private var now = 0L
    private var shown = 0

    private fun consent(askAgainWhenStopped: Boolean = false) =
        ScreenConsent(clock = { now }, askAgainWhenStopped = askAgainWhenStopped) { shown++ }

    @Test fun askWhileLockedWaitsForUnlock() {
        val consent = consent()
        consent.lockedChanged(true)
        consent.ask()
        assertEquals(0, shown)
        // The teacher reads "Aguardando o aluno permitir" meanwhile.
        assertEquals(ConsentState.Waiting, consent.state)

        consent.lockedChanged(false)
        assertEquals(1, shown)

        // Locking and unlocking again with nothing asked shows nothing.
        consent.lockedChanged(true)
        consent.lockedChanged(false)
        assertEquals(1, shown)
    }

    @Test fun deniedStaysDeniedUntilAskedAgain() {
        val consent = consent()
        assertEquals(ConsentState.NotAsked, consent.state)

        // The lesson starts: the first teacher on 5901 asks.
        consent.teacherConnected()
        assertEquals(1, shown)
        assertEquals(ConsentState.Waiting, consent.state)

        consent.denied()
        assertEquals(ConsentState.Denied, consent.state)

        // Another connection (or teacher) does not ask a student who said no.
        consent.teacherConnected()
        assertEquals(1, shown)
        assertEquals(ConsentState.Denied, consent.state)

        // "Pedir de novo para ver a tela".
        consent.ask()
        assertEquals(2, shown)
        assertEquals(ConsentState.Waiting, consent.state)

        consent.granted()
        assertEquals(ConsentState.Capturing, consent.state)
        consent.ask()
        consent.teacherConnected()
        assertEquals(2, shown)
    }

    @Test fun aWaitingAskIsShownAgainOnlyAfterAWhile() {
        val consent = consent()
        consent.ask()
        consent.ask()
        consent.teacherConnected()
        assertEquals(1, shown)

        // The request may have gone away without an answer.
        now += ScreenConsent.ASK_AGAIN_AFTER_MILLIS
        consent.ask()
        assertEquals(2, shown)
    }

    @Test fun captureStoppingAsksAgainOnlyFromAndroid14() {
        val before14 = consent(askAgainWhenStopped = false)
        before14.ask()
        before14.granted()
        before14.stopped()
        assertEquals(ConsentState.NotAsked, before14.state)
        assertEquals(1, shown)
        // The next connection of a teacher asks again.
        before14.teacherConnected()
        assertEquals(2, shown)

        shown = 0
        val from14 = consent(askAgainWhenStopped = true)
        from14.ask()
        from14.granted()
        from14.stopped()
        assertEquals(2, shown)
        assertEquals(ConsentState.Waiting, from14.state)

        // Stopped while the screen is locked: asked on unlock.
        from14.granted()
        from14.lockedChanged(true)
        from14.stopped()
        assertEquals(2, shown)
        from14.lockedChanged(false)
        assertEquals(3, shown)
    }

    @Test fun theTileSaysWhyTheScreenIsNotShown() {
        assertEquals("Aguardando o aluno permitir", ScreenConsent.statusText(ConsentState.NotAsked))
        assertEquals("Aguardando o aluno permitir", ScreenConsent.statusText(ConsentState.Waiting))
        assertEquals("Não permitiu ver a tela", ScreenConsent.statusText(ConsentState.Denied))
        assertNull(ScreenConsent.statusText(ConsentState.Capturing))
    }

    @Test fun deniedWhileCapturingKeepsCapturing() {
        val consent = consent()
        consent.ask()
        consent.granted()
        // A second request (activity and notification both opened) answered "no".
        consent.denied()
        assertEquals(ConsentState.Capturing, consent.state)
    }

    @Test fun aCaptureThatKeepsFailingStopsBeingAskedFor() {
        val consent = consent(askAgainWhenStopped = true)
        consent.ask()
        assertEquals(1, shown)

        consent.captureFailed()
        assertEquals(2, shown)
        assertEquals(ConsentState.Waiting, consent.state)

        consent.captureFailed()
        assertEquals(2, shown)
        assertEquals(ConsentState.Denied, consent.state)

        // The teacher asks again; a capture that starts resets the count.
        consent.ask()
        consent.granted()
        consent.captureFailed()
        assertEquals(4, shown)
        assertEquals(ConsentState.Waiting, consent.state)
    }

    // The student left the request without answering (the home button, say):
    // nobody said no, the tile still waits, and the teacher's next ask puts
    // it up again at once instead of a minute later.
    @Test fun aRequestLeftUnansweredIsShownAgainAtTheNextAsk() {
        val consent = consent()
        consent.teacherConnected()
        assertEquals(1, shown)

        consent.dismissed()
        assertEquals(ConsentState.Waiting, consent.state)

        // "Pedir de novo para ver a tela", well within the minute.
        now += 1_000
        consent.ask()
        assertEquals(2, shown)
        assertEquals(ConsentState.Waiting, consent.state)

        // Still one request on the screen at a time.
        consent.ask()
        assertEquals(2, shown)
    }

    // The teacher's lock screen came over the request: it comes back at the unlock.
    @Test fun aRequestPushedAsideByTheLockIsShownAtTheUnlock() {
        val consent = consent()
        consent.ask()
        assertEquals(1, shown)

        consent.lockedChanged(true)
        consent.dismissed()
        assertEquals(ConsentState.Waiting, consent.state)
        assertEquals(1, shown)

        consent.lockedChanged(false)
        assertEquals(2, shown)
    }

    // A late "went away" changes nothing once the student answered.
    @Test fun dismissedAfterAnAnswerChangesNothing() {
        val consent = consent()
        consent.ask()
        consent.denied()
        consent.dismissed()
        assertEquals(ConsentState.Denied, consent.state)

        consent.ask()
        consent.granted()
        consent.dismissed()
        assertEquals(ConsentState.Capturing, consent.state)
        assertEquals(2, shown)
    }

    // Only leaving the request for something else counts as no answer: not
    // turning the phone, not the answer itself, not the screen going off.
    @Test fun onlyLeavingTheRequestIsNoAnswer() {
        assertTrue(ScreenConsent.leftUnanswered(changingConfigurations = false, answered = false, screenOn = true))
        assertFalse(ScreenConsent.leftUnanswered(changingConfigurations = true, answered = false, screenOn = true))
        assertFalse(ScreenConsent.leftUnanswered(changingConfigurations = false, answered = true, screenOn = true))
        assertFalse(ScreenConsent.leftUnanswered(changingConfigurations = false, answered = false, screenOn = false))

        // A "no" still being weighed when the screen went away: the system
        // took its request down as the student left, not a refusal.
        assertTrue(
            ScreenConsent.leftUnanswered(changingConfigurations = false, answered = false, screenOn = true, closingRequest = true),
        )
        assertTrue(
            ScreenConsent.leftUnanswered(changingConfigurations = false, answered = false, screenOn = false, closingRequest = true),
        )
        assertFalse(
            ScreenConsent.leftUnanswered(changingConfigurations = true, answered = false, screenOn = true, closingRequest = true),
        )
    }

    // The lock cannot come up between deciding to show the request and
    // showing it: lockedChanged waits until the request is on its way.
    @Test fun theLockWaitsForARequestBeingShown() {
        val inShow = CountDownLatch(1)
        val letGo = CountDownLatch(1)
        lateinit var consent: ScreenConsent
        consent = ScreenConsent(clock = { now }) {
            shown++
            inShow.countDown()
            letGo.await(5, TimeUnit.SECONDS)
        }
        val asking = Thread { consent.ask() }
        asking.start()
        assertTrue(inShow.await(5, TimeUnit.SECONDS))

        val locked = CountDownLatch(1)
        val locking = Thread {
            consent.lockedChanged(true)
            locked.countDown()
        }
        locking.start()
        assertFalse("the lock came up while the request was being shown", locked.await(200, TimeUnit.MILLISECONDS))

        letGo.countDown()
        assertTrue(locked.await(5, TimeUnit.SECONDS))
        asking.join(5_000)
        locking.join(5_000)
        assertTrue(consent.isLocked)
        assertEquals(1, shown)
    }
}
