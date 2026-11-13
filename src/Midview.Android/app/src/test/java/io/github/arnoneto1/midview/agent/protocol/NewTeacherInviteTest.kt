package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class NewTeacherInviteTest {
    private val master = "A".repeat(40)
    private val agent = "D".repeat(40)
    private val agentNonce = ByteArray(32) { (it + 1).toByte() }
    private val masterNonce = ByteArray(32) { (100 + it).toByte() }

    private fun hex(bytes: ByteArray) = bytes.joinToString("") { "%02x".format(it) }

    private fun proofFor(code: String, masterThumb: String = master) =
        NewTeacherInvite.proof(NewTeacherInvite.key(code, agent), masterThumb, agent, agentNonce, masterNonce)

    // The same bytes as .NET's NewTeacherInviteTests.
    @Test fun keyAndProofMatchTheSharedVector() {
        val key = NewTeacherInvite.key("K7PQ2M94", agent)
        assertEquals("35b054ac0ea6fc7720587a0b83f8ede6cbccf432b8b6a27b0a68bc4f72f2c835", hex(key))
        assertEquals(
            "a70ea9f6f00656859588392e3f9e0ccf72c1e6a6460f471b608c43ba5f84d16e",
            hex(NewTeacherInvite.proof(key, master, agent, agentNonce, masterNonce)),
        )
    }

    @Test fun theRightCodeVerifiesWhateverTheThumbprintsCase() {
        assertTrue(NewTeacherInvite.verifies("K7PQ2M94", proofFor("K7PQ2M94"), master.lowercase(), " ${agent.lowercase()}", agentNonce, masterNonce))
    }

    @Test fun aWrongCodeDoesNotVerify() {
        assertFalse(NewTeacherInvite.verifies("K7PQ2M95", proofFor("K7PQ2M94"), master, agent, agentNonce, masterNonce))
    }

    // A proof is for one teacher's certificate, one device and one pairing.
    @Test fun aProofForAnotherTeacherDeviceOrPairingDoesNotVerify() {
        val proof = proofFor("K7PQ2M94")
        val other = "B".repeat(40)
        assertFalse(NewTeacherInvite.verifies("K7PQ2M94", proof, other, agent, agentNonce, masterNonce))
        assertFalse(NewTeacherInvite.verifies("K7PQ2M94", proof, master, other, agentNonce, masterNonce))
        assertFalse(NewTeacherInvite.verifies("K7PQ2M94", proof, master, agent, masterNonce, agentNonce))
    }

    @Test fun aProofOfTheWrongSizeDoesNotVerify() {
        assertFalse(NewTeacherInvite.verifies("K7PQ2M94", ByteArray(0), master, agent, agentNonce, masterNonce))
        assertFalse(NewTeacherInvite.verifies("K7PQ2M94", proofFor("K7PQ2M94").copyOf(31), master, agent, agentNonce, masterNonce))
    }

    @Test fun decodeTakesOnlyACode() {
        assertEquals("K7PQ2M94", NewTeacherInvite.decode("K7PQ2M94".toByteArray()))
        assertNull(NewTeacherInvite.decode(ByteArray(0)))
        assertNull(NewTeacherInvite.decode("K7PQ2M9".toByteArray()))
        assertNull(NewTeacherInvite.decode("k7pq2m94".toByteArray()))
        assertNull(NewTeacherInvite.decode("K7PQ2M9U".toByteArray()))
    }

    // As .NET's TeacherInvite.Alphabet.
    @Test fun theAlphabetIsCrockfordsBase32() {
        assertEquals(32, NewTeacherInvite.ALPHABET.length)
        assertFalse(NewTeacherInvite.ALPHABET.any { it in "ILOU" })
    }
}
