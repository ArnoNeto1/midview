package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

// "Desfazer e tirar o Midview": number 106, as the C# RemoveMidviewFromPhone,
// confirmed by the word the teacher typed.
class PhoneLeaveRequestTest {
    @Test fun theNumberIsTheMastersOne() = assertEquals(106, MessageType.REMOVE_MIDVIEW_FROM_PHONE)

    @Test fun thePayloadIsTheWordInUtf8() {
        assertArrayEquals(byteArrayOf(82, 69, 77, 79, 86, 69, 82), PhoneLeaveRequest.encode())
        assertTrue(PhoneLeaveRequest.isConfirmed("REMOVER".toByteArray()))
    }

    @Test fun anythingElseIsNoConfirmation() {
        for (other in listOf("", "remover", "REMOVE", "REMOVER ", "APAGAR")) {
            assertFalse(other, PhoneLeaveRequest.isConfirmed(other.toByteArray()))
        }
    }
}
