package io.github.arnoneto1.midview.agent.protocol

// RequestThumbnail's payload, as src/Midview.Protocol/ThumbnailRequest.cs:
// the box the thumbnail must fit in (the teacher's tile), width then height,
// uint16 each, big-endian. A Master from before tiles could be resized sends
// it empty.
object ThumbnailRequest {
    const val DEFAULT_WIDTH = 320
    const val DEFAULT_HEIGHT = 180
    const val MAX_WIDTH = 960
    const val MAX_HEIGHT = 540

    // Never refuses: the default box when the request can't be read, and
    // never past the maximum.
    fun decode(payload: ByteArray): Pair<Int, Int> {
        if (payload.size != 4) {
            return DEFAULT_WIDTH to DEFAULT_HEIGHT
        }

        val width = ((payload[0].toInt() and 0xFF) shl 8) or (payload[1].toInt() and 0xFF)
        val height = ((payload[2].toInt() and 0xFF) shl 8) or (payload[3].toInt() and 0xFF)
        if (width == 0 || height == 0) {
            return DEFAULT_WIDTH to DEFAULT_HEIGHT
        }
        return minOf(width, MAX_WIDTH) to minOf(height, MAX_HEIGHT)
    }
}
