package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class LabConfigTest {
    // The bytes of src/Midview.Protocol/LabConfig.cs for SavedAt
    // 0x0102030405060708 with BlockDownloads (4) and Adblock (16) on.
    private val pcBytes = byteArrayOf(1, 1, 2, 3, 4, 5, 6, 7, 8, 20)

    @Test fun encodesAsAPc() {
        assertArrayEquals(pcBytes, LabConfig(0x0102030405060708, blockDownloads = true, adblock = true).encode())
    }

    @Test fun decodesAPcsPayload() {
        assertEquals(LabConfig(0x0102030405060708, blockDownloads = true, adblock = true), LabConfig.decode(pcBytes))
    }

    // The PC-only functions are never a phone's: left out, and never sent back.
    @Test fun thePcOnlyFunctionsAreLeftOut() {
        val everything = pcBytes.copyOf().also { it[9] = 0xFF.toByte() }
        val config = LabConfig.decode(everything)!!
        assertEquals(LabConfig(0x0102030405060708, blockDownloads = true, adblock = true), config)
        assertEquals(20, config.encode()[9].toInt())
        val pcOnly = pcBytes.copyOf().also { it[9] = 0xEB.toByte() }
        assertEquals(LabConfig(0x0102030405060708, blockDownloads = false, adblock = false), LabConfig.decode(pcOnly))
    }

    @Test fun anythingElseIsRefused() {
        assertNull(LabConfig.decode(ByteArray(0)))
        assertNull(LabConfig.decode(pcBytes.copyOf(9)))
        assertNull(LabConfig.decode(pcBytes + 0))
        assertNull(LabConfig.decode(pcBytes.copyOf().also { it[0] = 2 }))
    }

    @Test fun aPhoneStartsWithDownloadsFreeAndTheAdBlockerOn() {
        assertEquals(LabConfig(0, blockDownloads = false, adblock = true), LabConfig.DEFAULT)
    }

    @Test fun sameFunctionsInAnySavedAt() {
        assertTrue(LabConfig(1, true, false).sameFunctionsAs(LabConfig(2, true, false)))
        assertFalse(LabConfig(1, true, false).sameFunctionsAs(LabConfig(1, true, true)))
    }
}
