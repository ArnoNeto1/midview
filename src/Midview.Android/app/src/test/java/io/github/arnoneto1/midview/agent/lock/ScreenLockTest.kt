package io.github.arnoneto1.midview.agent.lock

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test

class ScreenLockTest {
    private var shows = 0
    private var hides = 0
    private var failShow = false
    private val lock = ScreenLock(
        show = {
            if (failShow) throw IllegalStateException("no screen")
            shows++
        },
        hide = { hides++ },
    )

    // Several teachers' connections (a Master that reconnects overlaps the
    // one it replaces): the screen comes back only when the last one ends.
    @Test fun firstHoldShowsLastReleaseHides() {
        val first = lock.hold()
        val second = lock.hold()
        assertEquals(1, shows)
        assertTrue(lock.locked)

        first.release()
        assertEquals(0, hides)
        assertTrue(lock.locked)

        second.release()
        assertEquals(1, hides)
        assertFalse(lock.locked)
    }

    @Test fun aHandleReleasedTwiceCountsOnce() {
        val first = lock.hold()
        val second = lock.hold()

        first.release()
        first.release()

        assertEquals(0, hides)
        assertTrue(lock.locked)
        second.release()
        assertEquals(1, hides)
    }

    @Test fun lockedAgainAfterTheUnlock() {
        lock.hold().release()
        lock.hold()

        assertEquals(2, shows)
        assertEquals(1, hides)
        assertTrue(lock.locked)
    }

    // As the Session counts it: only once shown, so a lock that could not
    // come up leaves nothing for a later one to wait on.
    @Test fun aShowThatFailsHoldsNothing() {
        failShow = true
        try {
            lock.hold()
            fail("the failure reaches the caller")
        } catch (e: IllegalStateException) {
            // expected
        }
        assertFalse(lock.locked)

        failShow = false
        lock.hold().release()
        assertEquals(1, shows)
        assertEquals(1, hides)
    }
}
