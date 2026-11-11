package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.pairing.PairingFlow
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
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
    private val pairing by lazy { PairingFlow(identity, trusted, { _, _ -> }, {}, deviceName = { "Cel 01" }) }
    private val handler by lazy { CommandHandler(trusted, pairing) }

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

    private companion object {
        val identity: AgentIdentity by lazy {
            AgentIdentity(Files.createTempDirectory("command-handler").toFile().apply { deleteOnExit() }, "Cel 01")
        }
    }
}
