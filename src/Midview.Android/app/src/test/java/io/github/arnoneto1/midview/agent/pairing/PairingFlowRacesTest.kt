package io.github.arnoneto1.midview.agent.pairing

import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.net.FakeChannel
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.NewTeacherInvite
import io.github.arnoneto1.midview.agent.protocol.Pairing
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.io.IOException
import java.nio.file.Files

// What changes while a pairing waits for the teacher to compare the codes:
// right before trusting, the phone checks again that what let the pairing in
// still holds. And how long a connection that never sends its nonce may
// hold the one pairing.
class PairingFlowRacesTest {
    @get:Rule val folder = TemporaryFolder()

    private val teacherA = "A".repeat(40)
    private val teacherB = "B".repeat(40)

    private val agentNonce = ByteArray(32) { (it + 1).toByte() }
    private val masterNonce = ByteArray(32) { (100 + it).toByte() }
    private var uptime = 60_000L

    private val trusted by lazy { TrustedMasters(File(folder.newFolder(), "trusted-masters.txt")) }

    private val flow by lazy {
        PairingFlow(
            identity, trusted,
            showCode = { _, _ -> true },
            hideCode = {},
            deviceName = { "Cel 01" },
            newNonce = { agentNonce.copyOf() },
            now = { 1_700_000_000_000L },
            elapsed = { uptime },
        )
    }

    private fun request() = Message(MessageType.PAIR_REQUEST, "PROF".toByteArray(Charsets.UTF_8))
    private fun nonce() = Message(MessageType.PAIR_NONCE, masterNonce)

    private fun confirm(thumb: String, code: String?) = Message(
        MessageType.PAIR_CONFIRM,
        if (code == null) {
            ByteArray(0)
        } else {
            NewTeacherInvite.proof(NewTeacherInvite.key(code, identity.thumbprint), thumb, identity.thumbprint, agentNonce, masterNonce)
        },
    )

    private fun allow(code: String = CODE) = flow.allowNewTeacher(code.toByteArray(Charsets.US_ASCII))

    // Pairs, with something happening on the phone as the confirmation comes.
    private fun pairsWhile(thumb: String, code: String?, meanwhile: () -> Unit): FakeChannel {
        val channel = FakeChannel(thumb, nonce(), confirm(thumb, code))
        channel.onReceive = { if (it.type == MessageType.PAIR_CONFIRM) meanwhile() }
        flow.run(channel, request())
        return channel
    }

    private fun refusal(channel: FakeChannel) = Pairing.refusalText(channel.sent.last().payload)

    // A trusted teacher about to be forgotten keeps a pairing pending: it
    // must not come back once forgotten.
    @Test fun aTeacherForgottenWhilePairingAgainIsNotAddedBack() {
        trusted.add(teacherA)
        trusted.add(teacherB)
        val channel = pairsWhile(teacherB, null) { flow.forgetOtherTeachers(teacherA) }

        assertEquals(PairingFlow.NO_LONGER_ALLOWED, refusal(channel))
        assertTrue(trusted.isTrusted(teacherA))
        assertFalse(trusted.isTrusted(teacherB))
    }

    @Test fun aCodeDrawnAgainWhilePairingTurnsTheOldOneAway() {
        trusted.add(teacherA)
        allow("AAAAAAAA")
        val channel = pairsWhile(teacherB, "AAAAAAAA") { allow("BBBBBBBB") }

        assertEquals(PairingFlow.NO_LONGER_ALLOWED, refusal(channel))
        assertFalse(trusted.isTrusted(teacherB))
        assertTrue(flow.newTeacherOpen)
    }

    @Test fun aWindowClosedWhilePairingTurnsTheCodeHolderAway() {
        trusted.add(teacherA)
        allow()
        val channel = pairsWhile(teacherB, CODE) { flow.forgetOtherTeachers(teacherA) }

        assertEquals(PairingFlow.NO_LONGER_ALLOWED, refusal(channel))
        assertFalse(trusted.isTrusted(teacherB))
    }

    @Test fun aWindowThatEndedWhilePairingTurnsTheCodeHolderAway() {
        trusted.add(teacherA)
        allow()
        uptime += Pairing.NEW_TEACHER_WINDOW_MILLIS - 60_000L
        val channel = pairsWhile(teacherB, CODE) { uptime += 120_000L }

        assertEquals(PairingFlow.NO_LONGER_ALLOWED, refusal(channel))
        assertFalse(trusted.isTrusted(teacherB))
    }

    @Test fun theRightCodeStillGetsInAndIsUsedUp() {
        trusted.add(teacherA)
        allow()
        val channel = pairsWhile(teacherB, CODE) {}

        assertEquals(MessageType.PAIR_DONE, channel.sent.last().type)
        assertTrue(trusted.isTrusted(teacherB))
        assertFalse(flow.newTeacherOpen)
    }

    // Two teachers forget each other at once: the one forgotten first must
    // not end up the only teacher.
    @Test fun forgetFromATeacherForgottenAlreadyChangesNothing() {
        trusted.add(teacherA)
        trusted.add(teacherB)
        flow.forgetOtherTeachers(teacherA)

        assertEquals(false to PairingFlow.NOT_ON_LIST, flow.forgetOtherTeachers(teacherB))
        assertTrue(trusted.isTrusted(teacherA))
        assertFalse(trusted.isTrusted(teacherB))
    }

    // Until the teacher's nonce is in, the connection keeps its short timeout
    // and a stranger's deadline: holding the one pairing costs a code.
    @Test fun theLongWaitStartsOnlyWithTheTeachersNonce() {
        val stalled = FakeChannel(teacherA)
        try {
            flow.run(stalled, request())
        } catch (e: IOException) {
            // The short timeout ran out.
        }
        assertFalse(stalled.pairing)
        assertEquals(0, stalled.timeoutMillis)

        val wrong = FakeChannel(teacherA, Message(MessageType.PAIR_CANCEL, ByteArray(0)))
        flow.run(wrong, request())
        assertFalse(wrong.pairing)
        assertEquals(0, wrong.timeoutMillis)

        val channel = FakeChannel(teacherA, nonce(), confirm(teacherA, null))
        channel.onReceive = {
            if (it.type == MessageType.PAIR_NONCE) {
                assertFalse(channel.pairing)
                assertEquals(0, channel.timeoutMillis)
            }
        }
        flow.run(channel, request())
        assertTrue(channel.pairing)
        assertTrue(channel.timeoutMillis > Pairing.CONFIRM_TIMEOUT_MILLIS)
    }

    private companion object {
        const val CODE = "K7PQ2M94"

        val identity: AgentIdentity by lazy {
            AgentIdentity(Files.createTempDirectory("pairing-races").toFile().apply { deleteOnExit() }, "Cel 01")
        }
    }
}
