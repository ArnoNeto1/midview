package io.github.arnoneto1.midview.agent.protocol

// FrameAck's payload (MessageType.FRAME_ACK): the newest frame id the viewer
// has drawn, int32 little-endian; it also acknowledges every frame before it.
object FrameAck {
    fun decode(payload: ByteArray): Int? {
        if (payload.size != 4) return null
        return (payload[0].toInt() and 0xFF) or
            ((payload[1].toInt() and 0xFF) shl 8) or
            ((payload[2].toInt() and 0xFF) shl 16) or
            ((payload[3].toInt() and 0xFF) shl 24)
    }
}
