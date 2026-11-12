package io.github.arnoneto1.midview.agent.protocol

// One of the teacher's mouse or keyboard events while controlling ("Controlar"),
// the payload of MessageType.INPUT_EVENT, as src/Midview.Protocol/InputEvent.cs
// and InputEventCodec.cs: 11 bytes, the kind (1 byte), X and Y (int32
// little-endian each) and the Windows virtual-key code (uint16 little-endian).
// X and Y are pixels of the picture the teacher sees (the ScreenFrame size);
// a wheel's delta rides in X (120 a notch, positive away from the teacher);
// TEXT carries one UTF-16 unit of what the teacher typed in the key field.
data class InputEvent(val kind: Int, val x: Int, val y: Int, val key: Int) {
    companion object {
        const val MOUSE_MOVE = 0
        const val LEFT_DOWN = 1
        const val LEFT_UP = 2
        const val KEY_DOWN = 3
        const val KEY_UP = 4
        const val RIGHT_DOWN = 5
        const val RIGHT_UP = 6
        const val WHEEL = 7
        const val TEXT = 8

        const val LENGTH = 11

        // Null when the payload is too short to be one; longer is read as
        // far as one goes (as the PC's codec).
        fun decode(payload: ByteArray): InputEvent? {
            if (payload.size < LENGTH) return null
            return InputEvent(payload[0].toInt() and 0xFF, int32(payload, 1), int32(payload, 5), uint16(payload, 9))
        }

        fun encode(event: InputEvent): ByteArray {
            val bytes = ByteArray(LENGTH)
            bytes[0] = event.kind.toByte()
            for (i in 0 until 4) {
                bytes[1 + i] = (event.x shr (8 * i)).toByte()
                bytes[5 + i] = (event.y shr (8 * i)).toByte()
            }
            bytes[9] = event.key.toByte()
            bytes[10] = (event.key shr 8).toByte()
            return bytes
        }

        private fun int32(bytes: ByteArray, at: Int): Int =
            (bytes[at].toInt() and 0xFF) or
                ((bytes[at + 1].toInt() and 0xFF) shl 8) or
                ((bytes[at + 2].toInt() and 0xFF) shl 16) or
                ((bytes[at + 3].toInt() and 0xFF) shl 24)

        private fun uint16(bytes: ByteArray, at: Int): Int =
            (bytes[at].toInt() and 0xFF) or ((bytes[at + 1].toInt() and 0xFF) shl 8)
    }
}
