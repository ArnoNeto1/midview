package io.github.arnoneto1.midview.agent.screen

import org.junit.Assert.assertEquals
import org.junit.Test

class CaptureCadenceTest {
    @Test fun cadenceIs66IdleAnd33ActiveAndNeverBelowHalfTheWork() {
        val c = CaptureCadence()
        assertEquals(66L, c.restAfter(0, false, 10_000))
        assertEquals(56L, c.restAfter(10, false, 10_000))
        // One changed capture is a blinking caret: still idle.
        assertEquals(66L, c.restAfter(0, true, 10_000))
        // Changed twice in a row: moving, active for 1 s.
        assertEquals(33L, c.restAfter(0, true, 10_100))
        assertEquals(23L, c.restAfter(10, false, 10_200))
        assertEquals(33L, c.restAfter(0, false, 11_099))
        assertEquals(66L, c.restAfter(0, false, 11_100))
        // Slow PC: at least half the work.
        assertEquals(50L, c.restAfter(100, false, 20_000))
        assertEquals(200L, c.restAfter(400, false, 20_000))
    }

    @Test fun teacherInputKeepsItActive() {
        val c = CaptureCadence()
        c.activity(5_000)
        assertEquals(33L, c.restAfter(0, false, 5_500))
        assertEquals(66L, c.restAfter(0, false, 6_000))
    }
}
