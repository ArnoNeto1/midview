package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.files.FileNames
import io.github.arnoneto1.midview.agent.files.IncomingFile
import io.github.arnoneto1.midview.agent.files.PhoneFile
import io.github.arnoneto1.midview.agent.files.PhoneFolder
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.FileHeader
import io.github.arnoneto1.midview.agent.protocol.FileTransfer
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.IOException

// "Enviar arquivo", "Recolher arquivos" and "Apagar as pastas dos alunos" on
// a phone: the PC Session's exchanges, over a folder kept in memory.
class FileHandlerTest {
    // Download/Midview in memory: path -> content.
    private class FakeFolder : PhoneFolder {
        val files = linkedMapOf<String, ByteArray>()
        override var seesEverything = true
        var refusal: String? = null
        var failWritesAfter = Int.MAX_VALUE
        val unreadable = mutableSetOf<String>()
        var discarded = 0
        var clearProblems = emptyList<String>()

        override fun refusalFor(size: Long) = refusal

        override fun begin(safeName: String): IncomingFile = object : IncomingFile {
            val out = ByteArrayOutputStream()
            var writes = 0
            var completed = false

            override fun write(bytes: ByteArray) {
                if (++writes > failWritesAfter) throw IOException("ENOSPC (No space left on device)")
                out.write(bytes)
            }

            override fun complete(overwrite: Boolean): Pair<String, String?> {
                completed = true
                if (overwrite && safeName in files) {
                    files[safeName] = out.toByteArray()
                    return safeName to "substituiu o anterior"
                }
                val name = FileNames.unique(safeName) { it in files }
                files[name] = out.toByteArray()
                return name to null
            }

            override fun close() {
                if (!completed) discarded++
            }
        }

        override fun list(maxFiles: Int) = files.entries.take(maxFiles + 1).map { (path, content) ->
            PhoneFile(path, content.size.toLong()) {
                if (path in unreadable) throw IOException("EACCES") else ByteArrayInputStream(content)
            }
        }

        override fun clear(): Pair<Int, List<String>> {
            val removed = files.size
            files.clear()
            return removed to clearProblems
        }
    }

    private val folder = FakeFolder()
    private val notices = mutableListOf<String>()
    private val afters = mutableListOf<Pair<Int, String>>()
    private var afterAnswer: String? = null

    private fun handler(maxFiles: Int = FileTransfer.MAX_FILES, maxTotalBytes: Long = FileTransfer.MAX_TOTAL_BYTES) =
        FileHandler(folder, notify = { notices += it }, after = { a, n -> afters += a to n; afterAnswer }, maxFiles, maxTotalBytes)

    private fun chunk(vararg bytes: Byte) = Message(MessageType.FILE_CHUNK, bytes)

    private fun send(name: String, size: Long) = Message(MessageType.SEND_FILE, FileTransfer.encodeHeader(name, size))

    private fun acks(channel: FakeChannel) = channel.sent.map {
        assertEquals(MessageType.COMMAND_ACK, it.type)
        CommandResult.decode(it.payload)
    }

    @Test
    fun aFileIsAcceptedThenSavedAndTheStudentIsTold() {
        val channel = FakeChannel("T", chunk(1, 2, 3), chunk(4, 5))

        handler().receive(channel, send("..\\Lista de exercícios.pdf", 5))

        assertEquals(listOf(true to "", true to "Lista de exercícios.pdf"), acks(channel))
        assertArrayEquals(byteArrayOf(1, 2, 3, 4, 5), folder.files["Lista de exercícios.pdf"])
        assertEquals(listOf("O professor enviou o arquivo \"Lista de exercícios.pdf\".\nEle foi salvo em: Download/Midview"), notices)
        assertEquals(listOf(FileTransfer.AFTER_NOTHING to "Lista de exercícios.pdf"), afters)
    }

    @Test
    fun anEmptyFileTakesNoChunks() {
        val channel = FakeChannel("T")
        handler().receive(channel, send("vazio.txt", 0))
        assertEquals(listOf(true to "", true to "vazio.txt"), acks(channel))
        assertEquals(0, folder.files["vazio.txt"]!!.size)
    }

    @Test
    fun aSecondCopyGetsItsOwnName_unlessTheTeacherReplacesIt() {
        folder.files["notas.pdf"] = byteArrayOf(9)
        val copy = FakeChannel("T", chunk(1))
        handler().receive(copy, send("notas.pdf", 1))
        assertEquals(true to "notas (2).pdf", acks(copy)[1])

        val replace = FakeChannel("T", chunk(2))
        val options = FileTransfer.encodeOptionsHeader(FileHeader("notas.pdf", 1, overwrite = true))
        handler().receive(replace, Message(MessageType.SEND_FILE_WITH_OPTIONS, options))
        assertEquals(true to "notas.pdf (substituiu o anterior)", acks(replace)[1])
        assertArrayEquals(byteArrayOf(2), folder.files["notas.pdf"])
    }

