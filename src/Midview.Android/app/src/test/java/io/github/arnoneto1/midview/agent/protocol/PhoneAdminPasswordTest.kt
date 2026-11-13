package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class PhoneAdminPasswordTest {
    private val salt = ByteArray(16) { it.toByte() }

    private fun hex(bytes: ByteArray) = bytes.joinToString("") { "%02X".format(it) }

    @Test fun theMessagesAreTheWireNumbers() {
        assertEquals(103, MessageType.FREE_PHONE_NETWORK)
        assertEquals(104, MessageType.SET_PHONE_ADMIN_PASSWORD)
    }

    // RFC 7914, section 11: PBKDF2-HMAC-SHA256 ("passwd", "salt", 1).
    @Test fun theHashIsPbkdf2HmacSha256() {
        assertEquals(
            "55AC046E56E3089FEC1691C22544B605F94185216DDE0465E68B9D57C20DACBC",
            hex(PhoneAdminPassword.hash("passwd", "salt".toByteArray(), 1)),
        )
    }

    // Same bytes as PhoneAdminPasswordTests.cs (the Master's).
    @Test fun thePayloadIsTheMastersBytes() {
        val payload = PhoneAdminPassword.encode("abc123", salt, 10_000)
        assertEquals(53, payload.size)
        assertArrayEquals(byteArrayOf(1, 0, 0, 0x27, 0x10), payload.copyOfRange(0, 5))
        assertArrayEquals(salt, payload.copyOfRange(5, 21))
        assertEquals("42FB757417EDCFAB297FE3D49C0C3EF0057AD45E1B331AD5921B203983BBA499", hex(payload.copyOfRange(21, 53)))
    }

    @Test fun theRightPasswordIsVerified() {
        val stored = PhoneAdminPassword.decode(PhoneAdminPassword.encode("abc123", salt, 10_000))
        assertNotNull(stored)
        assertTrue(PhoneAdminPassword.verify("abc123", stored!!))
        assertEquals(10_000, stored.iterations)
        assertArrayEquals(salt, stored.salt)
    }

    @Test fun anyOtherIsNot() {
        val stored = PhoneAdminPassword.decode(PhoneAdminPassword.encode("abc123", salt, 10_000))!!
        for (typed in listOf("abc124", "ABC123", "abc1234", "", "abc12", "abc 123")) {
            assertFalse(typed, PhoneAdminPassword.verify(typed, stored))
        }
    }

    @Test fun sixToTwelveLettersOrDigitsAreValid() {
        for (password in listOf("abc123", "Escola2026", "123456", "ABCDEFGHIJKL")) {
            assertTrue(password, PhoneAdminPassword.isValid(password))
        }
        for (password in listOf("1234", "abc12", "abcdefghijklm", "abc 123", "senha-1", "çãoabc", "１２３４５６", "")) {
            assertFalse(password, PhoneAdminPassword.isValid(password))
        }
    }

    @Test fun whatThisVersionCannotUseIsRefused() {
        val good = PhoneAdminPassword.encode("abc123", salt, 10_000)
        assertNull(PhoneAdminPassword.decode(ByteArray(0)))
        assertNull(PhoneAdminPassword.decode(good.copyOf(52)))
        assertNull(PhoneAdminPassword.decode(good + 0))
        assertNull(PhoneAdminPassword.decode(good.copyOf().also { it[0] = 2 }))
        assertNull(PhoneAdminPassword.decode(good.copyOf().also { it[3] = 0; it[4] = 1 }))
        assertNull(PhoneAdminPassword.decode(good.copyOf().also { it[1] = 0x7F }))
    }

    @Test fun theMinutesAreOneByte() {
        for (minutes in listOf(0, 1, 10, 30, 60)) {
            assertEquals(minutes, PhoneNetworkFree.decode(PhoneNetworkFree.encode(minutes)))
        }
        assertNull(PhoneNetworkFree.decode(byteArrayOf(61)))
        assertNull(PhoneNetworkFree.decode(byteArrayOf(-1)))
        assertNull(PhoneNetworkFree.decode(ByteArray(0)))
        assertNull(PhoneNetworkFree.decode(byteArrayOf(10, 0)))
    }
}
