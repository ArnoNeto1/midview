package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import io.github.arnoneto1.midview.agent.screen.CaptureCadence
import io.github.arnoneto1.midview.agent.screen.Frame
import io.github.arnoneto1.midview.agent.screen.FrameCredit
import io.github.arnoneto1.midview.agent.screen.FrameSource
import io.github.arnoneto1.midview.agent.screen.LatestFrame
import io.github.arnoneto1.midview.agent.screen.Rect
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.EOFException
import java.io.IOException
import java.net.SocketTimeoutException
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger
import kotlin.concurrent.thread

class ScreenStreamHandlerTest {
    // A teacher's connection that stays open: the test delivers messages
    // when it wants, a receive waits up to the timeout set (as a socket
    // does), and what the agent sends can be waited for.
    private class LiveChannel : Channel {
        private object End

        private val incoming = LinkedBlockingQueue<Any>()
        val sent = LinkedBlockingQueue<Message>()
        override val remoteThumbprint = "A".repeat(40)

        @Volatile var timeoutMillis = 0
            private set
        @Volatile var closed = false
            private set

        fun deliver(message: Message) = incoming.put(message)

        fun hangUp() = incoming.put(End)

        fun next(waitMillis: Long = 2_000): Message? = sent.poll(waitMillis, TimeUnit.MILLISECONDS)

        override fun receive(maxLength: Int): Message {
            if (closed) throw IOException("closed")
            val next = if (timeoutMillis == 0) incoming.take() else incoming.poll(timeoutMillis.toLong(), TimeUnit.MILLISECONDS)
            if (next == null) throw SocketTimeoutException("silence")
            if (next === End) throw EOFException()
            return next as Message
        }

        override fun send(message: Message) {
            if (closed) throw IOException("closed")
            sent.put(message)
        }

        override fun setTimeout(millis: Int) {
            timeoutMillis = millis
        }

        override fun close() {
            closed = true
            incoming.put(End)
        }
    }

    private data class Decoded(val id: Int, val width: Int, val height: Int, val regions: List<Rect>)

    // ScreenFrame's layout (little-endian int32s), the fake JPEG being one byte: its quality.
    private fun decode(message: Message?): Decoded {
        assertNotNull("no ScreenFrame", message)
        assertEquals(MessageType.SCREEN_FRAME, message!!.type)
        val buffer = ByteBuffer.wrap(message.payload).order(ByteOrder.LITTLE_ENDIAN)
        val id = buffer.int
        val width = buffer.int
        val height = buffer.int
        val regions = (0 until buffer.int).map {
            val rect = Rect(buffer.int, buffer.int, buffer.int, buffer.int)
            val jpeg = ByteArray(buffer.int).also { buffer.get(it) }
            assertEquals(90, jpeg.single().toInt())
            rect
        }
        assertFalse(buffer.hasRemaining())
        return Decoded(id, width, height, regions)
    }

