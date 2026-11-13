package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class PhoneWallpaperTest {
    private val jpeg = byteArrayOf(0xFF.toByte(), 0xD8.toByte(), 0xFF.toByte(), 0xE0.toByte(), 1, 2, 3, 0xFF.toByte(), 0xD9.toByte())
    private val png = byteArrayOf(0x89.toByte(), 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 8, 7)

    @Test fun theMessageAndTheScreensAreTheWireNumbers() {
        assertEquals(105, MessageType.SET_PHONE_WALLPAPER)
        assertEquals(1, PhoneWallpaper.HOME)
        assertEquals(2, PhoneWallpaper.LOCK)
        assertEquals(3, PhoneWallpaper.BOTH)
    }

    @Test fun aJpegOrAPngRoundTripsOnEachScreen() {
        for (screens in 1..3) {
            for (image in listOf(jpeg, png)) {
                val payload = PhoneWallpaper.encode(screens, image)
                assertEquals(screens, payload[0].toInt())
                val decoded = PhoneWallpaper.decode(payload)
                assertNotNull(decoded)
                assertEquals(screens, decoded!!.screens)
                assertArrayEquals(image, decoded.image)
            }
        }
    }

    // What the C# PhoneWallpaper.Encode gives for the same picture.
    @Test fun theBytesAreTheMastersOnes() {
        assertArrayEquals(byteArrayOf(2) + jpeg, PhoneWallpaper.encode(PhoneWallpaper.LOCK, jpeg))
        assertArrayEquals(byteArrayOf(3), PhoneWallpaper.encode(PhoneWallpaper.BOTH, ByteArray(0)))
    }

    @Test fun noPicturePutsThePhonesOwnBack() {
        val decoded = PhoneWallpaper.decode(byteArrayOf(3))
        assertNotNull(decoded)
        assertEquals(0, decoded!!.image.size)
    }

    @Test fun theBiggestPictureStillFits() {
        val image = ByteArray(PhoneWallpaper.MAX_IMAGE_BYTES)
        jpeg.copyInto(image)
        val payload = PhoneWallpaper.encode(PhoneWallpaper.HOME, image)
        assertEquals(image.size, PhoneWallpaper.decode(payload)!!.image.size)
        assertTrue(payload.size < MessageFramer.MAX)
    }

    @Test fun encodeRefusesWrongScreensTooBigOrNotAnImage() {
        val tooBig = ByteArray(PhoneWallpaper.MAX_IMAGE_BYTES + 1).also { jpeg.copyInto(it) }
        val bad = listOf<() -> Unit>(
            { PhoneWallpaper.encode(0, jpeg) },
            { PhoneWallpaper.encode(4, jpeg) },
            { PhoneWallpaper.encode(3, tooBig) },
            { PhoneWallpaper.encode(3, "GIF89a".toByteArray()) },
        )
        for (call in bad) {
            try {
                call()
                throw AssertionError("encoded")
            } catch (e: IllegalArgumentException) {
                // Refused, as it should be.
            }
        }
    }

    @Test fun decodeRefusesAnyOtherPayload() {
        assertNull(PhoneWallpaper.decode(ByteArray(0)))
        assertNull(PhoneWallpaper.decode(byteArrayOf(0)))
        assertNull(PhoneWallpaper.decode(byteArrayOf(4)))
        assertNull(PhoneWallpaper.decode(byteArrayOf(0xFF.toByte(), 0xFF.toByte(), 0xD8.toByte(), 0xFF.toByte())))
        assertNull(PhoneWallpaper.decode(byteArrayOf(1, 'G'.code.toByte(), 'I'.code.toByte(), 'F'.code.toByte())))
        assertNull(PhoneWallpaper.decode(byteArrayOf(3, 0xFF.toByte(), 0xD8.toByte())))
        val tooBig = ByteArray(1 + PhoneWallpaper.MAX_IMAGE_BYTES + 1).also { it[0] = 1; jpeg.copyInto(it, 1) }
        assertNull(PhoneWallpaper.decode(tooBig))
    }
}
