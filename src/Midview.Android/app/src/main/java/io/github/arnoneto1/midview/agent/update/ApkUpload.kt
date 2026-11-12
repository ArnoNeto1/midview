package io.github.arnoneto1.midview.agent.update

import io.github.arnoneto1.midview.agent.apps.AppBlockStore
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.security.MessageDigest

// What came of an upload: the APK is there, whole and as offered; it was
// refused (the teacher's program is still there to be told why); or the
// connection dropped or went quiet midway (nobody left to tell).
sealed interface UploadResult {
    object Received : UploadResult

    data class Refused(val detail: String) : UploadResult

    data class Interrupted(val detail: String) : UploadResult
}

// The new APK as the Master sends it after UPDATE_ACCEPTED: FILE_CHUNKs, one
// after another with no answer between them, up to exactly the offered size
// (as UpdateReceiver on a PC), written to folder/agent.apk and hashed on the
// way. A chunk past that size, an empty one or another message refuses the
// upload, and so does a SHA-256 that is not the offer's. Whatever does not
// end in Received leaves no file behind. "Instalar programa" receives the
// teacher's APK the same way (TeacherInstalls), as folder/fileName, with no
// SHA-256 to compare (a PC's install request has none).
class ApkUpload(private val folder: File, private val fileName: String = FILE_NAME) {
    val file: File get() = File(folder, fileName)

    fun receive(size: Long, sha256: ByteArray?, receive: () -> Message): UploadResult {
        delete()
        val result = try {
            write(size, sha256, receive)
        } catch (e: IOException) {
            UploadResult.Interrupted(INTERRUPTED)
        }
        if (result != UploadResult.Received) delete()
        return result
    }

    // The folder emptied: the APK is Android's now, or was refused, or an
    // agent that stopped midway left it.
    fun delete() {
        folder.listFiles()?.forEach { it.delete() }
    }

    // A disk that refuses the APK does not stop the reading: a refusal sent
    // while the teacher's program still sends would be lost to the reset
    // connection. The connection's own failures (IOException) are thrown.
    private fun write(size: Long, sha256: ByteArray?, receive: () -> Message): UploadResult {
        var writeProblem: String? = null
        val out = try {
            folder.mkdirs()
            FileOutputStream(file)
        } catch (e: IOException) {
            writeProblem = notWritten(e)
            null
        }
        val hash = MessageDigest.getInstance("SHA-256")
        try {
            var received = 0L
            while (received < size) {
                val message = receive()
                if (message.type != MessageType.FILE_CHUNK || message.payload.isEmpty() || received + message.payload.size > size) {
                    return UploadResult.Refused(INVALID_TRANSFER)
                }
                hash.update(message.payload)
                received += message.payload.size
                if (out != null && writeProblem == null) {
                    try {
                        out.write(message.payload)
                    } catch (e: IOException) {
                        writeProblem = notWritten(e)
                    }
                }
            }
        } finally {
            try {
                out?.close()
            } catch (e: IOException) {
                if (writeProblem == null) writeProblem = notWritten(e)
            }
        }
        writeProblem?.let { return UploadResult.Refused(it) }
        return if (sha256 == null || MessageDigest.isEqual(hash.digest(), sha256)) UploadResult.Received else UploadResult.Refused(CORRUPT)
    }

    companion object {
        const val FILE_NAME = "agent.apk"
        const val INVALID_TRANSFER = "transferência inválida"
        const val CORRUPT = "o aplicativo chegou corrompido (o hash não confere)"
        const val INTERRUPTED = "o envio foi interrompido"

        private fun notWritten(e: IOException) = "o aplicativo não pôde ser gravado: ${AppBlockStore.saveError(e)}"
    }
}
