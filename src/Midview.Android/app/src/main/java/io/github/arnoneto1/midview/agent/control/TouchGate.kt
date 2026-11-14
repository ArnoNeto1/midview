package io.github.arnoneto1.midview.agent.control

import android.view.KeyEvent

// "Travar o toque do aluno" while the teacher controls the device: whether
// the student's touch lock (StudentTouchLock, a see-through window over the
// whole screen that takes every touch) is up, and whether it lets touches
// through at this moment. It lets them through only while the teacher's own
// finger is on the screen (a gesture is injected below the window, as a real
// finger, so it must not land on it); typing and Android's buttons need no
// opening. Every change answers the state to show. No Android classes but
// the key codes (JVM tests); one control's thread at a time.
class TouchGate {
    enum class Overlay { NONE, BLOCKING, PASSING }

    private var wanted = false
    private var teacherTouching = false

    val overlay: Overlay
        get() = when {
            !wanted -> Overlay.NONE
            teacherTouching -> Overlay.PASSING
            else -> Overlay.BLOCKING
        }

    // The teacher's toggle (or its default at the start of control).
    fun want(on: Boolean): Overlay {
        wanted = on
        return overlay
    }

    // The teacher's finger went down on the screen (true) or lifted (false).
    fun teacher(touching: Boolean): Overlay {
        teacherTouching = touching
        return overlay
    }

    // Control ended, however it ended: nothing left on the screen.
    fun stop(): Overlay {
        wanted = false
        teacherTouching = false
        return overlay
    }

    companion object {
        // The student's keys taken while the touch is locked: Back, Home,
        // Recents, the menu, search and the volume. Never the power key: the
        // student can always turn the screen off. The teacher's Back, Home
        // and Recents are global actions, not keys, and are never taken.
        val BLOCKED_KEYS: Set<Int> = setOf(
            KeyEvent.KEYCODE_BACK,
            KeyEvent.KEYCODE_HOME,
            KeyEvent.KEYCODE_APP_SWITCH,
            KeyEvent.KEYCODE_MENU,
            KeyEvent.KEYCODE_SEARCH,
            KeyEvent.KEYCODE_ASSIST,
            KeyEvent.KEYCODE_VOLUME_UP,
            KeyEvent.KEYCODE_VOLUME_DOWN,
            KeyEvent.KEYCODE_VOLUME_MUTE,
        )

        fun blocks(overlay: Overlay, keyCode: Int): Boolean = overlay != Overlay.NONE && keyCode in BLOCKED_KEYS
    }
}
