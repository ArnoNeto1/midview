package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.nio.ByteBuffer

// The bytes of src/Midview.Protocol/FileTransfer.cs, both ways.
class FileTransferTest {
    private fun header(size: Long, name: ByteArray) = ByteBuffer.allocate(8 + name.size).putLong(size).put(name).array()

    @Test
    fun aHeaderIsTheSizeBigEndianThenTheName() {
        assertArrayEquals(
            byteArrayOf(0, 0, 0, 0, 0, 0, 1, 2, 'a'.code.toByte(), '.'.code.toByte(), 't'.code.toByte()),
            FileTransfer.encodeHeader("a.t", 258),
        )
        assertEquals(FileHeader("Lição.pdf", 1234), FileTransfer.decodeHeader(FileTransfer.encodeHeader("Lição.pdf", 1234)))
    }

    @Test
    fun sizesOutsideZeroToOneGibAreRefused() {
        assertEquals(0L, FileTransfer.decodeHeader(header(0, "a".toByteArray()))!!.size)
        assertEquals(FileTransfer.MAX_FILE_SIZE, FileTransfer.decodeHeader(header(1L shl 30, "a".toByteArray()))!!.size)
        assertNull(FileTransfer.decodeHeader(header((1L shl 30) + 1, "a".toByteArray())))
        assertNull(FileTransfer.decodeHeader(header(-1, "a".toByteArray())))
    }

    @Test
    fun badNamesAreRefused() {
        assertNull(FileTransfer.decodeHeader(ByteArray(8)))
        assertNull(FileTransfer.decodeHeader(header(1, "  ".toByteArray())))
        assertNull(FileTransfer.decodeHeader(header(1, byteArrayOf(0xC3.toByte(), 0x28))))
        assertNull(FileTransfer.decodeHeader(header(1, ByteArray(FileTransfer.MAX_NAME_BYTES + 1) { 'a'.code.toByte() })))
        assertEquals(1024, FileTransfer.decodeHeader(header(1, ByteArray(FileTransfer.MAX_NAME_BYTES) { 'a'.code.toByte() }))!!.name.length)
    }

    @Test
    fun theOptionsComeBeforeTheHeader() {
        val sent = FileHeader("notas.pdf", 9, overwrite = true, afterSend = FileTransfer.AFTER_OPEN_FILE)
        val payload = FileTransfer.encodeOptionsHeader(sent)
        assertEquals(1, payload[0].toInt())
        assertEquals(2, payload[1].toInt())
        assertEquals(sent, FileTransfer.decodeOptionsHeader(payload))
    }

    @Test
    fun unknownOptionsAreRefused() {
        val rest = FileTransfer.encodeHeader("a", 1)
        assertNull(FileTransfer.decodeOptionsHeader(byteArrayOf(2, 0) + rest))
        assertNull(FileTransfer.decodeOptionsHeader(byteArrayOf(0, 3) + rest))
        assertNull(FileTransfer.decodeOptionsHeader(byteArrayOf(0)))
        assertEquals(FileHeader("a", 1, false, FileTransfer.AFTER_OPEN_FOLDER), FileTransfer.decodeOptionsHeader(byteArrayOf(0, 1) + rest))
    }
}
