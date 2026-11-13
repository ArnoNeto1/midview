package io.github.arnoneto1.midview.agent.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

// The admin password of "Tirar o Midview deste celular" holds a minute at
// most, and never past the screen going away.
class AdminSessionTest {
    private var now = 1_000_000L
    private val session = AdminSession { now }

    @Test fun closedUntilThePasswordIsRight() = assertFalse(session.isOpen)

    @Test fun openForAMinuteThenClosed() {
        session.verified()
        now += 59_999
        assertTrue(session.isOpen)
        assertEquals(1L, session.remainingMillis())

        now += 1
        assertFalse(session.isOpen)
        // Closed for good: time going back does not open it again.
        now -= 30_000
        assertFalse(session.isOpen)
    }

    // The phone locked, or another app in front (onStop).
    @Test fun theScreenGoingAwayClosesIt() {
        session.verified()
        session.end()

        assertFalse(session.isOpen)
        assertEquals(0L, session.remainingMillis())
    }

    @Test fun aClockThatWentBackClosesIt() {
        session.verified()
        now -= 1

        assertFalse(session.isOpen)
    }
}
