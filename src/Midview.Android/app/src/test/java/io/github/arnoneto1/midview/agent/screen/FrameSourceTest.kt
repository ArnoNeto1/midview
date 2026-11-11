package io.github.arnoneto1.midview.agent.screen

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Test
import java.nio.ByteBuffer
import kotlin.concurrent.thread

class FrameSourceTest {
    @Test fun captureIsLimitedTo1280OnTheLongSide() {
        assertEquals(559 to 1280, FrameScale.capture(720, 1650))
        assertEquals(1280 to 559, FrameScale.capture(1650, 720))
        assertEquals(576 to 1280, FrameScale.capture(1080, 2400))
        // Small enough already: native.
        assertEquals(640 to 480, FrameScale.capture(640, 480))
    }

    @Test fun thumbnailFitsTheBoxWithoutEnlarging() {
        assertEquals(79 to 180, FrameScale.thumbnail(320, 180, 720, 1650))
        assertEquals(320 to 140, FrameScale.thumbnail(320, 180, 1650, 720))
        assertEquals(72 to 165, FrameScale.thumbnail(960, 540, 72, 165))
        assertEquals(1 to 1, FrameScale.thumbnail(1, 1, 720, 1650))
    }

    @Test fun rgbaRowsWithPaddingBecomeArgb() {
        // 2 x 2, 4 bytes a pixel, rows of 12 bytes (4 of padding).
        val bytes = byteArrayOf(
            1, 2, 3, 0, 4, 5, 6, 0, 9, 9, 9, 9,
            -1, 0, 0, -1, 0, -128, 127, 10, 9, 9, 9, 9,
        )
        val buffer = ByteBuffer.allocateDirect(bytes.size + 3)
        buffer.put(byteArrayOf(7, 7, 7)).put(bytes)
        buffer.position(3)
        val pixels = RgbaPixels.toArgb(buffer, 2, 2, pixelStride = 4, rowStride = 12)
        assertArrayEquals(
            intArrayOf(0xFF010203.toInt(), 0xFF040506.toInt(), 0xFFFF0000.toInt(), 0xFF00807F.toInt()),
            pixels,
        )
        // The buffer is left as it was.
        assertEquals(3, buffer.position())
    }

    @Test fun latestFrameWaitsForANewerOne() {
        val frames = LatestFrame()
        assertNull(frames.latest())
        assertNull(frames.awaitNew(0, 10))

        frames.publish(IntArray(4), 2, 2)
        val first = frames.latest()!!
        assertEquals(1L, first.seq)
        assertSame(first, frames.awaitNew(0, 10))
        assertNull(frames.awaitNew(first.seq, 10))

        val publisher = thread { Thread.sleep(50); frames.publish(IntArray(6), 3, 2) }
        val second = frames.awaitNew(first.seq, 2_000)!!
        publisher.join()
        assertEquals(2L, second.seq)
        assertEquals(3, second.width)

        frames.clear()
        assertNull(frames.latest())
    }

    @Test fun picturesAreTurnedIntoPixelsOnlyWhenAskedFor() {
        val frames = LatestFrame()
        var conversions = 0
        frames.attach { conversions++; Picture(IntArray(4) { conversions }, 2, 2) }

        // The screen changes a lot with nobody watching: nothing converted.
        repeat(100) { frames.changed() }
        assertEquals(0, conversions)

        // A thumbnail asks: one conversion, the newest picture.
        val first = frames.latest()!!
        assertEquals(1, conversions)
        // Asked again with no change: the same picture.
        assertSame(first, frames.latest())
        assertNull(frames.awaitNew(first.seq, 10))
        assertEquals(1, conversions)

        // A big window waiting for a change gets it converted once.
        val waiter = thread { Thread.sleep(50); frames.changed() }
        val second = frames.awaitNew(first.seq, 2_000)!!
        waiter.join()
        assertEquals(2, conversions)
        assertEquals(2, second.pixels[0])

        // A new capture with nothing to give yet (convert gives null): no picture.
        frames.attach { conversions++; null }
        assertNull(frames.latest())
        assertEquals(3, conversions)

        frames.clear()
        frames.changed()
        assertNull(frames.latest())
        assertEquals(3, conversions)
    }
}
