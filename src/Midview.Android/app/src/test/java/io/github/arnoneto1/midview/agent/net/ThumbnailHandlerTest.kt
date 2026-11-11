package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

class ThumbnailHandlerTest {
    @get:Rule val folder = TemporaryFolder()

    private val teacher = "A".repeat(40)

    private val trusted by lazy { TrustedMasters(File(folder.newFolder(), "trusted-masters.txt")) }
    private val asked = mutableListOf<Pair<Int, Int>>()
    private val handler by lazy {
        ThumbnailHandler(trusted) { width, height -> asked += width to height; byteArrayOf(width.toByte(), height.toByte()) }
    }

    private fun request(payload: ByteArray = ByteArray(0)) = Message(MessageType.REQUEST_THUMBNAIL, payload)

    @Test fun eachRequestGetsAFrameInTheAskedBox() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, request(byteArrayOf(0, 83, 0, 120)), request())

        handler.handle(channel)

        assertEquals(listOf(83 to 120, 320 to 180), asked)
        assertEquals(listOf(MessageType.THUMBNAIL_FRAME, MessageType.THUMBNAIL_FRAME), channel.sent.map { it.type })
        assertArrayEquals(byteArrayOf(83, 120), channel.sent[0].payload)
    }

    @Test fun anUntrustedTeacherGetsNothing() {
        trusted.add(teacher)
        val channel = FakeChannel("B".repeat(40), request())
        handler.handle(channel)
        assertTrue(channel.sent.isEmpty())
        assertTrue(channel.events.isEmpty())
        assertTrue(asked.isEmpty())
    }

    @Test fun anythingElseEndsTheConnection() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, Message(MessageType.HEARTBEAT, ByteArray(0)), request())
        handler.handle(channel)
        assertTrue(channel.sent.isEmpty())
    }
}
