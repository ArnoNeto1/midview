package io.github.arnoneto1.midview.agent.lock

// The teacher's "Bloquear telas" on this phone: every LockScreen connection
// holds it for as long as it lasts, and the padlock stays up until the last
// one ends (a Master that reconnects overlaps the connection it replaces),
// as the Session's ShowScreenLock/HideScreenLock count it. The first hold
// shows the lock screen, the last release hides it. A hold counts only once
// the lock screen was shown, so one that could not come up leaves nothing
// behind. No Android classes (JVM tests); safe from any thread.
class ScreenLock(
    private val show: () -> Unit,
    private val hide: () -> Unit,
) {
    private val gate = Any()
    private var holders = 0

    val locked: Boolean get() = synchronized(gate) { holders > 0 }

    // Throws what show throws, holding nothing.
    fun hold(): Handle = synchronized(gate) {
        if (holders == 0) show()
        holders++
        Handle()
    }

    // One connection's hold; releasing it again changes nothing.
    inner class Handle internal constructor() {
        private var released = false

        fun release() = synchronized(gate) {
            if (released) return@synchronized
            released = true
            if (--holders == 0) hide()
        }
    }
}
