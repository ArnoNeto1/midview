package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.TextMessage
import io.github.arnoneto1.midview.agent.talk.TeacherChats
import java.io.IOException

// "Bate-papo" on the commands port, after a trusted teacher's ChatOpen
// (CommandHandler hands the connection over): as the PC Session's ServeChat,
// the connection lasts the whole conversation, ChatText lines flow both ways
// and the teacher's Heartbeats (every 5 s) keep it up while nobody types. A
// Session sends no answer to ChatOpen; here it gets one CommandAck first
// (CommandResult), which lets the teacher's program tell a phone that chats
// from one too old to (whose answer is "Ainda não funciona em celular.").
// The conversation ends however the connection does: closed by the teacher,
// broken, or silent for silenceMillis (the Session's 15 s).
// This thread only receives; the student's lines are written from another
// one, both under sendLock, the ack first.
class ChatHandler(
    private val channel: Channel,
    private val chats: TeacherChats,
    private val silenceMillis: Int = SILENCE_MILLIS,
) {
    private val sendLock = Any()

    // Returns when the conversation is over; the caller closes the connection.
    fun run() {
        val conversation = synchronized(sendLock) {
            val opened = try {
                chats.open(::sendToTeacher)
            } catch (e: RuntimeException) {
                null
            }
            val detail = when {
                opened == null -> TeacherChats.NOT_OPENED
                chats.locked -> TeacherChats.OPENED_AFTER_LOCK
                else -> TeacherChats.OPENED
            }
            try {
                channel.send(Message(MessageType.COMMAND_ACK, CommandResult.encode(opened != null, detail)))
            } catch (e: IOException) {
                opened?.end()
                return
            }
            opened
        } ?: return

        try {
            channel.setTimeout(silenceMillis)
            while (true) {
                val message = channel.receive(MAX_MESSAGE_LENGTH)
                // Heartbeats only keep the connection up; nothing else to do.
                if (message.type == MessageType.CHAT_TEXT) {
                    TextMessage.decode(message.payload)?.let(conversation::teacherSaid)
                }
            }
        } catch (e: IOException) {
            // The teacher closed it, it broke, or went silent.
        } finally {
            conversation.end()
        }
    }

    // Called on a background thread with a line the student typed.
    private fun sendToTeacher(text: String): Boolean = try {
        synchronized(sendLock) {
            channel.send(Message(MessageType.CHAT_TEXT, TextMessage.encode(text)))
        }
        true
    } catch (e: IOException) {
        false
    }

    companion object {
        // The Session's ConnectionTimeoutMs; the Master beats every 5 s.
        const val SILENCE_MILLIS = 15_000

        // A line of TextMessage.MAX_LENGTH, and its type.
        const val MAX_MESSAGE_LENGTH = TextMessage.MAX_BYTES + 1
    }
}
