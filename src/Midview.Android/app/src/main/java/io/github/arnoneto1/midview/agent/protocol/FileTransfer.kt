package io.github.arnoneto1.midview.agent.protocol

import java.nio.ByteBuffer
import java.nio.charset.CharacterCodingException
import java.nio.charset.CodingErrorAction

// The teacher's file, as the header of SendFile / SendFileWithOptions says
// it: the name (just as sent: FileNames.safe makes it a name to save under),
// the size, and the "Enviar arquivo" options.
data class FileHeader(
    val name: String,
    val size: Long,
    val overwrite: Boolean = false,
    val afterSend: Int = FileTransfer.AFTER_NOTHING,
)

// The wire format of src/Midview.Protocol/FileTransfer.cs (SendFile,
// FileChunk, SendFileWithOptions) and the limits of FileCollection.cs
// (CollectFiles), with the same numbers on both ends.
object FileTransfer {
    // 1 GiB: what a teacher hands out, and a cap on the phone's storage.
    const val MAX_FILE_SIZE = 1L shl 30

    // Each FileChunk's payload at most.
    const val CHUNK_SIZE = 256 * 1024

    const val MAX_NAME_BYTES = 1024

    // "Recolher arquivos", per phone per collection.
    const val MAX_FILES = 1000
    const val MAX_TOTAL_BYTES = 500L * 1024 * 1024

    // Path segments, the file name included: the folder and 7 levels of subfolders.
    const val MAX_DEPTH = 8

    // AfterSend (src/Midview.Protocol/SendFileOptions.cs): what the phone
    // does once the file is saved.
    const val AFTER_NOTHING = 0
    const val AFTER_OPEN_FOLDER = 1
    const val AFTER_OPEN_FILE = 2

    // SendFile payload: the size (int64, big-endian), then the name (UTF-8):
    // a name from the teacher, a '/'-separated path inside the folder when
    // collecting.
    fun encodeHeader(name: String, size: Long): ByteArray {
        val text = name.toByteArray(Charsets.UTF_8)
        val header = ByteArray(8 + text.size)
        ByteBuffer.wrap(header).putLong(size)
        text.copyInto(header, 8)
        return header
    }

    // Null for anything FileTransfer.TryDecodeHeader refuses: too short or
    // too long, a size outside 0..1 GiB, invalid UTF-8, a blank name.
    fun decodeHeader(payload: ByteArray): FileHeader? {
        if (payload.size <= 8 || payload.size > 8 + MAX_NAME_BYTES) return null
        val size = ByteBuffer.wrap(payload).getLong()
        if (size < 0 || size > MAX_FILE_SIZE) return null
        val name = try {
            Charsets.UTF_8.newDecoder()
                .onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT)
                .decode(ByteBuffer.wrap(payload, 8, payload.size - 8))
                .toString()
        } catch (e: CharacterCodingException) {
            return null
        }
        return if (name.isBlank()) null else FileHeader(name, size)
    }

    // SendFileWithOptions payload: overwrite (0 or 1), the AfterSend action,
    // then a SendFile header.
    fun decodeOptionsHeader(payload: ByteArray): FileHeader? {
        if (payload.size < 2) return null
        val overwrite = payload[0].toInt()
        val afterSend = payload[1].toInt()
        if (overwrite !in 0..1 || afterSend !in AFTER_NOTHING..AFTER_OPEN_FILE) return null
        return decodeHeader(payload.copyOfRange(2, payload.size))?.copy(overwrite = overwrite == 1, afterSend = afterSend)
    }

    fun encodeOptionsHeader(header: FileHeader): ByteArray =
        byteArrayOf(if (header.overwrite) 1 else 0, header.afterSend.toByte()) + encodeHeader(header.name, header.size)
}
