package io.github.arnoneto1.midview.agent.control

import io.github.arnoneto1.midview.agent.control.TouchGate.Overlay

// The student's touch lock of every teacher controlling the device at once
// (one TouchGate per screen connection): the one overlay on the screen is up
// while any of them locks, and open while any of them has a finger down. A
// connection that ends takes only its own part away. An opening older than
// PASS_LIMIT_MS without news (the teacher's finger lift lost on the way) is
// closed again. Every change answers what the screen should show now: the
// latest change always wins. No Android classes (JVM tests); callers hold
// one lock around it.
class TouchLocks(private val clock: () -> Long) {
    private class Part(val overlay: Overlay, val since: Long)

    private val parts = HashMap<Any, Part>()

    val overlay: Overlay
        get() = when {
            parts.values.any { it.overlay == Overlay.PASSING } -> Overlay.PASSING
            parts.values.any { it.overlay == Overlay.BLOCKING } -> Overlay.BLOCKING
            else -> Overlay.NONE
        }

    fun set(owner: Any, next: Overlay): Overlay {
        if (next == Overlay.NONE) parts.remove(owner) else parts[owner] = Part(next, clock())
        return overlay
    }

    // The openings left without news for too long close again.
    fun expire(): Overlay {
        val now = clock()
        for ((owner, part) in parts.entries.toList()) {
            if (part.overlay == Overlay.PASSING && now - part.since >= PASS_LIMIT_MS) parts[owner] = Part(Overlay.BLOCKING, now)
        }
        return overlay
    }

    // No control left at all (StudentTouchLock's watchdog).
    fun clear(): Overlay {
        parts.clear()
        return overlay
    }

    companion object {
        // Longer than any of the teacher's gestures (a long press is 0.7 s,
        // a drag sends a step at least every 0.12 s).
        const val PASS_LIMIT_MS = 5_000L
    }
}
