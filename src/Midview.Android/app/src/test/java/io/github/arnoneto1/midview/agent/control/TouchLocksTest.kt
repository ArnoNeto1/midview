package io.github.arnoneto1.midview.agent.control

import io.github.arnoneto1.midview.agent.control.TouchGate.Overlay
import org.junit.Assert.assertEquals
import org.junit.Test

class TouchLocksTest {
    private var now = 0L
    private val locks = TouchLocks { now }
    private val first = Any()
    private val second = Any()

    @Test fun oneTeacherLocksAndFrees() {
        assertEquals(Overlay.BLOCKING, locks.set(first, Overlay.BLOCKING))
        assertEquals(Overlay.NONE, locks.set(first, Overlay.NONE))
    }

    // A second connection ending its control leaves the first teacher's lock.
    @Test fun anotherConnectionEndingLeavesTheLock() {
        locks.set(first, Overlay.BLOCKING)
        locks.set(second, Overlay.BLOCKING)
        assertEquals(Overlay.BLOCKING, locks.set(second, Overlay.NONE))
        assertEquals(Overlay.NONE, locks.set(first, Overlay.NONE))
    }

    // Any teacher's finger down opens it; lifted, it blocks again.
    @Test fun anyFingerDownOpensIt() {
        locks.set(first, Overlay.BLOCKING)
        assertEquals(Overlay.PASSING, locks.set(second, Overlay.PASSING))
        assertEquals(Overlay.BLOCKING, locks.set(second, Overlay.BLOCKING))
    }

    // The finger lift lost (the teacher's input queue full): closed after a while.
    @Test fun anOpeningWithoutNewsClosesAgain() {
        locks.set(first, Overlay.PASSING)
        now += TouchLocks.PASS_LIMIT_MS - 1
        assertEquals(Overlay.PASSING, locks.expire())
        now += 1
        assertEquals(Overlay.BLOCKING, locks.expire())
    }

    // A long drag keeps it open: each step is news.
    @Test fun aLongDragStaysOpen() {
        locks.set(first, Overlay.PASSING)
        now += TouchLocks.PASS_LIMIT_MS - 100
        locks.set(first, Overlay.PASSING)
        now += 200
        assertEquals(Overlay.PASSING, locks.expire())
    }

    @Test fun clearingLeavesNothing() {
        locks.set(first, Overlay.PASSING)
        locks.set(second, Overlay.BLOCKING)
        assertEquals(Overlay.NONE, locks.clear())
    }
}
