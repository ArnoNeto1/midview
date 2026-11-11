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
    private var teachers = 0
    private val streamed = mutableListOf<Channel>()
    private val locks = mutableListOf<Channel>()
    private val handler by lazy {
        ThumbnailHandler(
            trusted,
            render = { width, height -> asked += width to height; byteArrayOf(width.toByte(), height.toByte()) },
            teacherConnected = { teachers++ },
            stream = { streamed += it },
            lockScreen = { locks += it },
        )
    }

    private fun request(payload: ByteArray = ByteArray(0)) = Message(MessageType.REQUEST_THUMBNAIL, payload)

    @Test fun eachRequestGetsAFrameInTheAskedBox() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, request(byteArrayOf(0, 83, 0, 120)), request())

        handler.handle(channel)

        assertEquals(listOf(83 to 120, 320 to 180), asked)
        assertEquals(listOf(MessageType.THUMBNAIL_FRAME, MessageType.THUMBNAIL_FRAME), channel.sent.map { it.type })
        assertArrayEquals(byteArrayOf(83, 120), channel.sent[0].payload)
        assertEquals(15_000, channel.timeoutMillis)
    }

    @Test fun anUntrustedTeacherGetsNothing() {
        trusted.add(teacher)
        val channel = FakeChannel("B".repeat(40), request())
        handler.handle(channel)
        assertTrue(channel.sent.isEmpty())
        assertTrue(channel.events.isEmpty())
        assertTrue(asked.isEmpty())
        assertEquals(0, teachers)
    }

    @Test fun anythingElseEndsTheConnection() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, Message(MessageType.HEARTBEAT, ByteArray(0)), request())
        handler.handle(channel)
        assertTrue(channel.sent.isEmpty())
    }

    @Test fun aTrustedTeacherOnThisPortAsksToSeeTheScreen() {
        trusted.add(teacher)
        handler.handle(FakeChannel(teacher, request()))
        assertEquals(1, teachers)
    }

    @Test fun screenStreamGoesToTheStream() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, Message(MessageType.SCREEN_STREAM, ByteArray(0)))
        handler.handle(channel)
        assertEquals(listOf<Channel>(channel), streamed)
        assertTrue(asked.isEmpty())
        // The first message within the 10 s a connection gets to say what it is.
        assertEquals(listOf("receive ${MessageType.SCREEN_STREAM}"), channel.events)
        assertEquals(10_000, channel.timeoutMillis)
        assertEquals(1, teachers)
    }

    // The lock does not ask to see the screen: a request would only wait
    // under the padlock (the grid's thumbnails ask anyway).
    @Test fun lockScreenGoesToTheLock() {
        trusted.add(teacher)
        val channel = FakeChannel(teacher, Message(MessageType.LOCK_SCREEN, ByteArray(0)))
        handler.handle(channel)
        assertEquals(listOf<Channel>(channel), locks)
        assertTrue(channel.sent.isEmpty())
        assertTrue(streamed.isEmpty())
        assertEquals(0, teachers)
    }

    @Test fun anUntrustedTeacherCannotLock() {
        trusted.add(teacher)
        handler.handle(FakeChannel("B".repeat(40), Message(MessageType.LOCK_SCREEN, ByteArray(0))))
        assertTrue(locks.isEmpty())
    }

    @Test fun controlIsNotTakenOnAPhone() {
        trusted.add(teacher)
        for (type in listOf(
            MessageType.START_CONTROL, MessageType.STOP_CONTROL, MessageType.INPUT_EVENT,
            MessageType.CLIPBOARD_TEXT, MessageType.FILL_ADMIN_PASSWORD,
        )) {
            val channel = FakeChannel(teacher, Message(type, ByteArray(0)), request())
            handler.handle(channel)
            assertTrue("type $type", channel.sent.isEmpty())
        }
        assertTrue(streamed.isEmpty())
        assertTrue(locks.isEmpty())
        assertTrue(asked.isEmpty())
    }
}
