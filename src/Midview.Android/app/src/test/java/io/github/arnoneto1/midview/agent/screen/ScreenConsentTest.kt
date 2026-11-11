package io.github.arnoneto1.midview.agent.screen

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

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
}
