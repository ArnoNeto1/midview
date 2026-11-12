package io.github.arnoneto1.midview.agent.talk

import io.github.arnoneto1.midview.agent.protocol.TextMessage

// The teacher's "Bate-papo" on this phone, as the PC Session's
// WinFormsChatWindows: every conversation a teacher opens (ChatHandler) gets
// the "Conversa com o professor" screen, with the teacher's lines, the
// student's own, and a box to answer. A teacher's line brings the screen back
// to the front, as on a PC; leaving it (back, home) only hides it, and its
// notification brings it back while the conversation lasts. When the teacher
// ends it (or the connection drops) the screen says so and closes. More than
// one conversation can be open (two teachers, or one teacher's class chat and
// a private one), each with its own screen state and notification. Under the
// teacher's lock screen nothing comes up over the padlock: the conversation
// waits, and shows once the teacher unlocks.
// No Android classes (JVM tests); safe from any thread. screen draws it;
// background runs the network write of a student's line off the screen's
// thread.
class TeacherChats(
    private val isLocked: () -> Boolean,
    private val screen: ChatScreen,
    private val background: (() -> Unit) -> Unit,
) {
    private val gate = Any()
    private var lastId = 0
    private val open = LinkedHashMap<Int, Conversation>()

    // Conversations shown or wanting to be shown once the lock goes, by id.
    private val waiting = LinkedHashSet<Int>()

    val locked: Boolean get() = isLocked()

    fun conversation(id: Int): Conversation? = synchronized(gate) { open[id] }

    // The newest conversation still on, if any.
    val newest: Conversation? get() = synchronized(gate) { open.values.lastOrNull() }

    // A teacher opened a conversation: its screen comes up (after the lock,
    // under it). sendToTeacher delivers one line of the student's and says
    // whether it went; it is called on a background thread.
    fun open(sendToTeacher: (String) -> Boolean): Conversation {
        val conversation = synchronized(gate) {
            Conversation(++lastId, sendToTeacher).also { open[it.id] = it }
        }
        try {
            screen.changed(conversation)
        } catch (e: RuntimeException) {
            synchronized(gate) { open.remove(conversation.id) }
            throw e
        }
        bringUp(conversation)
        return conversation
    }

    // The teacher's lock screen went: the newest conversation that wanted
    // the screen meanwhile comes up.
    fun unlocked() {
        val id = synchronized(gate) { waiting.lastOrNull().also { waiting.clear() } } ?: return
        conversation(id)?.let { bringUp(it) }
    }

    private fun bringUp(conversation: Conversation) {
        if (isLocked()) {
            synchronized(gate) {
                waiting.remove(conversation.id)
                waiting += conversation.id
            }
            return
        }
        try {
            screen.show(conversation)
        } catch (e: RuntimeException) {
            // Its notification is still there to open it.
        }
    }

    // One conversation with one teacher: what was said, in order.
    inner class Conversation internal constructor(val id: Int, private val sendToTeacher: (String) -> Boolean) {
        private val said = ArrayList<ChatLine>()

        @Volatile var ended = false
            private set

        val lines: List<ChatLine> get() = synchronized(said) { said.toList() }

        // The teacher's last line, for the notification.
        val lastFromTeacher: String?
            get() = synchronized(said) { said.lastOrNull { it.who == ChatLine.Who.TEACHER }?.text }

        // A line from the teacher: the screen comes back to the front.
        fun teacherSaid(text: String) {
            if (ended) return
            add(ChatLine(ChatLine.Who.TEACHER, text))
            bringUp(this)
        }

        // The student's "Enviar": false (and nothing sent) when there is
        // nothing to send or the conversation is over. The line shows at
        // once; if it cannot reach the teacher, a note says so.
        fun reply(typed: String): Boolean {
            val text = typed.trim()
            if (text.isEmpty() || ended) return false
            if (text.length > TextMessage.MAX_LENGTH || !TextMessage.isEncodable(text)) {
                add(ChatLine(ChatLine.Who.NOTE, NOT_ENCODABLE))
                return false
            }
            add(ChatLine(ChatLine.Who.STUDENT, text))
            background {
                val sent = try {
                    sendToTeacher(text)
                } catch (e: RuntimeException) {
                    false
                }
                if (!sent) add(ChatLine(ChatLine.Who.NOTE, NOT_SENT))
            }
            return true
        }

        // The teacher closed the conversation, or the connection dropped:
        // nothing the student types can reach them any more.
        fun end() {
            synchronized(said) {
                if (ended) return
                ended = true
                said += ChatLine(ChatLine.Who.NOTE, ENDED)
            }
            synchronized(gate) {
                open.remove(id)
                waiting.remove(id)
            }
            screen.ended(this)
        }

        private fun add(line: ChatLine) {
            synchronized(said) { said += line }
            screen.changed(this)
        }
    }

    companion object {
        // The PC's words (WinFormsChatWindows).
        const val TITLE = "Conversa com o professor"
        const val SEND = "Enviar"
        const val ENDED = "— O professor encerrou a conversa —"
        const val NOT_SENT = "(não enviada: a conversa caiu)"
        const val NOT_ENCODABLE = "(não enviada: o texto tem um caractere inválido)"

        // The CommandAck's detail when the conversation opens.
        const val OPENED = "conversa aberta"
        const val OPENED_AFTER_LOCK = "a tela está bloqueada: a conversa aparece quando ela for liberada"
        const val NOT_OPENED = "a conversa não pôde ser aberta"
    }
}

// One line of a conversation, as the PC shows it ("Professor: ...",
// "Você: ...", or a note in its own words).
data class ChatLine(val who: Who, val text: String) {
    enum class Who { TEACHER, STUDENT, NOTE }

    val shown: String
        get() = when (who) {
            Who.TEACHER -> "Professor: $text"
            Who.STUDENT -> "Você: $text"
            Who.NOTE -> text
        }
}

// What the student sees of the conversations (ChatActivity and the
// notifications). changed: something new to draw (any thread); show brings
// the conversation's screen to the front and may throw when it cannot be
// started; ended: the conversation is over, its screen says so and closes.
interface ChatScreen {
    fun changed(conversation: TeacherChats.Conversation)

    fun show(conversation: TeacherChats.Conversation)

    fun ended(conversation: TeacherChats.Conversation)
}
