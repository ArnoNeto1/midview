package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test

class PhoneScreenPasswordTest {
    @Test fun theMessageIsTheWireNumber() {
        assertEquals(100, MessageType.SET_PHONE_SCREEN_PASSWORD)
    }

    @Test fun fourDigitsOrNothingAreValid() {
        for (pin in listOf("1234", "0000", "9876", "")) {
            assertTrue(pin, PhoneScreenPassword.isValid(pin))
            assertEquals(pin, PhoneScreenPassword.decode(PhoneScreenPassword.encode(pin)))
        }
    }

    @Test fun anythingElseIsRefused() {
        // Arabic-Indic and fullwidth digits are digits, but not ASCII.
        for (pin in listOf("123", "12345", "12a4", "abcd", "12 4", " 1234", "١٢٣٤", "１２３４")) {
            assertFalse(pin, PhoneScreenPassword.isValid(pin))
            assertNull(pin, PhoneScreenPassword.decode(pin.toByteArray(Charsets.UTF_8)))
            try {
                PhoneScreenPassword.encode(pin)
                fail("encoded $pin")
            } catch (e: IllegalArgumentException) {
                // Refused.
            }
        }
    }

    @Test fun theBytesAreTheAsciiDigits() {
        // Same bytes as PhoneScreenPasswordTests.cs (the .NET side).
        assertArrayEquals(byteArrayOf(0x31, 0x32, 0x33, 0x34), PhoneScreenPassword.encode("1234"))
        assertArrayEquals(ByteArray(0), PhoneScreenPassword.encode(PhoneScreenPassword.NONE))
    }

    @Test fun bytesThatAreNotAsciiDigitsAreRefused() {
        assertNull(PhoneScreenPassword.decode(byteArrayOf(0x31, 0x32, 0x33, 0xB4.toByte())))
        assertNull(PhoneScreenPassword.decode(byteArrayOf(0x31, 0x32, 0x33, 0x00)))
    }

    @Test fun theTeacherReadsWhy() {
        assertEquals("o PIN tem que ter 4 números", PhoneScreenPassword.INVALID)
    }
}
