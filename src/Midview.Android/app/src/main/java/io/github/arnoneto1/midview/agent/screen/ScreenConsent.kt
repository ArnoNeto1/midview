package io.github.arnoneto1.midview.agent.screen

enum class ConsentState { NotAsked, Waiting, Denied, Capturing }

// Whether the teacher may see this phone's screen. Android asks the student
// ("Iniciar agora") every time a capture starts; show puts that request on
// the screen (ConsentActivity). The request comes up when a teacher first
// connects to the screen port (the lesson starts, or the phone restarted),
// when the teacher asks again ("Pedir de novo para ver a tela") and, where
// the system wants a new answer for each capture (askAgainWhenStopped:
// Android 14 and newer), whenever the capture stops. A student who said no
// is not asked again until the teacher asks; one who left the request
// without answering (dismissed) is asked again as soon as the teacher asks.
// While the teacher keeps the screen locked, a request waits for the unlock.
// The request is put on the screen under the same lock lockedChanged takes,
// so the lock screen never comes up between the decision and the request
// (show must not call back into this class). No Android classes (JVM tests);
// safe from any thread.
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
    private var failures = 0
    private var shownAtMillis: Long? = null

    val state: ConsentState get() = synchronized(lock) { current }

    // The teacher's lock screen is up: a request waits for the unlock.
    val isLocked: Boolean get() = synchronized(lock) { locked }

    // A trusted teacher connected to the screen port: asks unless the
    // student was asked already.
    fun teacherConnected() = synchronized(lock) {
        if (current == ConsentState.NotAsked && request()) show()
    }

    // The teacher asks (again), whatever the student said before.
    fun ask() = synchronized(lock) {
        if (request()) show()
    }

    fun granted() = synchronized(lock) {
        current = ConsentState.Capturing
        pending = false
        failures = 0
    }

    // A "no" while capturing (a second request answered late) changes nothing.
    fun denied() = synchronized(lock) {
        if (current != ConsentState.Capturing) {
            current = ConsentState.Denied
            pending = false
        }
    }

    // The student said yes but the capture would not start. Treated as a
    // stop the first time; again right after, the phone stops asking (the
    // teacher reads "Não permitiu ver a tela" and may ask again) instead of
    // asking over and over for a capture that fails.
    fun captureFailed() = synchronized(lock) {
        failures++
        if (failures >= FAILURES_BEFORE_GIVING_UP) {
            current = ConsentState.Denied
            pending = false
        } else {
            stopped()
        }
    }

    // The capture ended (the student stopped it, or the system did).
    fun stopped() = synchronized(lock) {
        current = ConsentState.NotAsked
        pending = false
        if (askAgainWhenStopped && request()) show()
    }

    // The request went off the screen with no answer: the student went to
    // something else, or the teacher's lock screen came over it. Nobody said
    // no; the tile keeps waiting, the teacher's next ask shows it at once
    // and, under the lock, it comes back at the unlock.
    fun dismissed() = synchronized(lock) {
        if (current == ConsentState.Waiting) {
            shownAtMillis = null
            if (locked) pending = true
        }
    }

    // The teacher's lock screen came up (true) or went away (false).
    fun lockedChanged(locked: Boolean) = synchronized(lock) {
        this.locked = locked
        if (!locked && pending && request()) show()
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
        const val FAILURES_BEFORE_GIVING_UP = 2

        // ConsentActivity stopped without an answer: the student left the
        // request (home, another app, the lock screen over it). Turning the
        // phone is not leaving it, and with the screen off the request is
        // still there when it comes back on. A "no" that came just before
        // the stop (closingRequest) is the system taking its request down
        // as the student left (home on Android 13 does), not a refusal.
        fun leftUnanswered(
            changingConfigurations: Boolean,
            answered: Boolean,
            screenOn: Boolean,
            closingRequest: Boolean = false,
        ): Boolean = !changingConfigurations && !answered && (screenOn || closingRequest)

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
