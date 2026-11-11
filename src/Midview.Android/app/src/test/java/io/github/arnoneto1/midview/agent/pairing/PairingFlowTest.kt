package io.github.arnoneto1.midview.agent.pairing

import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.net.FakeChannel
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.Pairing
import io.github.arnoneto1.midview.agent.protocol.TeacherStatus
import org.json.JSONObject
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.nio.file.Files
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class PairingFlowTest {
    @get:Rule val folder = TemporaryFolder()

    private val teacherA = "A".repeat(40)
    private val teacherB = "B".repeat(40)
    private val teacherC = "C".repeat(40)

    private val agentNonce = ByteArray(32) { (it + 1).toByte() }
    private val masterNonce = ByteArray(32) { (100 + it).toByte() }

    private var clock = 1_700_000_000_000L
    private val events = mutableListOf<String>()
    private val shown = mutableListOf<Pair<String, String>>()
    private var hidden = 0

    private val trusted by lazy { TrustedMasters(File(folder.newFolder(), "trusted-masters.txt")) }

    private val flow by lazy {
        PairingFlow(
            identity, trusted,
            showCode = { code, teacher -> events += "show"; shown += code to teacher },
            hideCode = { events += "hide"; hidden++ },
            deviceName = { "Cel 01" },
            newNonce = { agentNonce.copyOf() },
            now = { clock },
        )
    }

    private fun request(name: String) = Message(MessageType.PAIR_REQUEST, name.toByteArray(Charsets.UTF_8))
    private fun nonce(bytes: ByteArray = masterNonce) = Message(MessageType.PAIR_NONCE, bytes)
    private fun confirm() = Message(MessageType.PAIR_CONFIRM, ByteArray(0))
    private fun cancel() = Message(MessageType.PAIR_CANCEL, ByteArray(0))

    private fun teacher(thumb: String, vararg script: Message) = FakeChannel(thumb, *script, events = events)

    private fun refusal(channel: FakeChannel): String {
        assertEquals(listOf(MessageType.PAIR_REFUSED), channel.sent.map { it.type })
        return Pairing.refusalText(channel.sent[0].payload)
    }

    private fun pairs(thumb: String): FakeChannel {
        val channel = teacher(thumb, nonce(), confirm())
        flow.run(channel, request("PROF"))
        return channel
    }

    @Test fun firstTeacherPairsAndIsTrusted() {
        val channel = teacher(teacherA, request("PROF-01 <sala 2>"), nonce(), confirm())
        flow.run(channel)

        assertEquals(
            listOf(MessageType.PAIR_COMMIT, MessageType.PAIR_REVEAL, MessageType.PAIR_DONE),
            channel.sent.map { it.type },
        )
        assertArrayEquals(Pairing.commit(agentNonce), channel.sent[0].payload)
        assertArrayEquals(agentNonce, channel.sent[1].payload)

        val code = Pairing.code(teacherA, identity.thumbprint, agentNonce, masterNonce)
        assertEquals(listOf(Pairing.format(code) to "PROF-01 sala 2"), shown)
        // The code is on the phone before the teacher can have it, and goes
        // away when the pairing ends.
        assertEquals(
            listOf("receive 37", "send 38", "receive 39", "show", "send 40", "receive 41", "send 43", "hide"),
            events,
        )

        assertTrue(trusted.isTrusted(teacherA))
        val result = JSONObject(String(channel.sent[2].payload, Charsets.UTF_8))
        assertEquals("Cel 01", result.getString("name"))
        assertEquals(identity.thumbprint, result.getString("session"))
        assertTrue(result.isNull("mac"))
        assertEquals("android", result.getString("kind"))
    }

    @Test fun secondTeacherRefusedWithoutAllowNewTeacher() {
        pairs(teacherA)
        shown.clear()

        val channel = teacher(teacherB, nonce(), confirm())
        flow.run(channel, request("PROF-02"))

        assertEquals("Este celular já tem professor. Peça para ele usar \"Permitir novo professor\".", refusal(channel))
        assertFalse(trusted.isTrusted(teacherB))
        assertTrue(shown.isEmpty())
        assertEquals(TeacherStatus.OTHERS, flow.teachersSeenBy(teacherB))
    }

    @Test fun secondTeacherPairsInsideTheTenMinuteWindow() {
        pairs(teacherA)
        assertFalse(flow.newTeacherOpen)

        val until = Date(clock + 10 * 60 * 1000L)
        assertEquals(true to "liberada até ${SimpleDateFormat("HH:mm", Locale.ROOT).format(until)}", flow.allowNewTeacher())
        assertTrue(flow.newTeacherOpen)

        clock += 9 * 60 * 1000L
        assertEquals(MessageType.PAIR_DONE, pairs(teacherB).sent.last().type)
        assertTrue(trusted.isTrusted(teacherB))

        // The window lets one teacher in.
        assertFalse(flow.newTeacherOpen)
        val third = teacher(teacherC, nonce(), confirm())
        flow.run(third, request("PROF-03"))
        assertEquals(MessageType.PAIR_REFUSED, third.sent.single().type)
    }

    @Test fun theWindowEndsAfterTenMinutes() {
        pairs(teacherA)
        flow.allowNewTeacher()
        clock += 10 * 60 * 1000L
        assertFalse(flow.newTeacherOpen)

        val channel = teacher(teacherB, nonce(), confirm())
        flow.run(channel, request("PROF-02"))
        assertEquals(MessageType.PAIR_REFUSED, channel.sent.single().type)
    }

    @Test fun aTrustedTeacherMayPairAgain() {
        pairs(teacherA)
        assertEquals(MessageType.PAIR_DONE, pairs(teacherA).sent.last().type)
        assertEquals(1, trusted.count())
    }

    @Test fun cancelLeavesNothingTrusted() {
        val channel = teacher(teacherA, nonce(), cancel())
        flow.run(channel, request("PROF-01"))

        assertEquals(listOf(MessageType.PAIR_COMMIT, MessageType.PAIR_REVEAL), channel.sent.map { it.type })
        assertEquals(0, trusted.count())
        assertEquals(1, shown.size)
        assertEquals(1, hidden)
        assertEquals(TeacherStatus.NONE, flow.teachersSeenBy(teacherA))
    }

    @Test fun aDroppedConnectionTakesTheCodeDown() {
        val channel = teacher(teacherA, nonce())
        try {
            flow.run(channel, request("PROF-01"))
        } catch (e: java.io.IOException) {
            // The connection's end surfaces here, as on Windows.
        }
        assertEquals(1, hidden)
        assertEquals(0, trusted.count())
    }

    // The teacher's side refuses a reveal that doesn't open the commitment
    // (PairingSession): the phone's always does, and the code needs both.
    @Test fun commitThatDoesNotOpenIsRefused() {
        val channel = pairs(teacherA)
        val commitment = channel.sent[0].payload
        assertTrue(Pairing.opens(commitment, channel.sent[1].payload))
        assertFalse(Pairing.opens(commitment, masterNonce))
        assertFalse(Pairing.opens(commitment.copyOf(31), channel.sent[1].payload))
    }

    @Test fun aMasterNonceOfTheWrongSizeEndsThePairing() {
        val channel = teacher(teacherA, nonce(ByteArray(31)), confirm())
        flow.run(channel, request("PROF-01"))

        assertEquals(listOf(MessageType.PAIR_COMMIT), channel.sent.map { it.type })
        assertTrue(shown.isEmpty())
        assertEquals(0, trusted.count())
    }

    @Test fun anythingButTheNonceEndsThePairing() {
        val channel = teacher(teacherA, confirm(), confirm())
        flow.run(channel, request("PROF-01"))

        assertEquals(listOf(MessageType.PAIR_COMMIT), channel.sent.map { it.type })
        assertEquals(0, trusted.count())
    }

    @Test fun aConfirmAfterFiveMinutesIsRefused() {
        val channel = teacher(teacherA, nonce(), confirm())
        channel.onReceive = { if (it.type == MessageType.PAIR_CONFIRM) clock += 5 * 60 * 1000L + 1 }
        flow.run(channel, request("PROF-01"))

        assertEquals("O tempo para conferir o código acabou. Tente de novo.", Pairing.refusalText(channel.sent.last().payload))
        assertEquals(0, trusted.count())
        assertEquals(1, hidden)
    }

    @Test fun onePairingAtATime() {
        val second = teacher(teacherB, nonce(), confirm())
        val first = FakeChannel(teacherA, nonce(), confirm())
        // While the first teacher's code is up, a second one asks.
        val busyFlow = PairingFlow(
            identity, trusted,
            showCode = { _, _ -> if (second.sent.isEmpty()) flowRef!!.run(second, request("PROF-02")) },
            hideCode = {},
            deviceName = { "Cel 01" },
        )
        flowRef = busyFlow
        busyFlow.run(first, request("PROF-01"))

        assertEquals("Outro pareamento está em andamento neste celular.", refusal(second))
        assertEquals(MessageType.PAIR_DONE, first.sent.last().type)
        assertTrue(trusted.isTrusted(teacherA))
        assertFalse(trusted.isTrusted(teacherB))
    }

    private var flowRef: PairingFlow? = null

    @Test fun thePairingWaitsLongEnoughForTheTeacher() {
        val channel = pairs(teacherA)
        assertTrue(channel.timeoutMillis > Pairing.CONFIRM_TIMEOUT_MILLIS)
    }

    @Test fun teachersAsTheAskingTeacherSeesThem() {
        assertEquals(TeacherStatus.NONE, flow.teachersSeenBy(teacherA))
        pairs(teacherA)
        assertEquals(TeacherStatus.YOU, flow.teachersSeenBy(teacherA.lowercase()))
        assertEquals(TeacherStatus.OTHERS, flow.teachersSeenBy(teacherB))
    }

    private companion object {
        // One RSA key for the whole class: making one takes a moment.
        val identity: AgentIdentity by lazy {
            AgentIdentity(Files.createTempDirectory("pairing-flow").toFile().apply { deleteOnExit() }, "Cel 01")
        }
    }
}
