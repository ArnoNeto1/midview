package io.github.arnoneto1.midview.agent.screen

enum class ConsentState { NotAsked, Waiting, Denied, Capturing }

// Whether the teacher may see this phone's screen. Android asks the student
// ("Iniciar agora") every time a capture starts; show puts that request on
// the screen (ConsentActivity). The request comes up when a teacher first
// connects to the screen port (the lesson starts, or the phone restarted),
// when the teacher asks again ("Pedir de novo para ver a tela") and, where
// the system wants a new answer for each capture (askAgainWhenStopped:
// Android 14 and newer), whenever the capture stops. A student who said no
// is not asked again until the teacher asks. While the teacher keeps the
// screen locked, a request waits for the unlock. No Android classes (JVM
// tests); safe from any thread.
class ScreenConsent(
    private val clock: () -> Long,
    private val askAgainWhenStopped: Boolean = false,
    private val show: () -> Unit,
) {
    private val lock = Any()
    private var current = ConsentState.NotAsked
    private var locked = false

    // Asked while locked: shown at the unlock.
    private var pending = false
    private var shownAtMillis: Long? = null

    val state: ConsentState get() = synchronized(lock) { current }

    // A trusted teacher connected to the screen port: asks unless the
    // student was asked already.
    fun teacherConnected() {
        if (synchronized(lock) { current == ConsentState.NotAsked && request() }) show()
    }

    // The teacher asks (again), whatever the student said before.
    fun ask() {
        if (synchronized(lock) { request() }) show()
    }

    fun granted() = synchronized(lock) {
        current = ConsentState.Capturing
        pending = false
    }

    fun denied() = synchronized(lock) {
        current = ConsentState.Denied
        pending = false
    }

    // The capture ended (the student stopped it, or the system did).
    fun stopped() {
        val asking = synchronized(lock) {
            current = ConsentState.NotAsked
            pending = false
            askAgainWhenStopped && request()
        }
        if (asking) show()
    }

    // The teacher's lock screen came up (true) or went away (false).
    fun lockedChanged(locked: Boolean) {
        val asking = synchronized(lock) {
            this.locked = locked
            !locked && pending && request()
        }
        if (asking) show()
    }

    // Under the lock. True when the request must be put on the screen now.
    private fun request(): Boolean {
        if (current == ConsentState.Capturing) return false
        val now = clock()
        val shownAt = shownAtMillis
        // A request already on the screen is not stacked on itself; one that
        // went away without an answer is shown again after a while.
        if (current == ConsentState.Waiting && !pending && shownAt != null && now - shownAt < ASK_AGAIN_AFTER_MILLIS) {
            return false
        }
        current = ConsentState.Waiting
        if (locked) {
            pending = true
            return false
        }
        pending = false
        shownAtMillis = now
        return true
    }

    companion object {
        const val ASK_AGAIN_AFTER_MILLIS = 60_000L

        const val WAITING_TEXT = "Aguardando o aluno permitir"
        const val DENIED_TEXT = "Não permitiu ver a tela"

        // What the teacher's tile says instead of the screen; null while it is shown.
        fun statusText(state: ConsentState): String? = when (state) {
            ConsentState.NotAsked, ConsentState.Waiting -> WAITING_TEXT
            ConsentState.Denied -> DENIED_TEXT
            ConsentState.Capturing -> null
        }
    }
}
