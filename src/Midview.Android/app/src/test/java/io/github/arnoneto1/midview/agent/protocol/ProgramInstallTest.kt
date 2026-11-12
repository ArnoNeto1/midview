package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.nio.ByteBuffer

class ProgramInstallTest {
    private fun hex(text: String) = text.chunked(2).map { it.toInt(16).toByte() }.toByteArray()

    private fun request(size: Long, arguments: ByteArray, name: ByteArray): ByteArray =
        ByteBuffer.allocate(10 + arguments.size + name.size).putLong(size).putShort(arguments.size.toShort())
            .put(arguments).put(name).array()

    // The bytes ProgramInstallTests.cs has the Master send for an .apk.
    @Test fun theMastersApkRequestIsRead() {
        assertEquals(InstallRequest("ola.apk", 1000, ""), ProgramInstall.decodeRequest(hex("00000000000003E8" + "0000" + "6F6C612E61706B")))
        assertEquals(InstallRequest("setup.exe", 5, "/S"), ProgramInstall.decodeRequest(request(5, "/S ".toByteArray(), "setup.exe".toByteArray())))
    }

    @Test fun whatAPcsAgentRefusesIsRefusedToo() {
        assertNull(ProgramInstall.decodeRequest(ByteArray(9)))
        assertNull(ProgramInstall.decodeRequest(request(0, ByteArray(0), "a.apk".toByteArray())))
        assertNull(ProgramInstall.decodeRequest(request(ProgramInstall.MAX_FILE_SIZE + 1, ByteArray(0), "a.apk".toByteArray())))
        assertNull(ProgramInstall.decodeRequest(request(5, ByteArray(0), ByteArray(0))))
        assertNull(ProgramInstall.decodeRequest(request(5, "a\nb".toByteArray(), "a.apk".toByteArray())))
        assertNull(ProgramInstall.decodeRequest(request(5, ByteArray(0), "../a.apk".toByteArray())))
        assertNull(ProgramInstall.decodeRequest(request(5, ByteArray(0), byteArrayOf(0xC3.toByte(), 0x28))))
        assertNull(ProgramInstall.decodeRequest(request(5, ByteArray(2049), "a.apk".toByteArray())))
    }

    @Test fun apkNames() {
        assertTrue(ProgramInstall.isApkName("ola.apk"))
        assertTrue(ProgramInstall.isApkName("OLA.APK"))
        assertFalse(ProgramInstall.isApkName("ola.xapk"))
        assertFalse(ProgramInstall.isApkName("setup.exe"))
    }

    // As ProgramList.cs writes it.
    @Test fun theProgramListRoundTrips() {
        val bytes = ProgramInstall.encodeList(listOf("com.a.b", "Câmera"))
        assertEquals("01" + "0002" + "07636f6d2e612e62" + "0743c3a26d657261", bytes.joinToString("") { "%02x".format(it) })
        assertEquals(listOf("com.a.b", "Câmera"), ProgramInstall.decodeList(bytes))
        assertEquals(emptyList<String>(), ProgramInstall.decodeList(hex("010000")))
    }

    @Test fun aBadProgramListIsRefused() {
        assertNull(ProgramInstall.decodeList(hex("020000")))
        assertNull(ProgramInstall.decodeList(hex("0100")))
        assertNull(ProgramInstall.decodeList(hex("010001" + "05616263")))
        assertNull(ProgramInstall.decodeList(hex("010001" + "0161" + "00")))
        assertNull(ProgramInstall.decodeList(hex("010001" + "02c328")))
        assertNull(ProgramInstall.decodeList(hex("0103e9")))
    }
}