    // Android may hold an app back without a word, so the notice comes anyway.
    @Test
    fun whatOpensIsSaidToTheTeacher_andTheStudentIsToldAnyway() {
        afterAnswer = "aberto no aluno"
        val channel = FakeChannel("T", chunk(7))
        val options = FileTransfer.encodeOptionsHeader(FileHeader("foto.jpg", 1, afterSend = FileTransfer.AFTER_OPEN_FILE))

        handler().receive(channel, Message(MessageType.SEND_FILE_WITH_OPTIONS, options))

        assertEquals(true to "foto.jpg; aberto no aluno", acks(channel)[1])
        assertEquals(listOf(FileTransfer.AFTER_OPEN_FILE to "foto.jpg"), afters)
        assertEquals(1, notices.size)
    }

    @Test
    fun whatDidNotOpenIsSaid_andTheStudentIsTold() {
        afterAnswer = "a tela está bloqueada: não foi aberto"
        val channel = FakeChannel("T", chunk(7))
        val options = FileTransfer.encodeOptionsHeader(FileHeader("foto.jpg", 1, afterSend = FileTransfer.AFTER_OPEN_FOLDER))
        handler().receive(channel, Message(MessageType.SEND_FILE_WITH_OPTIONS, options))
        assertEquals(true to "foto.jpg; a tela está bloqueada: não foi aberto", acks(channel)[1])
        assertEquals(1, notices.size)
    }

    @Test
    fun aBadHeaderIsRefusedUpFront() {
        val channel = FakeChannel("T", chunk(1))
        handler().receive(channel, Message(MessageType.SEND_FILE, ByteArray(3)))
        assertEquals(listOf(false to FileHandler.INVALID_REQUEST), acks(channel))
        val options = FakeChannel("T")
        handler().receive(options, Message(MessageType.SEND_FILE_WITH_OPTIONS, byteArrayOf(5, 0) + FileTransfer.encodeHeader("a", 1)))
        assertEquals(listOf(false to FileHandler.INVALID_REQUEST), acks(options))
        assertTrue(folder.files.isEmpty())
    }

    @Test
    fun aFullPhoneRefusesUpFront() {
        folder.refusal = "não há espaço livre suficiente no celular"
        val channel = FakeChannel("T", chunk(1))
        handler().receive(channel, send("a.txt", 1))
        assertEquals(listOf(false to "não há espaço livre suficiente no celular"), acks(channel))
    }

    @Test
    fun moreBytesThanAnnouncedIsDiscarded() {
        val channel = FakeChannel("T", chunk(1, 2), chunk(3, 4))
        handler().receive(channel, send("a.txt", 3))
        assertEquals(listOf(true to "", false to FileHandler.INCOMPLETE), acks(channel))
        assertTrue(folder.files.isEmpty())
        assertEquals(1, folder.discarded)
        assertTrue(notices.isEmpty())
    }

    @Test
    fun anythingButAChunkIsDiscarded() {
        val channel = FakeChannel("T", chunk(1), Message(MessageType.HEARTBEAT, ByteArray(0)))
        handler().receive(channel, send("a.txt", 3))
        assertEquals(false to FileHandler.INCOMPLETE, acks(channel)[1])
        assertEquals(1, folder.discarded)
    }

    // The rest is still read, so the refusal reaches the teacher.
    @Test
    fun aStorageThatFailsMidwayStillReadsToTheEnd() {
        folder.failWritesAfter = 1
        val channel = FakeChannel("T", chunk(1), chunk(2), chunk(3))
        handler().receive(channel, send("a.txt", 3))
        assertEquals(false to "não foi possível salvar o arquivo: sem espaço no celular", acks(channel)[1])
        assertEquals(3, channel.events.count { it.startsWith("receive") })
        assertTrue(folder.files.isEmpty())
    }

    @Test
    fun aConnectionThatDropsMidwayLeavesNothing() {
        try {
            handler().receive(FakeChannel("T", chunk(1)), send("a.txt", 3))
            throw AssertionError("The dropped connection was not thrown")
        } catch (e: IOException) {
            // As expected: nobody is left to answer.
        }
        assertTrue(folder.files.isEmpty())
        assertEquals(1, folder.discarded)
    }

