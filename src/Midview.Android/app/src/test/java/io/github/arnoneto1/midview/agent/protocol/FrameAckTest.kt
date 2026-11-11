package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class FrameAckTest {
    @Test fun frameAckReadsLittleEndianAndRejectsOtherSizes() {
        assertEquals(0x01020304, FrameAck.decode(byteArrayOf(4, 3, 2, 1)))
        assertEquals(5, FrameAck.decode(byteArrayOf(5, 0, 0, 0)))
        assertEquals(-1, FrameAck.decode(byteArrayOf(-1, -1, -1, -1)))
        assertNull(FrameAck.decode(ByteArray(0)))
        assertNull(FrameAck.decode(ByteArray(3)))
        assertNull(FrameAck.decode(ByteArray(5)))
    }
}