    private fun ack(id: Int) = Message(
        MessageType.FRAME_ACK,
        ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN).putInt(id).array(),
    )

    private fun message(type: Int) = Message(type, ByteArray(0))

    private val channel = LiveChannel()
    private val frames = LatestFrame()
    private val running = mutableListOf<Thread>()

    private fun start(
        source: FrameSource = frames,
        silenceMillis: Int = ScreenStreamHandler.SILENCE_MILLIS,
    ): Thread {
        val handler = ScreenStreamHandler(
            channel, source, FrameCredit(), CaptureCadence(),
            encoder = { _, _, quality -> byteArrayOf(quality.toByte()) },
            silenceMillis = silenceMillis,
        )
        return thread { handler.run() }.also { running += it }
    }

    private fun assertEnds(stream: Thread) {
        stream.join(3_000)
        assertFalse("the stream is still running", stream.isAlive)
        assertTrue("the connection was not closed", channel.closed)
    }

    @After fun stopStreams() {
        channel.hangUp()
        running.forEach { it.join(3_000) }
    }

    @Test fun streamStartsWithFullScreenThenOnlyChanges() {
        frames.publish(IntArray(128 * 64), 128, 64)
        start()
        assertEquals(Decoded(1, 128, 64, listOf(Rect(0, 0, 128, 64))), decode(channel.next()))
        channel.deliver(ack(1))

        val changed = IntArray(128 * 64).also { it[2 * 128 + 70] = 0xFFFFFFFF.toInt() }
        frames.publish(changed, 128, 64)
        assertEquals(Decoded(2, 128, 64, listOf(Rect(64, 0, 64, 64))), decode(channel.next()))
        channel.deliver(ack(2))

        // A new picture with the same pixels: nothing to send.
        frames.publish(changed.copyOf(), 128, 64)
        assertNull(channel.next(300))
    }

    @Test fun rotationSendsFullScreenWithNewSize() {
        frames.publish(IntArray(128 * 64), 128, 64)
        start()
        decode(channel.next())
        channel.deliver(ack(1))

        frames.publish(IntArray(64 * 128), 64, 128)
        assertEquals(Decoded(2, 64, 128, listOf(Rect(0, 0, 64, 128))), decode(channel.next()))
    }

    @Test fun twoUnackedFramesPauseCapture() {
        val captures = AtomicInteger()
        val counted = object : FrameSource {
            override fun latest() = frames.latest()
            override fun awaitNew(afterSeq: Long, timeoutMs: Long): Frame? {
                captures.incrementAndGet()
                return frames.awaitNew(afterSeq, timeoutMs)
            }
        }
        frames.publish(IntArray(64 * 64) { 1 }, 64, 64)
        start(counted)
        assertEquals(1, decode(channel.next()).id)
        frames.publish(IntArray(64 * 64) { 2 }, 64, 64)
        assertEquals(2, decode(channel.next()).id)

        // Two on their way, none acknowledged: no capture until there is room.
        frames.publish(IntArray(64 * 64) { 3 }, 64, 64)
        Thread.sleep(100)
        val before = captures.get()
        assertNull(channel.next(400))
        assertEquals(before, captures.get())

        channel.deliver(ack(2))
        assertEquals(3, decode(channel.next()).id)
    }

    @Test fun noPictureYetSendsNothingAndKeepsTheStream() {
        val stream = start()
        assertNull(channel.next(700))
        assertTrue(stream.isAlive)
        frames.publish(IntArray(64 * 64), 64, 64)
        assertEquals(1, decode(channel.next()).id)
    }

    @Test fun stopControlAndSilenceEndTheStream() {
        frames.publish(IntArray(64 * 64), 64, 64)
        val stopped = start()
        decode(channel.next())
        assertEquals(15_000, channel.timeoutMillis)
        channel.deliver(message(MessageType.HEARTBEAT))
        channel.deliver(message(MessageType.STOP_CONTROL))
        assertEnds(stopped)
    }

    @Test fun silenceEndsTheStreamAndHeartbeatsKeepIt() {
        val silent = start(silenceMillis = 300)
        repeat(6) {
            Thread.sleep(100)
            channel.deliver(message(MessageType.HEARTBEAT))
        }
        assertTrue("heartbeats keep the stream", silent.isAlive)
        assertEnds(silent)
    }

    @Test fun theConnectionEndingEndsTheStream() {
        val stream = start()
        channel.hangUp()
        assertEnds(stream)
    }

    @Test fun controlMessagesCloseTheConnection() {
        for (type in listOf(MessageType.START_CONTROL, MessageType.INPUT_EVENT, MessageType.FILL_ADMIN_PASSWORD)) {
            val channel = LiveChannel()
            val handler = ScreenStreamHandler(
                channel, frames, FrameCredit(), CaptureCadence(), encoder = { _, _, _ -> ByteArray(1) },
            )
            val stream = thread { handler.run() }
            channel.deliver(Message(type, byteArrayOf(1, 2, 3)))
            stream.join(3_000)
            assertFalse("type $type", stream.isAlive)
            assertTrue("type $type", channel.closed)
        }
    }

    @Test fun clipboardTextMidStreamKeepsFramesFlowing() {
        frames.publish(IntArray(64 * 64) { 1 }, 64, 64)
        val stream = start()
        assertEquals(1, decode(channel.next()).id)

        // The teacher copied something while watching: dropped, even a long one.
        channel.deliver(Message(MessageType.CLIPBOARD_TEXT, ByteArray(200_000) { 'a'.code.toByte() }))
        channel.deliver(ack(1))
        frames.publish(IntArray(64 * 64) { 2 }, 64, 64)
        assertEquals(2, decode(channel.next()).id)
        assertTrue(stream.isAlive)
        assertFalse(channel.closed)
    }
}
