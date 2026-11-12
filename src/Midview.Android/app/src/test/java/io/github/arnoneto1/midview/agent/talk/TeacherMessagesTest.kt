package io.github.arnoneto1.midview.agent.talk

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class TeacherMessagesTest {
    private var locked = false
    private val events = mutableListOf<String>()
    private var refuse = false
    private val screen = object : MessageScreen {
        override fun present() {
            if (refuse) throw IllegalStateException("no screens from the background")
            events += "present"
        }

        override fun dismiss() {
            events += "dismiss"
        }
    }
    private val messages = TeacherMessages({ locked }, screen)

    private fun send(text: String) = messages.receive(text.toByteArray(Charsets.UTF_8))

    @Test fun aMessageComesUpAtOnceAndIsAcked() {
        assertEquals(true to TeacherMessages.SHOWN, send("  Atenção, turma!  "))
        assertEquals(listOf("present"), events)
        assertEquals("Atenção, turma!", messages.newest)
        assertEquals(TeacherMessages.HEADER, TeacherMessages.header(messages.count))
    }

    @Test fun okClosesIt() {
        send("Atenção")
        messages.ok()
        assertNull(messages.newest)
        assertEquals(listOf("present", "dismiss"), events)
    }

    // As the PC's windows: the newest on top, OK brings back the one before.
    @Test fun severalStackNewestFirst() {
        send("primeira")
        send("segunda")
        send("terceira")
        assertEquals("terceira", messages.newest)
        assertEquals("Mensagem do professor (mais 2 depois desta)", TeacherMessages.header(messages.count))

        messages.ok()
        assertEquals("segunda", messages.newest)
        assertEquals("Mensagem do professor (mais 1 depois desta)", TeacherMessages.header(messages.count))
        messages.ok()
        assertEquals("primeira", messages.newest)
        messages.ok()
        assertNull(messages.newest)
        assertEquals(listOf("present", "present", "present", "dismiss"), events)
    }

    // Nothing comes up over the teacher's padlock: it waits for the unlock.
    @Test fun underTheTeachersLockItWaits() {
        locked = true
        assertEquals(true to TeacherMessages.AFTER_LOCK, send("Prestem atenção"))
        assertEquals(emptyList<String>(), events)
        messages.present()
        assertEquals(emptyList<String>(), events)

        locked = false
        messages.unlocked()
        assertEquals(listOf("present"), events)
        assertEquals("Prestem atenção", messages.newest)
    }

    @Test fun anUnlockWithNothingWaitingShowsNothing() {
        messages.unlocked()
        assertEquals(emptyList<String>(), events)
    }

    @Test fun nothingWorthShowingIsRefused() {
        assertEquals(false to TeacherMessages.INVALID, send("   "))
        assertEquals(false to TeacherMessages.INVALID, messages.receive(byteArrayOf(0xC3.toByte())))
        assertEquals(false to TeacherMessages.INVALID, send("a".repeat(2001)))
        assertEquals(0, messages.count)
        assertEquals(emptyList<String>(), events)
    }

    @Test fun aScreenThatCannotComeUpIsAFailure() {
        refuse = true
        assertEquals(false to TeacherMessages.NOT_SHOWN, send("oi"))
        assertEquals(0, messages.count)
    }

    // "Fechar programas", as on a PC: shown and waiting alike.
    @Test fun closeAllTakesEveryMessage() {
        send("uma")
        locked = true
        send("outra, sob o bloqueio")
        messages.closeAll()
        assertEquals(0, messages.count)
        locked = false
        messages.unlocked()
        assertEquals(listOf("present", "dismiss"), events)
    }
}
