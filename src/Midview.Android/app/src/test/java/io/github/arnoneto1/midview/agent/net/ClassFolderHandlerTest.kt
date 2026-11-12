package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.classfolder.ClassFileTimes
import io.github.arnoneto1.midview.agent.classfolder.PhoneClassFolder
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.protocol.ClassFileRef
import io.github.arnoneto1.midview.agent.protocol.ClassFolderWire
import io.github.arnoneto1.midview.agent.protocol.ClassLesson
import io.github.arnoneto1.midview.agent.protocol.ClassPushHeader
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.FileTransfer
import io.github.arnoneto1.midview.agent.protocol.LessonId
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

// The class folder's messages on the screen port, as a PC Session answers them.
class ClassFolderHandlerTest {
    @get:Rule val temp = TemporaryFolder()

    private val teacher = "A".repeat(40)
    private val lessonId = LessonId(ByteArray(16) { 1 })
    private val lesson = ClassLesson(10, lessonId, "3º A", 1_700_000_000, ended = false)
    private val root by lazy { temp.newFolder("Midview") }
    private val classDir by lazy { File(root, lesson.folderName).apply { mkdirs() } }
    private val folder by lazy {
        PhoneClassFolder(root, { lesson }, ClassFileTimes(File(temp.root, "times.txt")), { true }, { Long.MAX_VALUE }, listingPartSize = 2)
    }
    private val handler by lazy { ClassFolderHandler(folder) }

    private fun ack(message: Message) = message.also { assertEquals(MessageType.COMMAND_ACK, it.type) }.let { CommandResult.decode(it.payload) }

    @Test
    fun theListingComesInParts() {
        for (i in 1..3) File(classDir, "f$i.txt").writeText("x")
        val channel = FakeChannel(teacher)
        handler.serve(channel, Message(MessageType.LIST_CLASS_FOLDER, ByteArray(0)))
        assertEquals(listOf(MessageType.CLASS_FOLDER_LISTING, MessageType.CLASS_FOLDER_LISTING), channel.sent.map { it.type })
        assertEquals(1, channel.sent[0].payload[18].toInt())
        assertEquals(0, channel.sent[1].payload[18].toInt())
    }

    @Test
    fun aPushIsAcceptedReceivedAndSaved() {
        classDir
        val bytes = "conteúdo".toByteArray()
        val header = ClassPushHeader(lessonId, "pasta/nota.txt", bytes.size.toLong(), 638_600_000_000_000_000, null)
        val channel = FakeChannel(teacher, Message(MessageType.FILE_CHUNK, bytes))
        handler.serve(channel, Message(MessageType.PUSH_CLASS_FILE, ClassFolderWire.encodePushHeader(header)))
        assertEquals(listOf(true to "", true to ""), channel.sent.map(::ack))
        assertEquals("conteúdo", File(classDir, "pasta/nota.txt").readText())
    }

    @Test
    fun aPushForAnotherLessonIsRefusedUpFront() {
        classDir
        val header = ClassPushHeader(LessonId(ByteArray(16) { 9 }), "nota.txt", 1, 638_600_000_000_000_000, null)
        val channel = FakeChannel(teacher)
        handler.serve(channel, Message(MessageType.PUSH_CLASS_FILE, ClassFolderWire.encodePushHeader(header)))
        assertEquals(listOf(false to "aula diferente"), channel.sent.map(::ack))
    }

    @Test
    fun aShortTransferIsNotSaved() {
        classDir
        val header = ClassPushHeader(lessonId, "nota.txt", 10, 638_600_000_000_000_000, null)
        val channel = FakeChannel(teacher, Message(MessageType.FILE_CHUNK, ByteArray(4)), Message(MessageType.HEARTBEAT, ByteArray(0)))
        handler.serve(channel, Message(MessageType.PUSH_CLASS_FILE, ClassFolderWire.encodePushHeader(header)))
        assertEquals(listOf(true to "", false to "a transferência chegou incompleta ou corrompida"), channel.sent.map(::ack))
        assertTrue(classDir.list()!!.isEmpty())
    }

    @Test
    fun aPullSendsTheFileThenSaysItAllWent() {
        File(classDir, "trabalho.docx").writeText("meu trabalho")
        val channel = FakeChannel(teacher)
        handler.serve(channel, Message(MessageType.PULL_CLASS_FILE, ClassFolderWire.encodeFileRef(ClassFileRef(lessonId, "trabalho.docx"))))
        assertEquals(listOf(MessageType.SEND_FILE, MessageType.FILE_CHUNK, MessageType.COMMAND_ACK), channel.sent.map { it.type })
        assertEquals("trabalho.docx", FileTransfer.decodeHeader(channel.sent[0].payload)!!.name)
        assertEquals("meu trabalho", String(channel.sent[1].payload))

        val missing = FakeChannel(teacher)
        handler.serve(missing, Message(MessageType.PULL_CLASS_FILE, ClassFolderWire.encodeFileRef(ClassFileRef(lessonId, "nada.txt"))))
        assertEquals(listOf(false to "arquivo não encontrado"), missing.sent.map(::ack))
    }

    @Test
    fun aBadPayloadIsRefused() {
        val channel = FakeChannel(teacher)
        handler.serve(channel, Message(MessageType.DELETE_CLASS_FILE, byteArrayOf(1, 2, 3)))
        assertEquals(listOf(false to "pedido inválido"), channel.sent.map(::ack))
    }

    @Test
    fun theScreenPortHandsTheClassFolderOverOnlyToATrustedTeacher() {
        val trusted = TrustedMasters(File(temp.newFolder(), "trusted.txt")).apply { add(teacher) }
        val served = mutableListOf<Int>()
        val screen = ThumbnailHandler(trusted, render = { _, _ -> ByteArray(0) }, classFolder = { _, first -> served += first.type })
        screen.handle(FakeChannel(teacher, Message(MessageType.LIST_CLASS_FOLDER, ByteArray(0))))
        screen.handle(FakeChannel("B".repeat(40), Message(MessageType.LIST_CLASS_FOLDER, ByteArray(0))))
        assertEquals(listOf(MessageType.LIST_CLASS_FOLDER), served)

        // A phone without it hangs up, as one from before it did.
        val old = FakeChannel(teacher, Message(MessageType.LIST_CLASS_FOLDER, ByteArray(0)))
        ThumbnailHandler(trusted, render = { _, _ -> ByteArray(0) }).handle(old)
        assertFalse(old.sent.any())
    }
}
