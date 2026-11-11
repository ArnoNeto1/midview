package io.github.arnoneto1.midview.agent.protocol

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class PairingTest {
    private val master = "A".repeat(40)
    private val agent = "B".repeat(40)

    private fun bytes(start: Int) = ByteArray(32) { (it + start).toByte() }

    private fun hex(bytes: ByteArray) = bytes.joinToString("") { "%02x".format(it) }

    // The same vector as tests/Midview.Protocol.Tests/PairingTests.cs.
    @Test fun pairingCodeMatchesDotNet() {
        val m = "0123456789ABCDEF0123456789ABCDEF01234567"; val a = "89ABCDEF0123456789ABCDEF0123456789ABCDEF"
        val an = ByteArray(32) { it.toByte() }; val mn = ByteArray(32) { (it + 32).toByte() }
        assertEquals(602288, Pairing.code(m, a, an, mn)); assertEquals("602 288", Pairing.format(602288))
    }

    @Test fun codeMatchesTheOtherDotNetReferenceValues() {
        assertEquals(433504, Pairing.code(master, agent, bytes(0), bytes(32)))
        assertEquals(353409, Pairing.code(master, agent, bytes(0), bytes(1)))
    }

    @Test fun codeDoesNotCareAboutTheThumbprintsCaseOrSpaces() {
        assertEquals(433504, Pairing.code(master.lowercase(), " ${agent.lowercase()} ", bytes(0), bytes(32)))
    }

    @Test fun codeChangesWithEachInput() {
        assertNotEquals(433504, Pairing.code("A".repeat(39) + "C", agent, bytes(0), bytes(32)))
        assertNotEquals(433504, Pairing.code(master, agent, bytes(1), bytes(32)))
        assertNotEquals(433504, Pairing.code(agent, master, bytes(0), bytes(32)))
    }

    @Test fun formatShowsTwoGroupsOfThree() {
        assertEquals("000 042", Pairing.format(42))
        assertEquals("433 504", Pairing.format(433504))
        assertEquals("999 999", Pairing.format(999999))
    }

    @Test fun commitIsSha256() {
        assertEquals("630dcd2966c4336691125448bbb25b4ff412a49c732db2c8abc1b8581bd710dd", hex(Pairing.commit(bytes(0))))
    }

    @Test fun opensOnlyTheCommittedNonce() {
        val commitment = Pairing.commit(bytes(0))
        assertTrue(Pairing.opens(commitment, bytes(0)))
        assertFalse(Pairing.opens(commitment, bytes(1)))
        assertFalse(Pairing.opens(commitment, ByteArray(31)))
        assertFalse(Pairing.opens(ByteArray(5), bytes(0)))
    }

    @Test fun newNonceIs32RandomBytes() {
        val first = Pairing.newNonce()
        assertEquals(32, first.size)
        assertFalse(first.contentEquals(Pairing.newNonce()))
    }

    @Test fun cleanTeacherNameKeepsOnlyWhatIsSafeToShow() {
        assertEquals("PROF-01", Pairing.cleanTeacherName("PROF-01"))
        assertEquals("sala 2  co", Pairing.cleanTeacherName("  sala \"2\" & co "))
        assertEquals("?", Pairing.cleanTeacherName(""))
        assertEquals("?", Pairing.cleanTeacherName("\"&|"))
        assertEquals("Sala_Ção.1", Pairing.cleanTeacherName("Sala_Ção.1"))
    }

    @Test fun cleanTeacherNameIsNeverLongerThanTheLimit() {
        assertEquals("x".repeat(40), Pairing.cleanTeacherName("x".repeat(60)))
        assertEquals("x".repeat(39), Pairing.cleanTeacherName("x".repeat(39) + " y"))
    }

    @Test fun refusalCarriesItsReason() {
        val message = Pairing.refused("Este aparelho já tem professor.")
        assertEquals(MessageType.PAIR_REFUSED, message.type)
        assertEquals("Este aparelho já tem professor.", Pairing.refusalText(message.payload))
    }

    @Test fun limitsMatchDotNet() {
        assertEquals(32, Pairing.NONCE_LENGTH)
        assertEquals(40, Pairing.MAX_TEACHER_NAME_LENGTH)
        assertEquals(5 * 60 * 1000L, Pairing.CONFIRM_TIMEOUT_MILLIS)
        assertEquals(10 * 60 * 1000L, Pairing.NEW_TEACHER_WINDOW_MILLIS)
    }

    @Test fun pairingResultIsDotNetJsonWithAndroidKind() {
        val json = JSONObject(String(Pairing.encodeResult("Cel 01", agent, null), Charsets.UTF_8))
        assertEquals("Cel 01", json.getString("name"))
        assertEquals(agent, json.getString("session"))
        assertTrue(json.isNull("mac"))
        assertEquals("android", json.getString("kind"))
        assertEquals(
            """{"name":"Cel 01","session":"$agent","mac":"00-1A-2B-3C-4D-5E","kind":"android"}""",
            String(Pairing.encodeResult("Cel 01", agent, "00-1A-2B-3C-4D-5E"), Charsets.UTF_8),
        )
    }

    // .NET's PairingResult.Decode drops a result without either.
    @Test fun pairingResultNeedsANameAndASession() {
        assertThrows(IllegalArgumentException::class.java) { Pairing.encodeResult("", agent, null) }
        assertThrows(IllegalArgumentException::class.java) { Pairing.encodeResult(" ", agent, null) }
        assertThrows(IllegalArgumentException::class.java) { Pairing.encodeResult("Cel 01", "", null) }
    }
}
