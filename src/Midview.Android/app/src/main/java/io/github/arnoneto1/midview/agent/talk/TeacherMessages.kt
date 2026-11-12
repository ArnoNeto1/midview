package io.github.arnoneto1.midview.agent.talk

import io.github.arnoneto1.midview.agent.protocol.TextMessage

// The teacher's "Mensagem" on this phone, as the PC Session's
// WinFormsMessageDisplay: each message is meant to be noticed, over whatever
// the student is doing, and stays until the student taps "OK, ENTENDI". Several
// can be open at once: the newest is shown, and OK brings the one before it
// back, as the PC's windows stack. Under the teacher's lock screen a message
// would only be hidden behind the padlock (and coming up over it would break
// the lock task): it waits, and shows once the teacher unlocks. "Fechar
// programas" closes them all, as on a PC (closeAll).
// No Android classes (JVM tests); safe from any thread. screen puts the
// message screen up (or refreshes it) and takes it down.
class TeacherMessages(
    private val isLocked: () -> Boolean,
    private val screen: MessageScreen,
) {
    private val gate = Any()

    // Oldest first.
    private val open = ArrayList<String>()

    // The one on the screen now: the newest still open.
    val newest: String? get() = synchronized(gate) { open.lastOrNull() }

    val count: Int get() = synchronized(gate) { open.size }

    // The ShowMessage payload in, the CommandAck's result out. As on a PC,
    // the answer comes once the message is on its way to the screen, not
    // when the student closes it.
    fun receive(payload: ByteArray): Pair<Boolean, String> {
        val text = TextMessage.decode(payload) ?: return false to INVALID
        synchronized(gate) { open += text }
        if (isLocked()) return true to AFTER_LOCK
        return try {
            screen.present()
            true to SHOWN
        } catch (e: RuntimeException) {
            synchronized(gate) { open.remove(text) }
            false to NOT_SHOWN
        }
    }

    // "OK, ENTENDI": the newest goes; the screen goes with the last one.
    fun ok() {
        val left = synchronized(gate) {
            open.removeLastOrNull()
            open.size
        }
        if (left == 0) screen.dismiss()
    }

    // The teacher's lock screen went: what waited under it comes up.
    fun unlocked() {
        if (count > 0) present()
    }

    // "Fechar programas": every message goes, shown or waiting.
    fun closeAll() {
        synchronized(gate) { open.clear() }
        screen.dismiss()
    }

    // Brought up again (the student came back from its notification): only
    // with something to show, and never over the teacher's lock screen.
    fun present() {
        if (count == 0 || isLocked()) return
        try {
            screen.present()
        } catch (e: RuntimeException) {
            // Its notification is still there to open it.
        }
    }

    companion object {
        // The CommandAck's detail; the PC Session answers only 1 or 0.
        const val SHOWN = "mensagem mostrada"
        const val AFTER_LOCK = "a tela está bloqueada: a mensagem aparece quando ela for liberada"
        const val NOT_SHOWN = "a mensagem não pôde ser mostrada"
        const val INVALID = "mensagem vazia ou inválida"

        // The PC's words (TeacherMessageLayout).
        const val HEADER = "Mensagem do professor"
        const val OK_BUTTON = "OK, ENTENDI"

        // Above the newest when older ones wait under it.
        fun header(count: Int): String = if (count <= 1) HEADER else "$HEADER (mais ${count - 1} depois desta)"
    }
}

// What the student sees of the messages (TeacherMessageActivity and its
// notification). present may throw when the screen cannot be started.
interface MessageScreen {
    fun present()

    fun dismiss()
}
