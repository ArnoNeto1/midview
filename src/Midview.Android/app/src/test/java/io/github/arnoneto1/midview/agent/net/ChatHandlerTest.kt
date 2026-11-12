package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.TextMessage
import io.github.arnoneto1.midview.agent.talk.ChatScreen
import io.github.arnoneto1.midview.agent.talk.TeacherChats
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.IOException
import java.net.SocketTimeoutException

class ChatHandlerTest {
    private val teacher = "A".repeat(40)
    private var locked = false
    private val ended = mutableListOf<TeacherChats.Conversation>()
    private var refuse = false
    private val screen = object : ChatScreen {
        override fun changed(conversation: TeacherChats.Conversation) {}

        override fun show(conversation: TeacherChats.Conversation) {}

        override fun ended(conversation: TeacherChats.Conversation) {
            ended += conversation
        }
    }
    private val chats = TeacherChats({ locked }, screen) { it() }

    private fun text(line: String) = Message(MessageType.CHAT_TEXT, TextMessage.encode(line))

    private fun heartbeat() = Message(MessageType.HEARTBEAT, ByteArray(0))

    // The ack comes first; then the teacher's lines show, heartbeats only
    // keep it up, and the end of the connection ends the conversation.
    @Test fun theTeachersLinesShowUntilTheConnectionEnds() {
        val channel = FakeChannel(teacher, heartbeat(), text("Bom dia!"), heartbeat(), text("  Tudo certo?  "))

        ChatHandler(channel, chats).run()

        val ack = channel.sent.first()
        assertEquals(MessageType.COMMAND_ACK, ack.type)
        assertEquals(true to TeacherChats.OPENED, CommandResult.decode(ack.payload))
        assertEquals(ChatHandler.SILENCE_MILLIS, channel.timeoutMillis)
        val conversation = ended.single()
        assertEquals(
            listOf("Professor: Bom dia!", "Professor: Tudo certo?", TeacherChats.ENDED),
            conversation.lines.map { it.shown },
        )
        assertNull(chats.newest)
    }

    // The student's line goes back over the same connection, as a ChatText.
    @Test fun theStudentsReplyGoesBackAsChatText() {
        val channel = FakeChannel(teacher, text("Terminou?"))
        channel.onReceive = { chats.newest?.reply("sim, terminei") }

        ChatHandler(channel, chats).run()

        assertEquals(listOf(MessageType.COMMAND_ACK, MessageType.CHAT_TEXT), channel.sent.map { it.type })
        assertEquals("sim, terminei", TextMessage.decode(channel.sent[1].payload))
    }

    @Test fun aReplyAfterTheEndIsNotSent() {
        val channel = FakeChannel(teacher)
        ChatHandler(channel, chats).run()
        assertFalse(ended.single().reply("oi?"))
        assertEquals(1, channel.sent.size)
    }

    // A reply that cannot be written says so in the conversation.
    @Test fun aReplyThatCannotBeWrittenSaysSo() {
        val channel = object : Channel by FakeChannel(teacher, text("oi")) {
            var acked = false
            override fun send(message: Message) {
                if (acked) throw IOException("broken pipe")
                acked = true
            }
        }
        var conversation: TeacherChats.Conversation? = null
        val wrapped = object : Channel by channel {
            override fun receive(maxLength: Int): Message {
                val message = channel.receive(maxLength)
                conversation = chats.newest
                conversation?.reply("estou aqui")
                return message
            }
        }

        ChatHandler(wrapped, chats).run()

        assertEquals(listOf("Você: estou aqui", TeacherChats.NOT_SENT), conversation!!.lines.take(2).map { it.shown })
    }

    // The teacher's PC went silent: the conversation ends after 15 s.
    @Test fun silenceEndsTheConversation() {
        val channel = object : Channel by FakeChannel(teacher) {
            override fun receive(maxLength: Int): Message = throw SocketTimeoutException("Read timed out")
        }
        ChatHandler(channel, chats).run()
        assertTrue(ended.single().ended)
    }

    @Test fun nonsenseIsIgnored() {
        val channel = FakeChannel(
            teacher,
            Message(MessageType.CHAT_TEXT, byteArrayOf(0xC3.toByte())),
            Message(MessageType.CHAT_TEXT, "   ".toByteArray()),
            Message(MessageType.SHOW_MESSAGE, "não é daqui".toByteArray()),
        )
        ChatHandler(channel, chats).run()
        assertEquals(listOf(TeacherChats.ENDED), ended.single().lines.map { it.shown })
    }

    @Test fun aLineLongerThanAnyTeacherSendsEndsIt() {
        val channel = FakeChannel(teacher, Message(MessageType.CHAT_TEXT, ByteArray(TextMessage.MAX_BYTES + 1) { 'a'.code.toByte() }))
        ChatHandler(channel, chats).run()
        assertEquals(listOf(TeacherChats.ENDED), ended.single().lines.map { it.shown })
    }

    // Under the teacher's lock the conversation opens, and says it shows later.
    @Test fun underTheLockTheAckSaysItWaits() {
        locked = true
        val channel = FakeChannel(teacher)
        ChatHandler(channel, chats).run()
        assertEquals(true to TeacherChats.OPENED_AFTER_LOCK, CommandResult.decode(channel.sent.single().payload))
    }

    @Test fun aConversationThatCannotOpenIsRefused() {
        val refusing = TeacherChats({ false }, object : ChatScreen {
            override fun changed(conversation: TeacherChats.Conversation) = throw IllegalStateException("no")
            override fun show(conversation: TeacherChats.Conversation) {}
            override fun ended(conversation: TeacherChats.Conversation) {}
        }) { it() }
        val channel = FakeChannel(teacher, text("oi"))
        ChatHandler(channel, refusing).run()
        assertEquals(false to TeacherChats.NOT_OPENED, CommandResult.decode(channel.sent.single().payload))
        // Nothing more read.
        assertTrue(channel.events.none { it.startsWith("receive") })
    }
}
