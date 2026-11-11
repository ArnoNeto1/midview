package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertEquals
import org.junit.Test

class ThumbnailRequestTest {
    // What .NET's ThumbnailRequest.Encode writes: uint16 big-endian width, then height.
    private fun request(width: Int, height: Int) =
        byteArrayOf((width shr 8).toByte(), width.toByte(), (height shr 8).toByte(), height.toByte())

    @Test fun limitsMatchDotNet() {
        assertEquals(320, ThumbnailRequest.DEFAULT_WIDTH)
        assertEquals(180, ThumbnailRequest.DEFAULT_HEIGHT)
        assertEquals(960, ThumbnailRequest.MAX_WIDTH)
        assertEquals(540, ThumbnailRequest.MAX_HEIGHT)
    }

    @Test fun theBoxIsReadAsDotNetWritesIt() {
        assertEquals(83 to 180, ThumbnailRequest.decode(request(83, 180)))
        assertEquals(300 to 260, ThumbnailRequest.decode(byteArrayOf(0x01, 0x2C, 0x01, 0x04)))
    }

    @Test fun anOldMastersEmptyRequestGetsTheDefault() {
        assertEquals(320 to 180, ThumbnailRequest.decode(ByteArray(0)))
    }

    @Test fun anythingUnreadableGetsTheDefault() {
        assertEquals(320 to 180, ThumbnailRequest.decode(byteArrayOf(1, 2, 3)))
        assertEquals(320 to 180, ThumbnailRequest.decode(byteArrayOf(0, 100, 0, 100, 0)))
        assertEquals(320 to 180, ThumbnailRequest.decode(request(0, 180)))
        assertEquals(320 to 180, ThumbnailRequest.decode(request(83, 0)))
    }

    @Test fun neverPastTheMaximum() {
        assertEquals(960 to 540, ThumbnailRequest.decode(request(65535, 65535)))
        assertEquals(960 to 100, ThumbnailRequest.decode(request(5000, 100)))
    }
}
