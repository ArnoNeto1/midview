package io.github.arnoneto1.midview.agent.protocol

import java.nio.ByteBuffer
import java.nio.charset.CharacterCodingException
import java.nio.charset.CodingErrorAction
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

// A lesson's id: the 16 bytes of a .NET Guid as Guid.ToByteArray writes them,
// kept as they came (the phone never needs the Guid's text form). All zeros
// is "no lesson" (Guid.Empty).
class LessonId(bytes: ByteArray) {
    private val value = bytes.copyOf()

    init {
        require(value.size == SIZE) { "A lesson id is $SIZE bytes, not ${value.size}" }
    }

    val bytes: ByteArray get() = value.copyOf()

    val isEmpty: Boolean get() = value.all { it.toInt() == 0 }

    // Lower-case hex of the bytes, for a folder or a file key on the phone.
    val hex: String get() = value.joinToString("") { HEX[(it.toInt() shr 4) and 0xF].toString() + HEX[it.toInt() and 0xF] }

    override fun equals(other: Any?) = other is LessonId && other.value.contentEquals(value)

    override fun hashCode() = value.contentHashCode()

    override fun toString() = hex

    companion object {
        const val SIZE = 16
        private const val HEX = "0123456789abcdef"

        val EMPTY = LessonId(ByteArray(SIZE))

        // What hex wrote; null for anything else.
        fun fromHex(text: String): LessonId? {
            if (text.length != SIZE * 2 || text.any { it !in HEX }) return null
            return LessonId(ByteArray(SIZE) { text.substring(it * 2, it * 2 + 2).toInt(16).toByte() })
        }
    }
}

// The class folder's lesson ("Pasta da turma"), as src/Midview.Protocol/
// ClassLesson.cs: which turma the teacher is teaching and since when, or that
// the lesson has ended. savedAt (Unix seconds on the teacher's PC) decides
// between two versions, the newest winning; startedAt (Unix seconds too) lets
// the phone end by itself a lesson that began on an earlier day.
//
// The payload of SetClassLesson and ClassLessonStatus (big-endian): format
// version (1 byte), savedAt (int64), startedAt (int64), ended (1 byte, 0 or
// 1), the lesson id (16 bytes), the turma (uint16 byte length, then UTF-8).
data class ClassLesson(val savedAt: Long, val lessonId: LessonId, val turma: String, val startedAt: Long, val ended: Boolean) {
    val isOn: Boolean get() = !ended && !lessonId.isEmpty

    // The folder's name, in Download/Midview on a phone (on a PC's Desktop).
    val folderName: String get() = FOLDER_PREFIX + turma

    // Did it start on a calendar day (in zone) before today? Then it is left
    // over from an earlier class.
    fun startedBefore(today: LocalDate, zone: ZoneId = ZoneId.systemDefault()): Boolean =
        Instant.ofEpochSecond(startedAt).atZone(zone).toLocalDate().isBefore(today)

    val isWellFormed: Boolean
        get() = turma.length <= MAX_TURMA_LENGTH && startedAt in 0..MAX_STARTED_AT

    fun encode(): ByteArray {
        check(isWellFormed) { "The class lesson can't be sent" }
        val text = turma.toByteArray(Charsets.UTF_8)
        val buffer = ByteBuffer.allocate(FIXED_LENGTH + text.size)
        buffer.put(FORMAT_VERSION)
        buffer.putLong(savedAt)
        buffer.putLong(startedAt)
        buffer.put(if (ended) 1 else 0)
        buffer.put(lessonId.bytes)
        buffer.putShort(text.size.toShort())
        buffer.put(text)
        return buffer.array()
    }

    companion object {
        const val FORMAT_VERSION: Byte = 1

        // In characters: what fits in a folder name next to FOLDER_PREFIX.
        const val MAX_TURMA_LENGTH = 60

        // How the answer to SetClassLesson starts when the phone keeps a
        // newer lesson than the one sent (then ": " and that lesson).
        const val KEPT_NEWER_TEXT = "mantida a aula mais nova"

        // U+2013 (en dash), not a hyphen.
        const val FOLDER_PREFIX = "Pasta da turma – "

        // DateTimeOffset.MaxValue in Unix seconds.
        const val MAX_STARTED_AT = 253_402_300_799L

        private const val FIXED_LENGTH = 36

        fun none(savedAt: Long) = ClassLesson(savedAt, LessonId.EMPTY, "", 0, true)

        // Null for anything ClassLesson.TryDecode refuses.
        fun decode(payload: ByteArray): ClassLesson? {
            if (payload.size < FIXED_LENGTH || payload[0] != FORMAT_VERSION || payload[17].toInt() !in 0..1) return null
            val buffer = ByteBuffer.wrap(payload)
            buffer.get()
            val savedAt = buffer.getLong()
            val startedAt = buffer.getLong()
            val ended = buffer.get().toInt() == 1
            val id = ByteArray(LessonId.SIZE).also { buffer.get(it) }
            val length = buffer.getShort().toInt() and 0xFFFF
            if (payload.size != FIXED_LENGTH + length) return null
            val turma = strictUtf8(payload, FIXED_LENGTH, length) ?: return null
            return ClassLesson(savedAt, LessonId(id), turma, startedAt, ended).takeIf { it.isWellFormed }
        }

        // Strict UTF-8, as .NET's throwOnInvalidBytes: null when not valid.
        fun strictUtf8(bytes: ByteArray, offset: Int, length: Int): String? = try {
            Charsets.UTF_8.newDecoder()
                .onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT)
                .decode(ByteBuffer.wrap(bytes, offset, length))
                .toString()
        } catch (e: CharacterCodingException) {
            null
        }
    }
}