    @Test
    fun collectingSendsEveryFileThenOneAck() {
        folder.files["a.pdf"] = ByteArray(FileTransfer.CHUNK_SIZE + 10) { it.toByte() }
        folder.files["sub/b.txt"] = byteArrayOf(1, 2)
        val channel = FakeChannel("T")

        handler().collect(channel)

        assertEquals(
            listOf(
                MessageType.SEND_FILE, MessageType.FILE_CHUNK, MessageType.FILE_CHUNK,
                MessageType.SEND_FILE, MessageType.FILE_CHUNK, MessageType.COMMAND_ACK,
            ),
            channel.sent.map { it.type },
        )
        assertEquals(FileHeader("a.pdf", FileTransfer.CHUNK_SIZE + 10L), FileTransfer.decodeHeader(channel.sent[0].payload))
        assertEquals(FileTransfer.CHUNK_SIZE, channel.sent[1].payload.size)
        assertArrayEquals(folder.files["a.pdf"], channel.sent[1].payload + channel.sent[2].payload)
        assertEquals(FileHeader("sub/b.txt", 2), FileTransfer.decodeHeader(channel.sent[3].payload))
        assertEquals(true to "", CommandResult.decode(channel.sent.last().payload))
        assertEquals(listOf("O professor recolheu 2 arquivo(s) da pasta Download/Midview."), notices)
        assertEquals(2, folder.files.size) // collecting never removes anything
    }

    @Test
    fun anEmptyFolderIsASuccessWithNothing() {
        val channel = FakeChannel("T")
        handler().collect(channel)
        assertEquals(listOf(true to ""), acks(channel))
        assertEquals(listOf("O professor pediu os arquivos da pasta Download/Midview, mas nenhum foi enviado."), notices)
    }

    @Test
    fun whatCannotGoIsNamedInTheAck() {
        folder.files["grande.mp4"] = ByteArray(10)
        folder.files["ok.txt"] = ByteArray(2)
        folder.files["preso.pdf"] = ByteArray(1)
        folder.files["x".repeat(FileTransfer.MAX_NAME_BYTES + 1)] = ByteArray(1)
        folder.unreadable += "preso.pdf"
        val channel = FakeChannel("T")

        handler(maxTotalBytes = 5).collect(channel)

        val (ok, detail) = CommandResult.decode(channel.sent.last().payload)
        assertTrue(ok)
        assertTrue(detail, detail.startsWith("3 não enviado(s): grande.mp4 (passaria do limite de 0 MB), preso.pdf (não pôde ser lido), xxx"))
        assertTrue(detail, detail.endsWith("(caminho longo demais)"))
        assertEquals(1, channel.sent.count { it.type == MessageType.SEND_FILE })
        assertTrue(notices.single().startsWith("O professor recolheu 1 arquivo(s) da pasta Download/Midview.\n3 não enviado(s): "))
    }

    @Test
    fun moreFilesThanTheLimitAreNamed() {
        for (i in 1..4) folder.files["f$i.txt"] = byteArrayOf(1)
        val channel = FakeChannel("T")
        handler(maxFiles = 2).collect(channel)
        assertEquals(2, channel.sent.count { it.type == MessageType.SEND_FILE })
        assertEquals(true to "1 não enviado(s): o que passou de 2 arquivos", CommandResult.decode(channel.sent.last().payload))
    }

    @Test
    fun withoutAllFilesAccessTheTeacherIsTold() {
        folder.seesEverything = false
        folder.files["a.txt"] = byteArrayOf(1)
        val channel = FakeChannel("T")
        handler().collect(channel)
        assertEquals(true to FileHandler.ONLY_OWN_FILES, CommandResult.decode(channel.sent.last().payload))
        assertEquals(true to "1 item foi apagado; ${FileHandler.ONLY_OWN_FILES}", handler().clear())
    }

    @Test
    fun clearingEmptiesTheFolderAndTellsTheStudent() {
        folder.files["a.txt"] = byteArrayOf(1)
        folder.files["b.txt"] = byteArrayOf(1)
        assertEquals(true to "2 itens foram apagados", handler().clear())
        assertEquals(listOf("O professor esvaziou a pasta Download/Midview. O que estava nela foi apagado."), notices)
        assertEquals(true to "a pasta já estava vazia", handler().clear())
        assertEquals(1, notices.size)
    }

    @Test
    fun whatCouldNotBeClearedIsNamed() {
        folder.files["a.txt"] = byteArrayOf(1)
        folder.clearProblems = listOf("link (pasta vinculada)")
        assertEquals(false to "1 item foi apagado; não pôde ser apagado: link (pasta vinculada)", handler().clear())
        assertEquals(1, notices.size)
    }

    @Test
    fun theCountIsSingularOrPlural() {
        assertEquals("1 item foi apagado", FileHandler.cleared(1, emptyList()))
        assertEquals("0 itens foram apagados; não pôde ser apagado: a.txt", FileHandler.cleared(0, listOf("a.txt")))
        assertEquals("3 itens foram apagados; não puderam ser apagados: a.txt, b", FileHandler.cleared(3, listOf("a.txt", "b")))
    }
}
