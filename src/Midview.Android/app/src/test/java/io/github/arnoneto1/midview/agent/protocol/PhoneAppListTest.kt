package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test

class PhoneAppListTest {
    // Same bytes as PhoneAppListTests.cs (the .NET side).
    private val goldenHex =
        "01" + "0002" +
            "0c636f6d2e7768617473617070" + "085768617473417070" + "0004" + "89504e47" +
            "07636f6d2e782e79" + "07" + "43c3a26d657261" + "0003" + "010203"

    private val golden = listOf(
        PhoneApp("com.whatsapp", "WhatsApp", byteArrayOf(0x89.toByte(), 0x50, 0x4e, 0x47)),
        PhoneApp("com.x.y", "Câmera", byteArrayOf(1, 2, 3)),
    )

    private fun hex(text: String) = text.chunked(2).map { it.toInt(16).toByte() }.toByteArray()

    private fun assertRefused(block: () -> Unit) {
        try {
            block()
            fail("expected IllegalArgumentException")
        } catch (_: IllegalArgumentException) {
        }
    }

    @Test fun encodeMatchesTheGoldenBytes() {
        assertArrayEquals(hex(goldenHex), PhoneAppList.encode(golden))
        assertArrayEquals(hex("010000"), PhoneAppList.encode(emptyList()))
    }

    @Test fun decodeReadsTheGoldenBytes() {
        val apps = PhoneAppList.decode(hex(goldenHex))
        assertNotNull(apps)
        assertEquals(2, apps!!.size)
        assertEquals("com.whatsapp", apps[0].pkg)
        assertEquals("Câmera", apps[1].label)
        assertArrayEquals(byteArrayOf(1, 2, 3), apps[1].iconPng)
        assertTrue(PhoneAppList.decode(hex("010000"))!!.isEmpty())
    }

    @Test fun iconLengthIsBigEndianAndCanBeLarge() {
        val icon = ByteArray(0x0102)
        val bytes = PhoneAppList.encode(listOf(PhoneApp("com.a.b", "A", icon)))
        // 01 | 0001 | 07 + 7 bytes | 01 + 1 byte | then the icon length: 01 02
        assertArrayEquals(byteArrayOf(0x01, 0x02), bytes.copyOfRange(3 + 8 + 2, 3 + 8 + 4))
        assertEquals(icon.size, PhoneAppList.decode(bytes)!![0].iconPng.size)
    }

    @Test fun threeHundredAppsRoundTripAndMoreAreRefused() {
        val apps = (0 until 300).map { PhoneApp("com.app$it", "App $it", byteArrayOf(1)) }
        assertEquals(300, PhoneAppList.decode(PhoneAppList.encode(apps))!!.size)
        assertRefused { PhoneAppList.encode(apps + PhoneApp("com.app300", "App 300", byteArrayOf(1))) }
        assertNull(PhoneAppList.decode(hex("01" + "012d")))
    }

    @Test fun labelOf64CharsIsKeptOf65IsRefused() {
        val ok = PhoneAppList.decode(PhoneAppList.encode(listOf(PhoneApp("com.a.b", "x".repeat(64), ByteArray(0)))))
        assertEquals(64, ok!![0].label.length)
        assertRefused { PhoneAppList.encode(listOf(PhoneApp("com.a.b", "x".repeat(65), ByteArray(0)))) }
        // A hand-built payload with a 65-char label.
        assertNull(PhoneAppList.decode(hex("01" + "0001" + "07636f6d2e612e62" + "41" + "78".repeat(65) + "0000")))
    }

    @Test fun encodeRefusesBadPackagesDuplicatesAndHugeIcons() {
        assertRefused { PhoneAppList.encode(listOf(PhoneApp("chrome.exe ", "x", ByteArray(0)))) }
        assertRefused { PhoneAppList.encode(listOf(PhoneApp("com.a.b", "x", ByteArray(0)), PhoneApp("com.a.b", "y", ByteArray(0)))) }
        assertRefused { PhoneAppList.encode(listOf(PhoneApp("com.a.b", "x", ByteArray(65536)))) }
    }

    @Test fun decodeRejectsTrailingBytesTruncationAndBadVersion() {
        assertNull(PhoneAppList.decode(hex(goldenHex + "00")))
        assertNull(PhoneAppList.decode(hex(goldenHex.dropLast(2))))
        assertNull(PhoneAppList.decode(hex("02" + goldenHex.drop(2))))
        assertNull(PhoneAppList.decode(ByteArray(0)))
        assertNull(PhoneAppList.decode(hex("0100")))
    }

    @Test fun decodeRejectsBadPackageBadUtf8AndDuplicates() {
        assertNull(PhoneAppList.decode(hex("01" + "0001" + "08" + "7768617473617070" + "00" + "0000")))
        assertNull(PhoneAppList.decode(hex("01" + "0001" + "00" + "00" + "0000")))
        assertNull(PhoneAppList.decode(hex("01" + "0001" + "07636f6d2e612e62" + "02c328" + "0000")))
        val one = "07636f6d2e612e62" + "00" + "0000"
        assertNull(PhoneAppList.decode(hex("01" + "0002" + one + one)))
    }
}
