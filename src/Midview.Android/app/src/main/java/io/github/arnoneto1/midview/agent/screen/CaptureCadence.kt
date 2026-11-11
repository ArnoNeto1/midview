package io.github.arnoneto1.midview.agent.screen

// How long the capture loop rests between captures; mirrors
// src/Midview.Session/CaptureCadence.cs. Twice as often (33 ms) while something
// moves on screen or the teacher is clicking and typing, 66 ms once that has
// stopped for a second. A blinking caret does not count as moving: it changes
// one capture and not the next.
class CaptureCadence {
    companion object {
        const val ACTIVE_INTERVAL_MS = 33L
        const val IDLE_INTERVAL_MS = 66L
        const val ACTIVE_FOR_MS = 1000L
    }

    @Volatile private var lastActivityMs = Long.MIN_VALUE / 2
    private var lastCaptureChanged = false

    // The teacher's input.
    fun activity(nowMs: Long) {
        lastActivityMs = nowMs
    }

    // Call after each capture, with how long the capture, diff and encode took
    // and whether the picture changed. The rest is never shorter than half the work.
    fun restAfter(workMs: Long, changed: Boolean, nowMs: Long): Long {
        if (changed && lastCaptureChanged) activity(nowMs)
        lastCaptureChanged = changed
        val interval = if (nowMs - lastActivityMs < ACTIVE_FOR_MS) ACTIVE_INTERVAL_MS else IDLE_INTERVAL_MS
        return maxOf(interval - workMs, workMs / 2)
    }
}
