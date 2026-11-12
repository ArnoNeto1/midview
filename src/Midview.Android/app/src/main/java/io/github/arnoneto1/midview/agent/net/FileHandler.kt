package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.apps.AppBlockStore
import io.github.arnoneto1.midview.agent.files.FileNames
import io.github.arnoneto1.midview.agent.files.IncomingFile
import io.github.arnoneto1.midview.agent.files.PhoneFolder
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.FileHeader
import io.github.arnoneto1.midview.agent.protocol.FileTransfer
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import java.io.IOException

// "Enviar arquivo", "Recolher arquivos" and "Apagar as pastas dos alunos" on
// the commands port (28900), from a trusted teacher only (CommandHandler
// hands them over), with the PC Session's exchanges and words: the file
// lands in, and is collected from, the phone's Download/Midview
// (PhoneFolder). notify tells the student, as the PC's notice does; after
// is the "Depois de enviar" choice (open the folder or the file), answered
// with a note for the teacher (null for "nothing").
class FileHandler(
    private val folder: PhoneFolder,
    private val notify: (String) -> Unit,
    private val after: (afterSend: Int, savedName: String) -> String?,
    private val maxFiles: Int = FileTransfer.MAX_FILES,
    private val maxTotalBytes: Long = FileTransfer.MAX_TOTAL_BYTES,
) {
    // SendFile or SendFileWithOptions: accepted or refused up front (a first
    // CommandAck), before the teacher streams anything; a second one says
    // how it ended. The connection's own failures (IOException) are thrown:
    // there is nobody left to tell.
    fun receive(channel: Channel, header: Message) {
        val file = if (header.type == MessageType.SEND_FILE_WITH_OPTIONS) {
            FileTransfer.decodeOptionsHeader(header.payload)
        } else {
            FileTransfer.decodeHeader(header.payload)
        }
        val refusal = if (file == null) INVALID_REQUEST else folder.refusalFor(file.size)
        channel.send(Message(MessageType.COMMAND_ACK, CommandResult.encode(refusal == null, refusal ?: "")))
        if (file == null || refusal != null) return

        val (saved, detail, note) = save(channel, file)
        if (!saved) {
            channel.send(Message(MessageType.COMMAND_ACK, CommandResult.encode(false, detail)))
            return
        }
        val afterNote = try {
            after(file.afterSend, detail)
        } catch (e: RuntimeException) {
            "não abriu: ${e.message ?: e.javaClass.simpleName}"
        }
        channel.send(
            Message(
                MessageType.COMMAND_ACK,
                CommandResult.encode(true, detail + (note?.let { " ($it)" } ?: "") + (afterNote?.let { "; $it" } ?: "")),
            ),
        )
        // On a PC what opens on screen tells the student by itself. Android
        // can hold back an app opened from the background without a word (for
        // 5 s after the student pressed Home), so the quiet notice comes
        // either way; tapping it opens the folder.
        notify("O professor enviou o arquivo \"$detail\".\nEle foi salvo em: ${PhoneFolder.NAME}")
    }

    // Exactly size bytes in FileChunks. A storage that refuses the file does
    // not stop the reading: a refusal sent while the teacher still streams
    // would be lost to the reset connection. Saved: the detail is the name.
    private fun save(channel: Channel, file: FileHeader): Triple<Boolean, String, String?> {
        var problem: String? = null
        val incoming: IncomingFile? = try {
            folder.begin(FileNames.safe(file.name))
        } catch (e: IOException) {
            problem = notSaved(e)
            null
        }
        incoming.use {
            var received = 0L
            while (received < file.size) {
                val chunk = channel.receive()
                if (chunk.type != MessageType.FILE_CHUNK || chunk.payload.isEmpty() || received + chunk.payload.size > file.size) {
                    return Triple(false, INCOMPLETE, null)
                }
                received += chunk.payload.size
                if (incoming != null && problem == null) {
                    try {
                        incoming.write(chunk.payload)
                    } catch (e: IOException) {
                        problem = notSaved(e)
                    }
                }
            }
            problem?.let { return Triple(false, it, null) }
            return try {
                val (name, note) = incoming!!.complete(file.overwrite)
                Triple(true, name, note)
            } catch (e: IOException) {
                Triple(false, notSaved(e), null)
            }
        }
    }

    // CollectFiles: the files one after another, no answer from the teacher
    // in between, then one CommandAck. A file that cannot be read or would
    // pass the limits is left out and named in it; one that fails midway
    // cannot be taken back, so the connection drops and the teacher's
    // program discards it.
    fun collect(channel: Channel) {
        val files = try {
            folder.list(maxFiles)
        } catch (e: IOException) {
            channel.send(Message(MessageType.COMMAND_ACK, CommandResult.encode(false, "a pasta ${PhoneFolder.NAME} não pôde ser lida: ${readError(e)}")))
            return
        }

        var sent = 0
        var bytes = 0L
        val notSent = mutableListOf<String>()
        val buffer = ByteArray(FileTransfer.CHUNK_SIZE)
        for (file in files) {
            if (sent == maxFiles) break
            if (file.relativePath.toByteArray(Charsets.UTF_8).size > FileTransfer.MAX_NAME_BYTES) {
                notSent += "${file.relativePath} (caminho longo demais)"
                continue
            }
            if (bytes + file.size > maxTotalBytes) {
                notSent += "${file.relativePath} (passaria do limite de ${maxTotalBytes / (1024 * 1024)} MB)"
                continue
            }
            val stream = try {
                file.open()
            } catch (e: IOException) {
                notSent += "${file.relativePath} (não pôde ser lido)"
                continue
            } catch (e: SecurityException) {
                notSent += "${file.relativePath} (não pôde ser lido)"
                continue
            }
            stream.use {
                channel.send(Message(MessageType.SEND_FILE, FileTransfer.encodeHeader(file.relativePath, file.size)))
                var remaining = file.size
                while (remaining > 0) {
                    val read = it.read(buffer, 0, minOf(buffer.size.toLong(), remaining).toInt())
                    if (read <= 0) throw IOException("${file.relativePath} diminuiu durante o envio")
                    channel.send(Message(MessageType.FILE_CHUNK, buffer.copyOf(read)))
                    remaining -= read
                }
            }
            sent++
            bytes += file.size
        }

        // The listing stops just past the limit, so a full one means files went unsent.
        if (files.size > maxFiles) notSent += "o que passou de $maxFiles arquivos"

        val parts = mutableListOf<String>()
        if (notSent.isNotEmpty()) {
            parts += "${notSent.size} não enviado(s): ${notSent.take(3).joinToString(", ")}" +
                if (notSent.size > 3) " e mais ${notSent.size - 3}" else ""
        }
        if (!folder.seesEverything) parts += ONLY_OWN_FILES
        val detail = parts.joinToString("; ")
        channel.send(Message(MessageType.COMMAND_ACK, CommandResult.encode(true, detail)))

        var notice = if (sent > 0) {
            "O professor recolheu $sent arquivo(s) da pasta ${PhoneFolder.NAME}."
        } else {
            "O professor pediu os arquivos da pasta ${PhoneFolder.NAME}, mas nenhum foi enviado."
        }
        if (notSent.isNotEmpty()) {
            val first = parts.first()
            notice += "\n${first[0].uppercaseChar()}${first.substring(1)}."
        }
        notify(notice)
    }

    // "Apagar as pastas dos alunos": everything in Download/Midview, and the
    // student is told. The CommandAck's result.
    fun clear(): Pair<Boolean, String> {
        val (removed, problems) = folder.clear()
        if (removed > 0) notify("O professor esvaziou a pasta ${PhoneFolder.NAME}. O que estava nela foi apagado.")
        var detail = if (removed == 0 && problems.isEmpty()) "a pasta já estava vazia" else cleared(removed, problems)
        if (!folder.seesEverything) detail += "; $ONLY_OWN_FILES"
        return problems.isEmpty() to detail
    }

    companion object {
        const val INVALID_REQUEST = "pedido de envio inválido"
        const val INCOMPLETE = "a transferência chegou incompleta ou corrompida"

        // Said with what was collected or cleared when Android hides from the
        // agent the files other apps put in the folder.
        const val ONLY_OWN_FILES =
            "sem acesso a todos os arquivos neste celular: só os que o próprio Midview salvou"

        // "1 item foi apagado", "5 itens foram apagados", then what stayed:
        // "não pôde ser apagado: a.txt" or "não puderam ser apagados: a.txt, b".
        fun cleared(removed: Int, problems: List<String>): String {
            val done = if (removed == 1) "1 item foi apagado" else "$removed itens foram apagados"
            return when (problems.size) {
                0 -> done
                1 -> "$done; não pôde ser apagado: ${problems[0]}"
                else -> "$done; não puderam ser apagados: ${problems.joinToString(", ")}"
            }
        }

        private fun notSaved(e: IOException) = "não foi possível salvar o arquivo: ${describe(e)}"

        private fun describe(e: IOException) = AppBlockStore.saveError(e)

        private fun readError(e: IOException) =
            if (e.message.orEmpty().contains("EACCES") || e.message.orEmpty().contains("Permission denied")) "acesso negado" else "erro ao ler"
    }
}
