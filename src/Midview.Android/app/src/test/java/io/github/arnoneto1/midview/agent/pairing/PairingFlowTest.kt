package io.github.arnoneto1.midview.agent.pairing

import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.net.FakeChannel
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.protocol.NewTeacherInvite
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

    // The time since the phone started, which nobody can set.
    private var uptime = 60_000L

    // Both clocks move on.
    private fun pass(millis: Long) {
        clock += millis
        uptime += millis
    }
    private val events = mutableListOf<String>()
    private val shown = mutableListOf<Pair<String, String>>()
    private var hidden = 0
    private var canShow = true

    private val trusted by lazy { TrustedMasters(File(folder.newFolder(), "trusted-masters.txt")) }

    private val flow by lazy {
        PairingFlow(
            identity, trusted,
            showCode = { code, teacher ->
                events += "show"
                shown += code to teacher
                canShow
            },
            hideCode = { events += "hide"; hidden++ },
            deviceName = { "Cel 01" },
            newNonce = { agentNonce.copyOf() },
            now = { clock },
            elapsed = { uptime },
        )
    }

    private fun request(name: String) = Message(MessageType.PAIR_REQUEST, name.toByteArray(Charsets.UTF_8))
    private fun nonce(bytes: ByteArray = masterNonce) = Message(MessageType.PAIR_NONCE, bytes)
    private fun confirm() = Message(MessageType.PAIR_CONFIRM, ByteArray(0))

    // A new teacher who typed this code: the proof, from the phone's nonce.
    private fun confirmWithCode(thumb: String, code: String = CODE) = Message(
        MessageType.PAIR_CONFIRM,
        NewTeacherInvite.proof(NewTeacherInvite.key(code, identity.thumbprint), thumb, identity.thumbprint, agentNonce, masterNonce),
    )

    private fun allow(code: String = CODE) = flow.allowNewTeacher(code.toByteArray(Charsets.US_ASCII))

    private fun pairsWithCode(thumb: String, code: String = CODE): FakeChannel {
        val channel = teacher(thumb, nonce(), confirmWithCode(thumb, code))
        flow.run(channel, request("PROF"))
        return channel
    }
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
        assertEquals(true to "liberada até ${SimpleDateFormat("HH:mm", Locale.ROOT).format(until)} com o código", allow())
        assertTrue(flow.newTeacherOpen)

        pass(9 * 60 * 1000L)
        assertEquals(MessageType.PAIR_DONE, pairsWithCode(teacherB).sent.last().type)
        assertTrue(trusted.isTrusted(teacherB))

        // The code lets one teacher in.
        assertFalse(flow.newTeacherOpen)
        val third = teacher(teacherC, nonce(), confirmWithCode(teacherC))
        flow.run(third, request("PROF-03"))
        assertEquals(MessageType.PAIR_REFUSED, third.sent.single().type)
    }

    // A Master from before the code: opening for it would let anyone in.
    @Test fun anOldMastersAllowNewTeacherOpensNothing() {
        pairs(teacherA)
        assertEquals(false to PairingFlow.OLD_TEACHER_PROGRAM, flow.allowNewTeacher(ByteArray(0)))
        assertEquals(false to "pedido inválido", flow.allowNewTeacher("K7PQ2M9U".toByteArray(Charsets.US_ASCII)))
        assertFalse(flow.newTeacherOpen)
    }

    @Test fun insideTheWindowATeacherWithoutTheCodeIsRefused() {
        pairs(teacherA)
        allow()
        val channel = pairs(teacherB)

        assertEquals(PairingFlow.NEEDS_CODE, Pairing.refusalText(channel.sent.last().payload))
        assertFalse(trusted.isTrusted(teacherB))
        assertTrue(flow.newTeacherOpen)
    }

    @Test fun aWrongCodeIsRefusedAndTheRightOneStillGetsIn() {
        pairs(teacherA)
        allow()
        val wrong = pairsWithCode(teacherB, "K7PQ2M95")
        assertEquals(PairingFlow.WRONG_CODE, Pairing.refusalText(wrong.sent.last().payload))
        assertFalse(trusted.isTrusted(teacherB))
        assertTrue(flow.newTeacherOpen)

        assertEquals(MessageType.PAIR_DONE, pairsWithCode(teacherC).sent.last().type)
    }

    // A proof made for another teacher's certificate is no proof.
    @Test fun aProofForAnotherTeacherIsRefused() {
        pairs(teacherA)
        allow()
        val channel = teacher(teacherB, nonce(), confirmWithCode(teacherC))
        flow.run(channel, request("PROF-02"))
        assertEquals(PairingFlow.WRONG_CODE, Pairing.refusalText(channel.sent.last().payload))
        assertFalse(trusted.isTrusted(teacherB))
    }

    // Asking again draws a new code and starts the tries over.
    @Test fun allowingAgainReplacesTheCodeAndTheTries() {
        pairs(teacherA)
        allow("AAAAAAAA")
        repeat(NewTeacherInvite.MAX_TRIES - 1) { flow.run(teacher(teacherB, nonce(), cancel()), request("ALUNO")) }
        allow("BBBBBBBB")
        pairsWithCode(teacherB, "AAAAAAAA")
        assertTrue(flow.newTeacherOpen)
        assertEquals(MessageType.PAIR_DONE, pairsWithCode(teacherB, "BBBBBBBB").sent.last().type)
    }

    // Only a new teacher needs the code: a first one's payload means nothing.
    @Test fun aFirstTeachersProofIsIgnored() {
        assertEquals(MessageType.PAIR_DONE, pairsWithCode(teacherA, "AAAAAAAA").sent.last().type)
    }

    @Test fun forgetOtherTeachersKeepsTheSenderAndClosesTheWindow() {
        pairs(teacherA)
        allow()
        pairsWithCode(teacherB)
        allow()
        assertEquals(true to "1 professor esquecido", flow.forgetOtherTeachers(teacherB.lowercase()))

        assertFalse(trusted.isTrusted(teacherA))
        assertTrue(trusted.isTrusted(teacherB))
        assertFalse(flow.newTeacherOpen)
        assertEquals(TeacherStatus.OTHERS, flow.teachersSeenBy(teacherA))
        assertEquals(true to "nenhum outro professor", flow.forgetOtherTeachers(teacherB))
    }

    @Test fun theWindowEndsAfterTenMinutes() {
        pairs(teacherA)
        allow()
        pass(10 * 60 * 1000L)
        assertFalse(flow.newTeacherOpen)

        val channel = teacher(teacherB, nonce(), confirmWithCode(teacherB))
        flow.run(channel, request("PROF-02"))
        assertEquals(MessageType.PAIR_REFUSED, channel.sent.single().type)
    }

    // A student who sets the date back in Settings does not keep the
    // window open: it runs on the time since the phone started.
    @Test fun settingTheClockBackDoesNotKeepTheWindowOpen() {
        pairs(teacherA)
        allow()
        clock -= 24 * 60 * 60 * 1000L
        uptime += 10 * 60 * 1000L
        assertFalse(flow.newTeacherOpen)
    }

    // Nor does it make a late confirmation look in time.
    @Test fun aLateConfirmIsRefusedWhateverTheWallClockSays() {
        val channel = teacher(teacherA, nonce(), confirm())
        channel.onReceive = {
            if (it.type == MessageType.PAIR_CONFIRM) {
                uptime += 5 * 60 * 1000L + 1
                clock -= 60 * 60 * 1000L
            }
        }
        flow.run(channel, request("PROF-01"))
        assertEquals("O tempo para conferir o código acabou. Tente de novo.", Pairing.refusalText(channel.sent.last().payload))
        assertEquals(0, trusted.count())
    }

    // A classmate who gets there first no longer uses the window up: wrong
    // codes, none, cancels and dropped connections after the code came up
    // are tries, and the fifth closes the window, before anyone can guess at
    // the code or flash codes on the screen for ten minutes.
    @Test fun fiveTriesWithoutTheCodeCloseTheWindow() {
        pairs(teacherA)
        allow()
        flow.run(teacher(teacherB, nonce(), cancel()), request("ALUNO"))
        assertTrue(flow.newTeacherOpen)
        pairsWithCode(teacherB, "K7PQ2M95")
        pairs(teacherB)
        try {
            flow.run(teacher(teacherB, nonce()), request("ALUNO"))
        } catch (e: java.io.IOException) {
            // The dropped connection.
        }
        assertTrue(flow.newTeacherOpen)
        pairsWithCode(teacherB, "AAAAAAAA")

        assertFalse(flow.newTeacherOpen)
        val real = teacher(teacherC, nonce(), confirmWithCode(teacherC))
        flow.run(real, request("PROF-03"))
        assertEquals(MessageType.PAIR_REFUSED, real.sent.single().type)
        assertEquals(1, trusted.count())
    }

    // A code the phone could not show is no try.
    @Test fun aCodeThatDidNotShowLeavesTheWindowOpen() {
        pairs(teacherA)
        allow()
        canShow = false
        repeat(NewTeacherInvite.MAX_TRIES + 1) { flow.run(teacher(teacherB, nonce(), confirmWithCode(teacherB)), request("PROF-02")) }
        assertTrue(flow.newTeacherOpen)
    }

    // From the PairRequest on, the connection may last as long as a pairing.
    @Test fun aPairingTellsTheConnectionItIsOne() {
        val channel = teacher(teacherA, nonce(), confirm())
        flow.run(channel, request("PROF-01"))
        assertTrue(channel.pairing)
    }

    @Test fun aRefusedPairingDoesNotGetAPairingsTime() {
        pairs(teacherA)
        val channel = teacher(teacherB, nonce(), confirm())
        flow.run(channel, request("PROF-02"))
        assertFalse(channel.pairing)
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

    // As on a PC: a code the student can't see is no pairing.
    @Test fun aCodeThePhoneCannotShowIsRefused() {
        canShow = false
        val channel = teacher(teacherA, nonce(), confirm())
        flow.run(channel, request("PROF-01"))

        assertEquals(listOf(MessageType.PAIR_COMMIT, MessageType.PAIR_REFUSED), channel.sent.map { it.type })
        assertEquals("Não foi possível mostrar o código na tela do celular. Tente de novo.", Pairing.refusalText(channel.sent[1].payload))
        assertEquals(0, trusted.count())
        assertEquals(1, hidden)
        // The teacher never gets the phone's nonce, so never a code.
        assertFalse(channel.events.contains("send ${MessageType.PAIR_REVEAL}"))
    }

    @Test fun aConfirmAfterFiveMinutesIsRefused() {
        val channel = teacher(teacherA, nonce(), confirm())
        channel.onReceive = { if (it.type == MessageType.PAIR_CONFIRM) pass(5 * 60 * 1000L + 1) }
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
            showCode = { _, _ ->
                if (second.sent.isEmpty()) flowRef!!.run(second, request("PROF-02"))
                true
            },
            hideCode = {},
            deviceName = { "Cel 01" },
            elapsed = { uptime },
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
        const val CODE = "K7PQ2M94"

        // One RSA key for the whole class: making one takes a moment.
        val identity: AgentIdentity by lazy {
            AgentIdentity(Files.createTempDirectory("pairing-flow").toFile().apply { deleteOnExit() }, "Cel 01")
        }
    }
}
