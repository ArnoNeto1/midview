package io.github.arnoneto1.midview.agent.protocol

import java.io.EOFException
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream

// One message: its type (a MessageType number, one byte on the wire) and its
// payload. Equal when the type and the payload's bytes are.
data class Message(val type: Int, val payload: ByteArray) {
    init {
        require(type in 0..255) { "Message type $type does not fit in one byte" }
    }

    override fun equals(other: Any?) =
        other is Message && other.type == type && other.payload.contentEquals(payload)

    override fun hashCode() = 31 * type + payload.contentHashCode()

    override fun toString() = "Message(type=$type, payload=${payload.size} bytes)"
}

// The frame of src/Midview.Protocol/MessageFramer.cs: a big-endian int32 with
// the length of the type and the payload, one byte of type, the payload.
object MessageFramer {
    // Far above anything real; stops a bad or hostile length prefix from
    // making read allocate up to 2 GB.
    const val MAX = 16 * 1024 * 1024

    // One write per message, as on Windows: a message with an empty payload
    // must not end in a separate zero-length write, which TLS can send as an
    // empty record the other side never consumes.
    fun write(out: OutputStream, m: Message) {
        val length = m.payload.size + 1
        val frame = ByteArray(4 + length)
        frame[0] = (length ushr 24).toByte()
        frame[1] = (length ushr 16).toByte()
        frame[2] = (length ushr 8).toByte()
        frame[3] = length.toByte()
        frame[4] = m.type.toByte()
        m.payload.copyInto(frame, 5)
        out.write(frame, 0, frame.size)
        out.flush()
    }

    // For a side that doesn't trust the other yet: a message whose type and
    // payload are longer than maxLength is refused before it is read. The
    // MAX limit always holds.
    fun read(input: InputStream, maxLength: Int = MAX): Message {
        val header = readExact(input, 4)
        val length = ((header[0].toInt() and 0xFF) shl 24) or
            ((header[1].toInt() and 0xFF) shl 16) or
            ((header[2].toInt() and 0xFF) shl 8) or
            (header[3].toInt() and 0xFF)
        if (length < 1 || length > minOf(maxLength, MAX)) {
            throw IOException("Invalid message length $length")
        }

        val typeAndPayload = readExact(input, length)
        return Message(typeAndPayload[0].toInt() and 0xFF, typeAndPayload.copyOfRange(1, length))
    }

    private fun readExact(input: InputStream, count: Int): ByteArray {
        val buffer = ByteArray(count)
        var offset = 0
        while (offset < count) {
            val read = input.read(buffer, offset, count - offset)
            if (read <= 0) {
                throw EOFException("Connection closed while reading message")
            }
            offset += read
        }
        return buffer
    }
}
