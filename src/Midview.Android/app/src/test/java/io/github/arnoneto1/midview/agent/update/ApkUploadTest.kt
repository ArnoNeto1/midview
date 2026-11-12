package io.github.arnoneto1.midview.agent.update

import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.EOFException
import java.io.File
import java.net.SocketTimeoutException
import java.security.MessageDigest

class ApkUploadTest {
    @get:Rule val folder = TemporaryFolder()

    private val apk = ByteArray(600_000) { (it * 31 + 7).toByte() }
    private val sha256 = MessageDigest.getInstance("SHA-256").digest(apk)
    private val updateFolder by lazy { File(folder.root, "update") }
    private val upload by lazy { ApkUpload(updateFolder) }

    // The Master's chunks (FileTransfer.ChunkSize), one after another.
    private fun chunks(bytes: ByteArray, size: Int = 256 * 1024): List<Message> =
        (bytes.indices step size).map { Message(MessageType.FILE_CHUNK, bytes.copyOfRange(it, minOf(it + size, bytes.size))) }

    // The connection: the messages in order, then closed (or the failure given).
    private class Script(messages: List<Message>, private val end: Exception = EOFException("closed")) {
        val queue = ArrayDeque(messages)
        fun receive(): Message = queue.removeFirstOrNull() ?: throw end
    }

    @Test fun theWholeApkIsWritten() {
        val script = Script(chunks(apk))
        assertEquals(UploadResult.Received, upload.receive(apk.size.toLong(), sha256, script::receive))
        assertArrayEquals(apk, upload.file.readBytes())
        assertEquals(File(updateFolder, "agent.apk"), upload.file)
        assertTrue(script.queue.isEmpty())
    }

    // Nothing read past the offered size: what follows is the next message.
    @Test fun nothingIsReadPastTheOfferedSize() {
        val script = Script(chunks(apk) + Message(MessageType.HEARTBEAT, ByteArray(0)))
        assertEquals(UploadResult.Received, upload.receive(apk.size.toLong(), sha256, script::receive))
        assertEquals(1, script.queue.size)
    }

    @Test fun aChunkPastTheOfferedSizeRefusesAndDeletes() {
        val result = upload.receive(apk.size.toLong() - 10, sha256, Script(chunks(apk))::receive)
        assertEquals(UploadResult.Refused("transferência inválida"), result)
        assertFalse(upload.file.exists())
    }

    @Test fun anotherMessageMidwayRefusesAndDeletes() {
        val messages = chunks(apk).toMutableList().apply { add(1, Message(MessageType.HEARTBEAT, ByteArray(0))) }
        assertEquals(UploadResult.Refused("transferência inválida"), upload.receive(apk.size.toLong(), sha256, Script(messages)::receive))
        assertFalse(upload.file.exists())
    }

    @Test fun anEmptyChunkRefuses() {
        val messages = listOf(Message(MessageType.FILE_CHUNK, ByteArray(0))) + chunks(apk)
        assertEquals(UploadResult.Refused("transferência inválida"), upload.receive(apk.size.toLong(), sha256, Script(messages)::receive))
        assertFalse(upload.file.exists())
    }

    @Test fun aWrongSha256RefusesAndDeletes() {
        val other = sha256.copyOf().also { it[0] = (it[0] + 1).toByte() }
        assertEquals(
            UploadResult.Refused("o aplicativo chegou corrompido (o hash não confere)"),
            upload.receive(apk.size.toLong(), other, Script(chunks(apk))::receive),
        )
        assertFalse(upload.file.exists())
    }

    // Wi-Fi gone or the teacher's program closed: nothing to answer.
    @Test fun aConnectionThatDropsInterruptsAndDeletes() {
        val half = chunks(apk).take(1)
        assertEquals(
            UploadResult.Interrupted("o envio foi interrompido"),
            upload.receive(apk.size.toLong(), sha256, Script(half)::receive),
        )
        assertFalse(upload.file.exists())
    }

    @Test fun aConnectionThatGoesQuietInterrupts() {
        val half = chunks(apk).take(2)
        assertEquals(
            UploadResult.Interrupted("o envio foi interrompido"),
            upload.receive(apk.size.toLong(), sha256, Script(half, SocketTimeoutException("Read timed out"))::receive),
        )
        assertFalse(upload.file.exists())
    }

    // As on a PC: the rest is read, so the answer is not lost to a reset
    // connection.
    @Test fun anApkThatCannotBeWrittenIsReadToTheEndThenRefused() {
        updateFolder.writeText("not a folder")
        val script = Script(chunks(apk))
        val result = upload.receive(apk.size.toLong(), sha256, script::receive)
        assertTrue(result.toString(), result is UploadResult.Refused)
        assertTrue((result as UploadResult.Refused).detail, result.detail.startsWith("o aplicativo não pôde ser gravado: "))
        assertTrue(script.queue.isEmpty())
    }

    @Test fun whatAnEarlierUploadLeftIsReplaced() {
        updateFolder.mkdirs()
        File(updateFolder, "agent.apk").writeText("half of an older upload")
        File(updateFolder, "leftover.tmp").writeText("x")
        assertEquals(UploadResult.Received, upload.receive(apk.size.toLong(), sha256, Script(chunks(apk))::receive))
        assertArrayEquals(apk, upload.file.readBytes())
        assertEquals(listOf("agent.apk"), updateFolder.list()!!.toList())
    }

    @Test fun deleteEmptiesTheFolder() {
        upload.receive(apk.size.toLong(), sha256, Script(chunks(apk))::receive)
        upload.delete()
        assertEquals(emptyList<String>(), updateFolder.list()?.toList().orEmpty())
        // Nothing there: nothing to do.
        upload.delete()
    }
}
