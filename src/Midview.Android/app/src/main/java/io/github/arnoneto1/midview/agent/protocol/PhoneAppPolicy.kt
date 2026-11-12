package io.github.arnoneto1.midview.agent.protocol

import java.io.ByteArrayOutputStream
import java.nio.ByteBuffer
import java.nio.charset.CharacterCodingException
import java.nio.charset.CodingErrorAction

// The modes of src/Midview.Protocol/PhoneAppPolicy.cs (AppBlockMode there).
enum class AppBlockMode(val code: Int) {
    // Nothing blocked: how a block is lifted.
    NONE(0),

    // Only the listed apps are blocked.
    BLOCK_LIST(1),

    // Everything is blocked except the essentials and the listed apps.
    ALLOW_ONLY(2),
    ;

    companion object {
        fun fromCode(code: Int): AppBlockMode? = entries.firstOrNull { it.code == code }
    }
}

// The app-blocking policy for the school's phones, byte for byte as
// src/Midview.Protocol/PhoneAppPolicy.cs (big-endian): format version (1),
// SavedAt (int64, Unix seconds on the teacher's PC), mode (1), count (u16),
// then each package as a length byte and its UTF-8 bytes. The newest
// SavedAt wins.
data class PhoneAppPolicy(val savedAt: Long, val mode: AppBlockMode, val packages: List<String>) {
    val isActive: Boolean get() = mode != AppBlockMode.NONE

    // Every name a valid package name, no duplicates, none for NONE.
    fun isValid(): Boolean {
        if (packages.size > MAX_PACKAGES || (mode == AppBlockMode.NONE && packages.isNotEmpty())) {
            return false
        }
        val seen = HashSet<String>()
        return packages.all { isValidPackageName(it) && seen.add(it) }
    }

    fun encode(): ByteArray {
        require(isValid()) { "invalid phone app policy" }
        val out = ByteArrayOutputStream()
        out.write(FORMAT_VERSION)
        out.write(ByteBuffer.allocate(8).putLong(savedAt).array())
        out.write(mode.code)
        out.write(packages.size ushr 8)
        out.write(packages.size and 0xFF)
        for (name in packages) {
            val bytes = name.toByteArray(Charsets.UTF_8)
            out.write(bytes.size)
            out.write(bytes)
        }
        return out.toByteArray()
    }

    // Same mode and the same packages, in any order.
    fun sameRulesAs(other: PhoneAppPolicy): Boolean =
        mode == other.mode && packages.size == other.packages.size && packages.toSet() == other.packages.toSet()

    companion object {
        const val FORMAT_VERSION = 1
        const val MAX_PACKAGES = 200

        // Bytes of UTF-8: the length is one byte on the wire.
        const val MAX_NAME_LENGTH = 255

        private val PACKAGE_PATTERN = Regex("[a-zA-Z][a-zA-Z0-9_]*(\\.[a-zA-Z0-9_]+)+")

        fun lifted(savedAt: Long) = PhoneAppPolicy(savedAt, AppBlockMode.NONE, emptyList())

        // matches() needs the whole string, so a trailing newline cannot pass.
        fun isValidPackageName(name: String?): Boolean =
            name != null && name.length <= MAX_NAME_LENGTH && PACKAGE_PATTERN.matches(name)

        // Null for anything that is not exactly a valid policy.
        fun decode(payload: ByteArray): PhoneAppPolicy? {
            if (payload.size < 12 || payload[0].toInt() != FORMAT_VERSION) {
                return null
            }
            val buffer = ByteBuffer.wrap(payload)
            val savedAt = buffer.getLong(1)
            val mode = AppBlockMode.fromCode(payload[9].toInt() and 0xFF) ?: return null
            val count = ((payload[10].toInt() and 0xFF) shl 8) or (payload[11].toInt() and 0xFF)
            if (count > MAX_PACKAGES) {
                return null
            }

            val packages = ArrayList<String>(count)
            var offset = 12
            repeat(count) {
                if (offset >= payload.size) {
                    return null
                }
                val length = payload[offset++].toInt() and 0xFF
                if (length == 0 || offset + length > payload.size) {
                    return null
                }
                packages.add(strictUtf8(payload, offset, length) ?: return null)
                offset += length
            }

            val decoded = PhoneAppPolicy(savedAt, mode, packages)
            return if (offset == payload.size && decoded.isValid()) decoded else null
        }
    }
}

// Null for bytes that are not well-formed UTF-8 (as .NET's throwing decoder).
internal fun strictUtf8(bytes: ByteArray, offset: Int, length: Int): String? =
    try {
        Charsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(bytes, offset, length))
            .toString()
    } catch (_: CharacterCodingException) {
        null
    }
