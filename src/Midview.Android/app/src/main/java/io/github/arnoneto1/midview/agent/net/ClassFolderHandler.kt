package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.classfolder.ClassFolderRefusal
import io.github.arnoneto1.midview.agent.classfolder.PhoneClassFolder
import io.github.arnoneto1.midview.agent.protocol.ClassFolderWire
import io.github.arnoneto1.midview.agent.protocol.ClassPushHeader
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.FileTransfer
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import java.io.FileInputStream
import java.io.IOException

// The class folder ("Pasta da turma") the teacher's Master lists, reads and
// writes on the screen port (5901), as a PC's Session does
// (src/Midview.Session/SessionServer.ClassFolder.cs), with the same messages
// and answers, each the first message of a short connection from a trusted
// teacher (ThumbnailHandler checks that). Nothing here tells the student:
// the files simply show up in the folder.
class ClassFolderHandler(private val folder: PhoneClassFolder) {
    fun serve(channel: Channel, message: Message) {
        when (message.type) {
            MessageType.LIST_CLASS_FOLDER -> {
                val listing = folder.list()
                for (part in ClassFolderWire.encodeListingParts(listing.current, listing.problem, listing.entries, folder.listingPartSize)) {
                    channel.send(Message(MessageType.CLASS_FOLDER_LISTING, part))
                }
            }
            MessageType.PULL_CLASS_FILE -> sendFile(channel, message.payload)
            MessageType.PUSH_CLASS_FILE -> receiveFile(channel, message.payload)
            MessageType.DELETE_CLASS_FILE -> {
                val ref = ClassFolderWire.decodeFileRef(message.payload)
                result(channel, ref?.let { folder.delete(it.lessonId, it.path) } ?: (false to INVALID))
            }
            MessageType.RENAME_CLASS_FILE -> {
                val rename = ClassFolderWire.decodeRename(message.payload)
                result(channel, rename?.let { folder.rename(it.lessonId, it.path, it.newPath) } ?: (false to INVALID))
            }
            MessageType.CLEAR_SET_ASIDE -> {
                val clear = ClassFolderWire.decodeClearSetAside(message.payload)
                result(channel, clear?.let { folder.clearSetAside(it.first, it.second) } ?: (false to INVALID))
            }
            else -> result(channel, false to INVALID)
        }
    }

    // A SendFile header and FileChunks, then a CommandAck; or one CommandAck
    // alone for a file that can't be found or read. A file that fails or
    // changes length midway drops the connection, and the Master discards it.
    private fun sendFile(channel: Channel, payload: ByteArray) {
        val ref = ClassFolderWire.decodeFileRef(payload) ?: return result(channel, false to INVALID)
        val file = folder.resolveExisting(ref.lessonId, ref.path) ?: return result(channel, false to PhoneClassFolder.NOT_FOUND)
        val stream = try {
            FileInputStream(file)
        } catch (e: IOException) {
            return result(channel, false to "não foi possível ler o arquivo")
        } catch (e: SecurityException) {
            return result(channel, false to "não foi possível ler o arquivo")
        }
        stream.use {
            val size = stream.channel.size()
            if (size > FileTransfer.MAX_FILE_SIZE) return result(channel, false to "arquivo grande demais")
            channel.send(Message(MessageType.SEND_FILE, FileTransfer.encodeHeader(ref.path, size)))
            val buffer = ByteArray(FileTransfer.CHUNK_SIZE)
            var remaining = size
            while (remaining > 0) {
                val read = stream.read(buffer, 0, minOf(buffer.size.toLong(), remaining).toInt())
                if (read <= 0) throw IOException("${ref.path} diminuiu durante o envio")
                channel.send(Message(MessageType.FILE_CHUNK, buffer.copyOf(read)))
                remaining -= read
            }
            if (stream.channel.size() != size) throw IOException("${ref.path} mudou de tamanho durante o envio")
        }
        result(channel, true to "")
    }

    // Accepted or refused up front (a first CommandAck), then the FileChunks,
    // then a second CommandAck saying whether it was saved. Only the lesson
    // on takes files.
    private fun receiveFile(channel: Channel, payload: ByteArray) {
        val header: ClassPushHeader = ClassFolderWire.decodePushHeader(payload) ?: return result(channel, false to INVALID_PUSH)
        val incoming = try {
            folder.beginPush(header).also { incoming ->
                folder.refusalFor(header.size)?.let {
                    incoming.close()
                    return result(channel, false to it)
                }
            }
        } catch (e: ClassFolderRefusal) {
            return result(channel, false to (e.message ?: INVALID_PUSH))
        } catch (e: IOException) {
            return result(channel, false to "não foi possível salvar o arquivo: ${e.message ?: "erro de gravação"}")
        }
        val outcome = incoming.use {
            result(channel, true to "")
            save(channel, it, header.size)
        }
        result(channel, outcome)
    }

    private fun save(channel: Channel, incoming: PhoneClassFolder.Incoming, size: Long): Pair<Boolean, String> {
        var received = 0L
        while (received < size) {
            val chunk = channel.receive()
            if (chunk.type != MessageType.FILE_CHUNK || chunk.payload.isEmpty() || received + chunk.payload.size > size) {
                return false to "a transferência chegou incompleta ou corrompida"
            }
            try {
                incoming.write(chunk.payload)
            } catch (e: IOException) {
                return false to "não foi possível salvar o arquivo: ${e.message ?: "erro de gravação"}"
            }
            received += chunk.payload.size
        }
        return try {
            incoming.complete()
            true to ""
        } catch (e: ClassFolderRefusal) {
            false to (e.message ?: PhoneClassFolder.CHANGED)
        } catch (e: IOException) {
            false to "não foi possível salvar o arquivo: ${e.message ?: "erro de gravação"}"
        }
    }

    private fun result(channel: Channel, result: Pair<Boolean, String>) {
        channel.send(Message(MessageType.COMMAND_ACK, CommandResult.encode(result.first, result.second)))
    }

    companion object {
        const val INVALID = "pedido inválido"
        const val INVALID_PUSH = "pedido de envio inválido"

        val MESSAGES = setOf(
            MessageType.LIST_CLASS_FOLDER, MessageType.PULL_CLASS_FILE, MessageType.PUSH_CLASS_FILE,
            MessageType.DELETE_CLASS_FILE, MessageType.RENAME_CLASS_FILE, MessageType.CLEAR_SET_ASIDE,
        )
    }
}
