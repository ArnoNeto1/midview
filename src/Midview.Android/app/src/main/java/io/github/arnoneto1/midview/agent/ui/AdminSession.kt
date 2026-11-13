package io.github.arnoneto1.midview.agent.ui

// How long the admin password typed in "Tirar o Midview deste celular"
// (LeaveActivity) holds: from the right password, for at most 60 seconds,
// and never past the screen going away (the phone locked, another app in
// front): a student who picks the phone up afterwards is asked again.
// clock: SystemClock.elapsedRealtime on the phone (it does not move with
// the wall clock).
class AdminSession(private val clock: () -> Long) {
    private var verifiedAt: Long? = null

    fun verified() {
        verifiedAt = clock()
    }

    // The screen went away (onStop), or the choice was used.
    fun end() {
        verifiedAt = null
    }

    val isOpen: Boolean
        get() {
            val at = verifiedAt ?: return false
            val elapsed = clock() - at
            if (elapsed in 0 until LIFETIME_MILLIS) return true
            verifiedAt = null
            return false
        }

    // How long it still holds (0 when closed), for the screen's own timer.
    fun remainingMillis(): Long {
        val at = verifiedAt ?: return 0
        return if (isOpen) LIFETIME_MILLIS - (clock() - at) else 0
    }

    companion object {
        const val LIFETIME_MILLIS = 60_000L
    }
}
