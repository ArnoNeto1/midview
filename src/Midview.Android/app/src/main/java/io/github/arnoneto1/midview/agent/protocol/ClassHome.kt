package io.github.arnoneto1.midview.agent.protocol

import java.io.ByteArrayOutputStream
import java.nio.ByteBuffer

// Which teacher PC keeps the class folders when a lab has two
// (src/Midview.Protocol/ClassHome.cs): its certificate thumbprint and
// computer name, where its Master listens (address, port) and its
// generation (when it became the home, in UTC ticks on its own clock; 0 for
// a home recorded from a lesson start with no claim).
//
// The payload of ClassHomeStatus (big-endian): format version (1 byte),
// thumbprint, name and address (each a uint16 byte length, then UTF-8), port
// (uint16), generation (int64). An empty payload is "no home".
data class ClassHome(val thumbprint: String, val name: String, val address: String, val port: Int, val generation: Long) {
    // The thumbprint and the name, each ignoring case; a home with no name is
    // any PC with that thumbprint.
    fun isSameIdentity(thumbprint: String, name: String): Boolean =
        this.thumbprint.equals(thumbprint, ignoreCase = true) && (this.name.isEmpty() || this.name.equals(name, ignoreCase = true))

    // Its identity; or its certificate and generation under a new name (the
    // PC renamed); or its name under another certificate (Midview installed
    // again, another Windows account).
    fun isSamePc(thumbprint: String, name: String, generation: Long?): Boolean =
        isSameIdentity(thumbprint, name) ||
            (generation == this.generation && this.thumbprint.equals(thumbprint, ignoreCase = true)) ||
            (this.name.isNotEmpty() && this.name.equals(name, ignoreCase = true))

    // What a claim or a start from another PC is answered.
    val refusalText: String get() = OTHER_HOME_TEXT + name.ifEmpty { UNNAMED_TEXT }

    val isWellFormed: Boolean
        get() = isThumbprint(thumbprint) && name.length <= MAX_NAME_LENGTH &&
            address.length in 1..MAX_ADDRESS_LENGTH && isIpLiteral(address) && port in 1..0xFFFF && generation >= 0

    fun encode(): ByteArray {
        check(isWellFormed) { "The class home can't be sent" }
        val out = ByteArrayOutputStream()
        out.write(FORMAT_VERSION.toInt())
        putText(out, thumbprint)
        putText(out, name)
        putText(out, address)
        putUInt16(out, port)
        out.write(ByteBuffer.allocate(8).putLong(generation).array())
        return out.toByteArray()
    }

    companion object {
        const val FORMAT_VERSION: Byte = 2
        const val MAX_NAME_LENGTH = 64
        const val MAX_ADDRESS_LENGTH = 64
        const val UNNAMED_TEXT = "de outro professor"

        // DateTime.MaxValue.Ticks: no claim goes past it.
        const val MAX_GENERATION = 3_155_378_975_999_999_999L

        // How a refusal starts when another PC is the home (the Master looks for it).
        const val OTHER_HOME_TEXT = "As pastas das turmas ficam no PC "

        // A claim or a lesson that couldn't be looked at in time: nothing taken.
        const val BUSY_TEXT = "ocupado agora, tente de novo em instantes"

        fun encode(home: ClassHome?): ByteArray = home?.encode() ?: ByteArray(0)

        // Empty: no home (Pair.first true, second null). Null: not a ClassHome.
        fun decode(payload: ByteArray): Pair<Boolean, ClassHome?> {
            if (payload.isEmpty()) return true to null
            val reader = Reader(payload)
            if (reader.byte() != FORMAT_VERSION.toInt()) return false to null
            val thumbprint = reader.text() ?: return false to null
            val name = reader.text() ?: return false to null
            val address = reader.text() ?: return false to null
            val port = reader.uint16() ?: return false to null
            val generation = reader.int64() ?: return false to null
            if (!reader.atEnd) return false to null
            val home = ClassHome(thumbprint, name, address, port, generation)
            return if (home.isWellFormed) true to home else false to null
        }

        fun isThumbprint(value: String): Boolean = value.length == 40 && value.all { it in '0'..'9' || it in 'a'..'f' || it in 'A'..'F' }

        // An IP address as text (never a host name to look up): what a
        // connection's address is, and all a home's address may be.
        fun isIpLiteral(text: String): Boolean {
            val parts = text.split('.')
            if (parts.size == 4 && parts.all { part -> part.length in 1..3 && part.all { it.isDigit() } && part.toInt() <= 255 }) {
                return true
            }
            val scopeless = text.substringBefore('%')
            return ':' in scopeless && scopeless.all { it.isDigit() || it in 'a'..'f' || it in 'A'..'F' || it == ':' || it == '.' }
        }

        internal fun putText(out: ByteArrayOutputStream, text: String) {
            val bytes = text.toByteArray(Charsets.UTF_8)
            putUInt16(out, bytes.size)
            out.write(bytes)
        }

        internal fun putUInt16(out: ByteArrayOutputStream, value: Int) {
            out.write((value ushr 8) and 0xFF)
            out.write(value and 0xFF)
        }
    }
}

// What a teacher PC sends before it starts a lesson: "I am the home"
// (src/Midview.Protocol/ClassHome.cs, ClassHomeClaim). The thumbprint comes
// from the TLS channel and the address from the connection, never from it.
//
// The payload of ClaimClassHome (big-endian): format version (1 byte), flags
// (1 byte, bit 0 takeOver, no other bit), port (uint16), generation (int64),
// name (uint16 byte length, then UTF-8).
data class ClassHomeClaim(val name: String, val port: Int, val takeOver: Boolean, val generation: Long) {
    fun encode(): ByteArray {
        val out = ByteArrayOutputStream()
        out.write(ClassHome.FORMAT_VERSION.toInt())
        out.write(if (takeOver) 1 else 0)
        ClassHome.putUInt16(out, port)
        out.write(ByteBuffer.allocate(8).putLong(generation).array())
        ClassHome.putText(out, name)
        return out.toByteArray()
    }

    companion object {
        fun decode(payload: ByteArray): ClassHomeClaim? {
            if (payload.size < 2 || payload[0] != ClassHome.FORMAT_VERSION || payload[1].toInt() !in 0..1) return null
            val reader = Reader(payload, 2)
            val port = reader.uint16() ?: return null
            val generation = reader.int64() ?: return null
            val name = reader.text() ?: return null
            if (!reader.atEnd) return null
            val claim = ClassHomeClaim(name, port, payload[1].toInt() == 1, generation)
            return claim.takeIf { name.length <= ClassHome.MAX_NAME_LENGTH && port in 1..0xFFFF && generation > 0 }
        }
    }
}

// Reading ClassHome and ClassHomeClaim from the front: null when what is
// left is too short or not valid.
private class Reader(private val bytes: ByteArray, private var position: Int = 0) {
    val atEnd: Boolean get() = position == bytes.size

    fun byte(): Int? = if (position < bytes.size) bytes[position++].toInt() and 0xFF else null

    fun uint16(): Int? {
        if (bytes.size - position < 2) return null
        val value = ((bytes[position].toInt() and 0xFF) shl 8) or (bytes[position + 1].toInt() and 0xFF)
        position += 2
        return value
    }

    fun int64(): Long? {
        if (bytes.size - position < 8) return null
        val value = ByteBuffer.wrap(bytes, position, 8).getLong()
        position += 8
        return value
    }

    fun text(): String? {
        val length = uint16() ?: return null
        if (bytes.size - position < length) return null
        val text = ClassLesson.strictUtf8(bytes, position, length) ?: return null
        position += length
        return text
    }
}
