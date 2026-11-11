package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.EOFException
import java.io.IOException

class MessageFramerTest {
    // Records each write call, to check a message goes out in one.
    private class RecordingStream : ByteArrayOutputStream() {
        val writes = mutableListOf<ByteArray>()

        override fun write(b: ByteArray, off: Int, len: Int) {
            writes += b.copyOfRange(off, off + len)
            super.write(b, off, len)
        }

        override fun write(b: Int) {
            writes += byteArrayOf(b.toByte())
            super.write(b)
        }
    }

    private fun frame(vararg bytes: Int) = ByteArray(bytes.size) { bytes[it].toByte() }

    @Test fun framerWritesLengthTypePayloadInOneFrame() {
        val out = RecordingStream()
        MessageFramer.write(out, Message(11, byteArrayOf(1, 2)))
        assertEquals(1, out.writes.size)
        assertArrayEquals(frame(0, 0, 0, 3, 11, 1, 2), out.writes[0])
    }

    @Test fun anEmptyPayloadIsStillOneFrame() {
        val out = RecordingStream()
        MessageFramer.write(out, Message(MessageType.HEARTBEAT, ByteArray(0)))
        assertEquals(1, out.writes.size)
        assertArrayEquals(frame(0, 0, 0, 1, 2), out.writes[0])
    }

    @Test fun readGetsBackWhatWasWritten() {
        val out = ByteArrayOutputStream()
        MessageFramer.write(out, Message(MessageType.PAIR_COMMIT, ByteArray(32) { it.toByte() }))
        MessageFramer.write(out, Message(MessageType.HEARTBEAT, ByteArray(0)))
        val input = ByteArrayInputStream(out.toByteArray())
        assertEquals(Message(MessageType.PAIR_COMMIT, ByteArray(32) { it.toByte() }), MessageFramer.read(input))
        assertEquals(Message(MessageType.HEARTBEAT, ByteArray(0)), MessageFramer.read(input))
    }

    @Test fun aTypeAbove127IsReadAsUnsigned() {
        assertEquals(200, MessageFramer.read(ByteArrayInputStream(frame(0, 0, 0, 1, 200))).type)
    }

    @Test fun aTypeThatDoesNotFitInAByteIsRefused() {
        assertThrows(IllegalArgumentException::class.java) { Message(256, ByteArray(0)) }
        assertThrows(IllegalArgumentException::class.java) { Message(-1, ByteArray(0)) }
    }

    @Test fun framerRefusesZeroAndOversizedLength() {
        assertEquals(16 * 1024 * 1024, MessageFramer.MAX)
        assertThrows(IOException::class.java) { MessageFramer.read(ByteArrayInputStream(frame(0, 0, 0, 0))) }
        assertThrows(IOException::class.java) { MessageFramer.read(ByteArrayInputStream(frame(0xFF, 0xFF, 0xFF, 0xFF))) }
        // 16 MiB + 1: the type and a payload of exactly 16 MiB.
        assertThrows(IOException::class.java) { MessageFramer.read(ByteArrayInputStream(frame(1, 0, 0, 1, 2))) }
        // A larger limit never lifts the 16 MiB one.
        assertThrows(IOException::class.java) {
            MessageFramer.read(ByteArrayInputStream(frame(1, 0, 0, 1, 2)), Int.MAX_VALUE)
        }
    }

    @Test fun aSmallerLimitIsHonoured() {
        val out = ByteArrayOutputStream()
        MessageFramer.write(out, Message(MessageType.PAIR_REQUEST, ByteArray(10)))
        assertThrows(IOException::class.java) { MessageFramer.read(ByteArrayInputStream(out.toByteArray()), 10) }
        assertEquals(10, MessageFramer.read(ByteArrayInputStream(out.toByteArray()), 11).payload.size)
    }

    @Test fun aClosedConnectionMidMessageIsEndOfStream() {
        assertThrows(EOFException::class.java) { MessageFramer.read(ByteArrayInputStream(frame(0, 0))) }
        assertThrows(EOFException::class.java) { MessageFramer.read(ByteArrayInputStream(frame(0, 0, 0, 3, 11, 1))) }
    }

    @Test fun messageTypesMatchDotNet() {
        assertEquals(2, MessageType.HEARTBEAT)
        assertEquals(11, MessageType.COMMAND_ACK)
        assertEquals(12, MessageType.REQUEST_THUMBNAIL)
        assertEquals(13, MessageType.THUMBNAIL_FRAME)
        assertEquals(37, MessageType.PAIR_REQUEST)
        assertEquals(38, MessageType.PAIR_COMMIT)
        assertEquals(39, MessageType.PAIR_NONCE)
        assertEquals(40, MessageType.PAIR_REVEAL)
        assertEquals(41, MessageType.PAIR_CONFIRM)
        assertEquals(42, MessageType.PAIR_CANCEL)
        assertEquals(43, MessageType.PAIR_DONE)
        assertEquals(44, MessageType.PAIR_REFUSED)
        assertEquals(45, MessageType.ALLOW_NEW_TEACHER)
    }
}
