package io.github.arnoneto1.midview.agent.protocol

import java.io.ByteArrayOutputStream
import java.nio.ByteBuffer

// One file of the class folder as a listing shows it: lessonId equal to the
// listing's current lesson means the file is in the lesson's folder, any
// other that it was set aside for that lesson. path is '/'-separated, from
// that folder; lastWriteTicks is in .NET DateTime ticks (UTC).
data class ClassFolderEntry(val lessonId: LessonId, val path: String, val size: Long, val lastWriteTicks: Long)

// A file of a lesson, by path (PullClassFile, DeleteClassFile).
data class ClassFileRef(val lessonId: LessonId, val path: String)

// RenameClassFile.
data class ClassFileRename(val lessonId: LessonId, val path: String, val newPath: String)

// PushClassFile's header: where the file goes, how long it is, the time to
// give it, and what it may replace (null: nothing may be there; otherwise
// the size and ticks the file there must still have).
data class ClassPushHeader(
    val lessonId: LessonId,
    val path: String,
    val size: Long,
    val lastWriteTicks: Long,
    val replacing: Pair<Long, Long>?,
)

// The class folder's Session messages, as src/Midview.Protocol/
// ClassFolderWire.cs: big-endian, each starting with a format version byte;
// a string is a uint16 byte length, then strict UTF-8, a path at most
// FileTransfer.MAX_NAME_BYTES of it and never blank. Decoding refuses the
// whole payload for anything out of place (null).
object ClassFolderWire {
    const val FORMAT_VERSION: Byte = 1

    // Entries in one ClassFolderListing message.
    const val MAX_ENTRIES = 10_000

    private const val MAX_PROBLEM_BYTES = 0xFFFF

    // What a DateTime can hold.
    const val MAX_TICKS = 3_155_378_975_999_999_999L

    // The whole listing as the messages to send one after the other: at most
    // partSize entries each, every one but the last saying more follow; a
    // problem, or nothing at all, is one message.
    fun encodeListingParts(
        current: LessonId,
        problem: String?,
        entries: List<ClassFolderEntry>,
        partSize: Int = MAX_ENTRIES,
    ): List<ByteArray> {
        require(partSize in 1..MAX_ENTRIES)
        require(problem == null || entries.isEmpty()) { "A listing with a problem has no entries" }
        val parts = mutableListOf<ByteArray>()
        var start = 0
        do {
            val part = entries.subList(start, minOf(entries.size, start + partSize))
            start += part.size
            parts += encodeListing(current, problem, part, more = start < entries.size)
        } while (start < entries.size)
        return parts
    }

    fun encodeListing(current: LessonId, problem: String?, entries: List<ClassFolderEntry>, more: Boolean): ByteArray {
        val out = ByteArrayOutputStream()
        out.write(FORMAT_VERSION.toInt())
        out.write(current.bytes)
        if (problem == null) {
            out.write(0)
        } else {
            out.write(1)
            putText(out, problem, MAX_PROBLEM_BYTES)
        }
        out.write(if (more) 1 else 0)
        out.write(ByteBuffer.allocate(4).putInt(entries.size).array())
        for (entry in entries) {
            out.write(entry.lessonId.bytes)
            putPath(out, entry.path)
            out.write(ByteBuffer.allocate(16).putLong(checkSize(entry.size)).putLong(checkTicks(entry.lastWriteTicks)).array())
        }
        return out.toByteArray()
    }

    fun encodeFileRef(ref: ClassFileRef): ByteArray {
        val out = ByteArrayOutputStream()
        out.write(FORMAT_VERSION.toInt())
        out.write(ref.lessonId.bytes)
        putPath(out, ref.path)
        return out.toByteArray()
    }

    fun decodeFileRef(payload: ByteArray): ClassFileRef? {
        val reader = WireReader(payload)
        if (!reader.version()) return null
        val lesson = reader.lessonId() ?: return null
        val path = reader.path() ?: return null
        return if (reader.atEnd) ClassFileRef(lesson, path) else null
    }

    fun encodeRename(rename: ClassFileRename): ByteArray {
        val out = ByteArrayOutputStream()
        out.write(FORMAT_VERSION.toInt())
        out.write(rename.lessonId.bytes)
        putPath(out, rename.path)
        putPath(out, rename.newPath)
        return out.toByteArray()
    }

    fun decodeRename(payload: ByteArray): ClassFileRename? {
        val reader = WireReader(payload)
        if (!reader.version()) return null
        val lesson = reader.lessonId() ?: return null
        val path = reader.path() ?: return null
        val newPath = reader.path() ?: return null
        return if (reader.atEnd) ClassFileRename(lesson, path, newPath) else null
    }

    fun encodePushHeader(header: ClassPushHeader): ByteArray {
        val out = ByteArrayOutputStream()
        out.write(FORMAT_VERSION.toInt())
        out.write(header.lessonId.bytes)
        putPath(out, header.path)
        out.write(ByteBuffer.allocate(16).putLong(checkSize(header.size)).putLong(checkTicks(header.lastWriteTicks)).array())
        val replacing = header.replacing
        if (replacing == null) {
            out.write(0)
        } else {
            out.write(1)
            out.write(ByteBuffer.allocate(16).putLong(checkSize(replacing.first)).putLong(checkTicks(replacing.second)).array())
        }
        return out.toByteArray()
    }

