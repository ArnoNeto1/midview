package io.github.arnoneto1.midview.agent.control

import android.view.KeyEvent
import io.github.arnoneto1.midview.agent.control.TouchGate.Overlay
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class TouchGateTest {
    private val gate = TouchGate()

    @Test fun nothingOnTheScreenUntilTheTeacherLocks() {
        assertEquals(Overlay.NONE, gate.overlay)
        assertEquals(Overlay.NONE, gate.teacher(true))
        assertEquals(Overlay.NONE, gate.teacher(false))
    }

    @Test fun lockedItTakesTheStudentsTouch() {
        assertEquals(Overlay.BLOCKING, gate.want(true))
    }

    // The teacher's finger is injected below the lock: open while it is down.
    @Test fun itOpensOnlyWhileTheTeachersFingerIsDown() {
        gate.want(true)
        assertEquals(Overlay.PASSING, gate.teacher(true))
        assertEquals(Overlay.BLOCKING, gate.teacher(false))
    }

    @Test fun freedByTheTeacherItGoes() {
        gate.want(true)
        assertEquals(Overlay.NONE, gate.want(false))
    }

    // However control ends, even with the teacher's finger down, nothing stays.
    @Test fun theEndOfControlLeavesNothing() {
        gate.want(true)
        gate.teacher(true)
        assertEquals(Overlay.NONE, gate.stop())
        assertEquals(Overlay.NONE, gate.teacher(false))
    }

    @Test fun theStudentsKeysAreTakenOnlyWhileLocked() {
        assertTrue(TouchGate.blocks(Overlay.BLOCKING, KeyEvent.KEYCODE_BACK))
        assertTrue(TouchGate.blocks(Overlay.PASSING, KeyEvent.KEYCODE_VOLUME_UP))
        assertTrue(TouchGate.blocks(Overlay.BLOCKING, KeyEvent.KEYCODE_APP_SWITCH))
        assertFalse(TouchGate.blocks(Overlay.NONE, KeyEvent.KEYCODE_BACK))
        // The student can always turn the screen off.
        assertFalse(TouchGate.blocks(Overlay.BLOCKING, KeyEvent.KEYCODE_POWER))
    }
}
