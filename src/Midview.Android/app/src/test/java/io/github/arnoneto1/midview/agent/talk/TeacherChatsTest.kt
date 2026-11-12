package io.github.arnoneto1.midview.agent.talk

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test

class TeacherChatsTest {
    private var locked = false
    private val events = mutableListOf<String>()
    private var refuse = false
    private val screen = object : ChatScreen {
        override fun changed(conversation: TeacherChats.Conversation) {
            events += "changed ${conversation.id}"
        }

        override fun show(conversation: TeacherChats.Conversation) {
            if (refuse) throw IllegalStateException("no screens from the background")
            events += "show ${conversation.id}"
        }

        override fun ended(conversation: TeacherChats.Conversation) {
            events += "ended ${conversation.id}"
        }
    }

    // The network writes run when the test says (the background thread).
    private val pending = mutableListOf<() -> Unit>()
    private val chats = TeacherChats({ locked }, screen) { pending += it }
    private val toTeacher = mutableListOf<String>()
    private var delivers = true

    private fun open() = chats.open { toTeacher += it; delivers }

    private fun runBackground() {
        pending.toList().forEach { it() }
        pending.clear()
    }

    private fun shown(conversation: TeacherChats.Conversation) = conversation.lines.map { it.shown }

    // A conversation that had to wait (behind a message, or a screen started
    // just as the teacher locked) comes up now, or once the lock goes.
    @Test fun aWaitingConversationComesUpOnlyWithoutTheLock() {
        open()
        events.clear()
        chats.comeUp(1)
        assertEquals(listOf("show 1"), events)

        events.clear()
        locked = true
        chats.comeUp(1)
        assertTrue(events.isEmpty())
        locked = false
        chats.unlocked()
        assertEquals(listOf("show 1"), events)

        events.clear()
        chats.comeUp(99)
        assertTrue(events.isEmpty())
    }

    @Test fun openingBringsTheScreenUp() {
        val chat = open()
        assertEquals(listOf("changed 1", "show 1"), events)
        assertSame(chat, chats.conversation(1))
        assertSame(chat, chats.newest)
        assertTrue(chat.lines.isEmpty())
    }

    // A teacher's line brings the screen back to the front, as on a PC.
    @Test fun theTeachersLinesShowAndBringTheScreenBack() {
        val chat = open()
        events.clear()
        chat.teacherSaid("Terminou o exercício?")
        assertEquals(listOf("Professor: Terminou o exercício?"), shown(chat))
        assertEquals("Terminou o exercício?", chat.lastFromTeacher)
        assertEquals(listOf("changed 1", "show 1"), events)
    }

    @Test fun theStudentsReplyGoesToTheTeacher() {
        val chat = open()
        assertTrue(chat.reply("  terminei  "))
        assertEquals(listOf("Você: terminei"), shown(chat))
        // Off the screen's thread.
        assertTrue(toTeacher.isEmpty())
        runBackground()
        assertEquals(listOf("terminei"), toTeacher)
        assertEquals(listOf("Você: terminei"), shown(chat))
    }

    @Test fun aReplyThatDoesNotGoThroughSaysSo() {
        delivers = false
        val chat = open()
        chat.reply("oi")
        runBackground()
        assertEquals(listOf("Você: oi", "(não enviada: a conversa caiu)"), shown(chat))
    }

    @Test fun aSendThatThrowsIsANotSentLine() {
        val chat = chats.open { throw IllegalStateException("closed") }
        chat.reply("oi")
        runBackground()
        assertEquals(listOf("Você: oi", TeacherChats.NOT_SENT), shown(chat))
    }

    @Test fun nothingTypedSendsNothing() {
        val chat = open()
        assertFalse(chat.reply("   "))
        assertTrue(chat.lines.isEmpty())
        assertTrue(pending.isEmpty())
    }

    @Test fun halfAnEmojiIsNotSent() {
        val chat = open()
        assertFalse(chat.reply("oi \uD83D"))
        assertEquals(listOf(TeacherChats.NOT_ENCODABLE), shown(chat))
        assertTrue(pending.isEmpty())
    }

    // The teacher closed it (or the connection dropped): said, and over.
    @Test fun theEndIsSaidAndNothingMoreGoes() {
        val chat = open()
        chat.teacherSaid("Até amanhã")
        events.clear()
        chat.end()
        assertTrue(chat.ended)
        assertEquals(listOf("Professor: Até amanhã", "— O professor encerrou a conversa —"), shown(chat))
        assertEquals(listOf("ended 1"), events)
        assertNull(chats.conversation(1))
        assertNull(chats.newest)

        assertFalse(chat.reply("tchau"))
        chat.teacherSaid("ainda aí?")
        chat.end()
        assertEquals(2, chat.lines.size)
        assertEquals(listOf("ended 1"), events)
    }

    // Nothing comes up over the teacher's padlock: the newest conversation
    // that wanted the screen comes up when it goes.
    @Test fun underTheTeachersLockTheScreenWaits() {
        locked = true
        val first = open()
        val second = open()
        first.teacherSaid("oi")
        assertTrue(events.none { it.startsWith("show") })

        locked = false
        chats.unlocked()
        assertEquals(listOf("show 1"), events.filter { it.startsWith("show") })
        // Once.
        chats.unlocked()
        assertEquals(1, events.count { it.startsWith("show") })
        assertEquals(2, second.id)
    }

    @Test fun aConversationThatEndedUnderTheLockDoesNotComeUp() {
        locked = true
        open().end()
        locked = false
        chats.unlocked()
        assertTrue(events.none { it.startsWith("show") })
    }

    @Test fun twoTeachersHaveTwoConversations() {
        val one = open()
        val other = chats.open { true }
        assertEquals(1, one.id)
        assertEquals(2, other.id)
        other.teacherSaid("sou o outro professor")
        assertTrue(one.lines.isEmpty())
        one.end()
        assertSame(other, chats.newest)
    }

    // A screen that cannot come up leaves the conversation on (its notification opens it).
    @Test fun aScreenThatCannotComeUpKeepsTheConversation() {
        refuse = true
        val chat = open()
        chat.teacherSaid("oi")
        assertSame(chat, chats.conversation(1))
        assertEquals(listOf("Professor: oi"), shown(chat))
    }
}