    fun decodePushHeader(payload: ByteArray): ClassPushHeader? {
        val reader = WireReader(payload)
        if (!reader.version()) return null
        val lesson = reader.lessonId() ?: return null
        val path = reader.path() ?: return null
        val size = reader.int64()?.takeIf(::isSize) ?: return null
        val ticks = reader.int64()?.takeIf(::isTicks) ?: return null
        val replacing = when (reader.byte()) {
            0 -> null
            1 -> {
                val replacedSize = reader.int64()?.takeIf(::isSize) ?: return null
                val replacedTicks = reader.int64()?.takeIf(::isTicks) ?: return null
                replacedSize to replacedTicks
            }
            else -> return null
        }
        return if (reader.atEnd) ClassPushHeader(lesson, path, size, ticks, replacing) else null
    }

    fun encodeClearSetAside(lessonId: LessonId, files: List<ClassFolderEntry>): ByteArray {
        val out = ByteArrayOutputStream()
        out.write(FORMAT_VERSION.toInt())
        out.write(lessonId.bytes)
        out.write(ByteBuffer.allocate(4).putInt(files.size).array())
        for (file in files) {
            putPath(out, file.path)
            out.write(ByteBuffer.allocate(16).putLong(checkSize(file.size)).putLong(checkTicks(file.lastWriteTicks)).array())
        }
        return out.toByteArray()
    }

    // The lesson and the files the Master has dealt with, as its listing showed them.
    fun decodeClearSetAside(payload: ByteArray): Pair<LessonId, List<ClassFolderEntry>>? {
        val reader = WireReader(payload)
        if (!reader.version()) return null
        val lesson = reader.lessonId() ?: return null
        val count = reader.uint32()?.takeIf { it <= MAX_ENTRIES } ?: return null
        val files = ArrayList<ClassFolderEntry>(count.toInt())
        repeat(count.toInt()) {
            val path = reader.path() ?: return null
            val size = reader.int64()?.takeIf(::isSize) ?: return null
            val ticks = reader.int64()?.takeIf(::isTicks) ?: return null
            files += ClassFolderEntry(lesson, path, size, ticks)
        }
        return if (reader.atEnd) lesson to files else null
    }

    fun isSize(size: Long) = size in 0..FileTransfer.MAX_FILE_SIZE

    fun isTicks(ticks: Long) = ticks in 0..MAX_TICKS

    private fun checkSize(size: Long) = size.also { require(isSize(it)) { "A file size is 0 to ${FileTransfer.MAX_FILE_SIZE}" } }

    private fun checkTicks(ticks: Long) = ticks.also { require(isTicks(it)) { "A last write time is 0 to DateTime.MaxValue" } }

    private fun putPath(out: ByteArrayOutputStream, path: String) {
        require(path.isNotBlank()) { "A path can't be empty" }
        putText(out, path, FileTransfer.MAX_NAME_BYTES)
    }

    private fun putText(out: ByteArrayOutputStream, text: String, maxBytes: Int) {
        val bytes = text.toByteArray(Charsets.UTF_8)
        require(bytes.size <= maxBytes) { "A string is at most $maxBytes bytes of UTF-8" }
        out.write((bytes.size ushr 8) and 0xFF)
        out.write(bytes.size and 0xFF)
        out.write(bytes)
    }

    private class WireReader(private val bytes: ByteArray) {
        private var position = 0

        val atEnd: Boolean get() = position == bytes.size

        fun byte(): Int? = if (position < bytes.size) bytes[position++].toInt() and 0xFF else null

        fun version(): Boolean = byte() == FORMAT_VERSION.toInt()

        fun lessonId(): LessonId? {
            if (bytes.size - position < LessonId.SIZE) return null
            val id = LessonId(bytes.copyOfRange(position, position + LessonId.SIZE))
            position += LessonId.SIZE
            return id
        }

        fun uint32(): Long? {
            if (bytes.size - position < 4) return null
            val value = ByteBuffer.wrap(bytes, position, 4).getInt().toLong() and 0xFFFFFFFFL
            position += 4
            return value
        }

        fun int64(): Long? {
            if (bytes.size - position < 8) return null
            val value = ByteBuffer.wrap(bytes, position, 8).getLong()
            position += 8
            return value
        }

        fun path(): String? {
            if (bytes.size - position < 2) return null
            val length = ((bytes[position].toInt() and 0xFF) shl 8) or (bytes[position + 1].toInt() and 0xFF)
            if (length > FileTransfer.MAX_NAME_BYTES || bytes.size - position - 2 < length) return null
            val text = ClassLesson.strictUtf8(bytes, position + 2, length) ?: return null
            position += 2 + length
            return text.takeIf { it.isNotBlank() }
        }
    }
}
