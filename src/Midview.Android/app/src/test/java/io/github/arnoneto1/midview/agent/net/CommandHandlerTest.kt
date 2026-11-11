package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.pairing.PairingFlow
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.screen.ConsentState
import io.github.arnoneto1.midview.agent.screen.ScreenConsent
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.io.IOException
import java.nio.file.Files

class CommandHandlerTest {
    @get:Rule val folder = TemporaryFolder()

    private val teacher = "A".repeat(40)
    private val stranger = "B".repeat(40)

    private val trusted by lazy { TrustedMasters(File(folder.newFolder(), "trusted-masters.txt")) }
    private val pairing by lazy { PairingFlow(identity, trusted, { _, _ -> true }, {}, deviceName = { "Cel 01" }) }
    private var shown = 0
    private val consent = ScreenConsent(clock = { 0L }) { shown++ }
    private val handler by lazy { CommandHandler(trusted, pairing, consent) }

    private fun message(type: Int, payload: ByteArray = ByteArray(0)) = Message(type, payload)

    @Test fun untrustedTeacherCannotSendCommands() {
        trusted.add(teacher)
        val channel = FakeChannel(stranger, message(MessageType.HEARTBEAT), message(MessageType.HEARTBEAT))

        handler.handle(channel)

        // Nothing answered, nothing more read: the server closes the connection.
        assertTrue(channel.sent.isEmpty())
        assertEquals(listOf("receive 2"), channel.events)
        assertFalse(pairing.newTeacherOpen)
    }

    @Test fun untrustedTeacherCannotOpenTheWindowEither() {
        trusted.add(teacher)
        handler.handle(FakeChannel(stranger, message(MessageType.ALLOW_NEW_TEACHER)))
        assertFalse(pairing.newTeacherOpen)
    }

    @Test fun untrustedTeacherMaySendOnlyASmallFirstMessage() {
        val channel = FakeChannel(stranger, message(MessageType.PAIR_REQUEST, ByteArray(CommandHandler.UNTRUSTED_MAX_LENGTH)))
        try {
            handler.handle(channel)
        } catch (e: IOException) {
            // Refused before it is read.
        }
        assertTrue(channel.sent.isEmpty())
    }

    @Test fun anyoneMayAskToPair() {
        val channel = FakeChannel(
            stranger,
            message(MessageType.PAIR_REQUEST, "PROF".toByteArray()),
            message(MessageType.PAIR_NONCE, ByteArray(32)),
            message(MessageType.PAIR_CONFIRM),
        )
        handler.handle(channel)
        assertEquals(MessageType.PAIR_DONE, channel.sent.last().type)
        assertTrue(trusted.isTrusted(stranger))
    }

    @Test fun heartbeatIsAnswered() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, message(MessageType.HEARTBEAT), message(MessageType.HEARTBEAT))
        handler.handle(channel)
        assertEquals(listOf(MessageType.HEARTBEAT, MessageType.HEARTBEAT), channel.sent.map { it.type })
        assertTrue(channel.sent.all { it.payload.isEmpty() })
    }

    @Test fun unknownCommandGetsNotOnPhoneAck() {
        trusted.add(teacher)
        // GetWakeAddress (22) and BlockInternet (7): PC commands.
        val channel = FakeChannel(teacher, message(22), message(7, byteArrayOf(1, 2)))
        handler.handle(channel)

        assertEquals(listOf(MessageType.COMMAND_ACK, MessageType.COMMAND_ACK), channel.sent.map { it.type })
        channel.sent.forEach {
            assertEquals(false to "Ainda não funciona em celular.", CommandResult.decode(it.payload))
        }
    }

    @Test fun allowNewTeacherOpensTheWindow() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, message(MessageType.ALLOW_NEW_TEACHER))
        handler.handle(channel)

        assertTrue(pairing.newTeacherOpen)
        val (succeeded, detail) = CommandResult.decode(channel.sent.single().also { assertEquals(MessageType.COMMAND_ACK, it.type) }.payload)
        assertTrue(succeeded)
        assertTrue(detail, detail.matches(Regex("liberada até \\d\\d:\\d\\d")))
    }

    @Test fun aTrustedTeacherWaitsLongerThanTheHandshake() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, message(MessageType.HEARTBEAT))
        handler.handle(channel)
        assertEquals(CommandHandler.IDLE_TIMEOUT_MILLIS, channel.timeoutMillis)
    }

    @Test fun requestScreenConsentAsksAndAcks() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, message(MessageType.REQUEST_SCREEN_CONSENT))
        handler.handle(channel)

        assertEquals(1, shown)
        assertEquals(ConsentState.Waiting, consent.state)
        assertEquals(true to "pedido mostrado", CommandResult.decode(channel.sent.single().also { assertEquals(MessageType.COMMAND_ACK, it.type) }.payload))
    }

    @Test fun requestScreenConsentWhileLockedSaysItWaits() {
        trusted.add(teacher)
        consent.lockedChanged(true)
        val channel = FakeChannel(teacher, message(MessageType.REQUEST_SCREEN_CONSENT))
        handler.handle(channel)

        assertEquals(0, shown)
        assertEquals(
            false to "a tela está bloqueada: o pedido aparece quando o celular for desbloqueado",
            CommandResult.decode(channel.sent.single().payload),
        )
        // It comes up at the unlock.
        consent.lockedChanged(false)
        assertEquals(1, shown)
    }

    @Test fun requestScreenConsentWhileCapturingChangesNothing() {
        trusted.add(teacher)
        consent.granted()
        val channel = FakeChannel(teacher, message(MessageType.REQUEST_SCREEN_CONSENT))
        handler.handle(channel)

        assertEquals(0, shown)
        assertEquals(ConsentState.Capturing, consent.state)
        assertEquals(true to "a tela já está liberada", CommandResult.decode(channel.sent.single().payload))
    }

    @Test fun requestScreenConsentAfterADenialShowsTheRequest() {
        trusted.add(teacher)
        consent.denied()
        val channel = FakeChannel(teacher, message(MessageType.REQUEST_SCREEN_CONSENT))
        handler.handle(channel)

        assertEquals(1, shown)
        assertEquals(ConsentState.Waiting, consent.state)
        assertEquals(true to "pedido mostrado", CommandResult.decode(channel.sent.single().payload))
    }

    @Test fun requestScreenConsentWhileWaitingShowsTheRequestAgain() {
        trusted.add(teacher)
        consent.ask()
        assertEquals(1, shown)
        val channel = FakeChannel(teacher, message(MessageType.REQUEST_SCREEN_CONSENT))
        handler.handle(channel)

        assertEquals(ConsentState.Waiting, consent.state)
        assertEquals(true to "pedido mostrado", CommandResult.decode(channel.sent.single().payload))
    }

    @Test fun untrustedTeacherCannotRequestScreenConsent() {
        trusted.add(teacher)
        handler.handle(FakeChannel(stranger, message(MessageType.REQUEST_SCREEN_CONSENT)))
        assertEquals(0, shown)
        assertEquals(ConsentState.NotAsked, consent.state)
    }

    private companion object {
        val identity: AgentIdentity by lazy {
            AgentIdentity(Files.createTempDirectory("command-handler").toFile().apply { deleteOnExit() }, "Cel 01")
        }
    }
}
