package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

// The same vectors as tests/Midview.Protocol.Tests/PhoneFindTests.cs.
class PhoneFindTest {
    private val vectors = mapOf(
        0 to byteArrayOf(0x00, 0x00),
        15 to byteArrayOf(0x00, 0x0F),
        60 to byteArrayOf(0x00, 0x3C),
        300 to byteArrayOf(0x01, 0x2C),
    )

    @Test fun messageTypeIsTheWireNumber() = assertEquals(108, MessageType.FIND_PHONE)

    @Test fun secondsRoundTrip() {
        for ((seconds, wire) in vectors) {
            assertArrayEquals(wire, PhoneFind.encode(seconds))
            assertEquals(seconds, PhoneFind.decode(wire))
        }
    }

    @Test fun outOfRangeIsRefused() {
        assertNull(PhoneFind.decode(byteArrayOf(0x01, 0x2D)))
        assertNull(PhoneFind.decode(byteArrayOf(0x3C)))
        assertNull(PhoneFind.decode(ByteArray(0)))
    }
}
