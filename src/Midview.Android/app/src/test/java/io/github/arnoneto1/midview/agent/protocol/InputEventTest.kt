package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class InputEventTest {
    // What src/Midview.Protocol/InputEventCodec.Encode writes for
    // new InputEvent(MouseLeftDown, 300, -2, 0x41): kind, X and Y as int32, the key as uint16, little-endian.
    private val fromTheMaster = byteArrayOf(1, 0x2C, 0x01, 0, 0, 0xFE.toByte(), 0xFF.toByte(), 0xFF.toByte(), 0xFF.toByte(), 0x41, 0)

    @Test fun decodesWhatTheMasterSends() {
        assertEquals(InputEvent(InputEvent.LEFT_DOWN, 300, -2, 0x41), InputEvent.decode(fromTheMaster))
    }

    @Test fun encodesTheSameBytes() {
        assertArrayEquals(fromTheMaster, InputEvent.encode(InputEvent(InputEvent.LEFT_DOWN, 300, -2, 0x41)))
    }

    @Test fun aWheelDeltaAndAHighKeySurvive() {
        val event = InputEvent(InputEvent.WHEEL, -360, 0, 0xFFFF)
        assertEquals(event, InputEvent.decode(InputEvent.encode(event)))
        val text = InputEvent(InputEvent.TEXT, 0, 0, 'ç'.code)
        assertEquals(text, InputEvent.decode(InputEvent.encode(text)))
    }

    @Test fun aShortPayloadIsNoEvent() {
        assertNull(InputEvent.decode(ByteArray(10)))
        assertNull(InputEvent.decode(ByteArray(0)))
    }

    @Test fun kindsKeepTheMastersNumbers() {
        assertEquals(
            listOf(0, 1, 2, 3, 4, 5, 6, 7, 8),
            listOf(
                InputEvent.MOUSE_MOVE, InputEvent.LEFT_DOWN, InputEvent.LEFT_UP, InputEvent.KEY_DOWN, InputEvent.KEY_UP,
                InputEvent.RIGHT_DOWN, InputEvent.RIGHT_UP, InputEvent.WHEEL, InputEvent.TEXT,
            ),
        )
    }
}
