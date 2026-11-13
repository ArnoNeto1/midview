package io.github.arnoneto1.midview.agent.agent

import io.github.arnoneto1.midview.agent.control.ControlNotice
import io.github.arnoneto1.midview.agent.net.LockScreenHandler
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Pairing
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertSame
import org.junit.Test

class DeviceWordTest {
    @After fun backToAPhone() {
        DeviceWord.tablet = false
    }

    @Test fun aPhoneSaysExactlyWhatItSaid() {
        val text = AgentService.STATUS_TEXT
        assertSame(text, DeviceWord.say(text))
        assertEquals("Celular da escola, gerenciado pelo Midview", DeviceWord.say(text))
        val ack = CommandResult.encode(false, LockScreenHandler.NO_SCREEN_LOCK)
        assertEquals("este celular não bloqueia a tela", CommandResult.decode(ack).second)
    }

    @Test fun aTabletSaysTablet() {
        DeviceWord.tablet = true
        assertEquals("Tablet da escola, gerenciado pelo Midview", DeviceWord.say(AgentService.STATUS_TEXT))
        assertEquals("Este tablet está sendo controlado pelo professor", DeviceWord.say(ControlNotice.TEXT))
        assertEquals("o celularzinho fica; o tablet não conseguiu guardar o PIN",
            DeviceWord.say("o celularzinho fica; o celular não conseguiu guardar o PIN"))
    }

    // "celulares" are the class's devices, phones and tablets alike.
    @Test fun thePluralStays() {
        DeviceWord.tablet = true
        assertEquals("aparece em todos os computadores e celulares da sala",
            DeviceWord.say("aparece em todos os computadores e celulares da sala"))
    }

    // What the teacher reads back names the tablet too.
    @Test fun theTeachersAnswersSayTablet() {
        DeviceWord.tablet = true
        val ack = CommandResult.encode(false, LockScreenHandler.NO_SCREEN_LOCK)
        assertEquals(false to "este tablet não bloqueia a tela", CommandResult.decode(ack))
        val refused = Pairing.refused("Outro pareamento está em andamento neste celular.")
        assertEquals("Outro pareamento está em andamento neste tablet.", Pairing.refusalText(refused.payload))
    }
}
