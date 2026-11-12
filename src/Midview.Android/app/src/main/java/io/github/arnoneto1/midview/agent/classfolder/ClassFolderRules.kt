package io.github.arnoneto1.midview.agent.classfolder

import io.github.arnoneto1.midview.agent.files.FileNames
import io.github.arnoneto1.midview.agent.protocol.FileTransfer
import java.nio.CharBuffer
import java.nio.charset.CharacterCodingException
import java.nio.charset.CodingErrorAction
import java.util.UUID

// Which files of a class folder the sync looks at, as
// src/Midview.Protocol/ClassFolderRules.cs: the phone lists what a PC's
// Session would, so the teacher's Master never copies a file again and again
// because one side leaves it out. Android's own ".pending-" (a file still
// arriving) and ".trashed-" (in its trash) names are left out too; any other
// name starting with '.' is listed, as on a PC, or a ".gitignore" from a PC
// would be pushed again every round.
object ClassFolderRules {
    // Office's lock file for an open document.
    const val LOCK_FILE_PREFIX = "~$"

    // Midview's own files: the temp of a file being received, and the
    // folder in Download/Midview where a finished lesson's files wait.
    const val TEMP_PREFIX = ".midview-"
    const val TEMP_SUFFIX = ".parcial"

    // Still being written: Chrome's, an editor's, Midview's.
    val SKIPPED_SUFFIXES = listOf(".crdownload", ".tmp", ".parcial", ".part")

    fun newTempName(): String = TEMP_PREFIX + UUID.randomUUID().toString().replace("-", "") + TEMP_SUFFIX

    private val ANDROID_PREFIXES = listOf(".pending-", ".trashed-")

    fun isListedFile(name: String, size: Long): Boolean =
        ANDROID_PREFIXES.none { name.startsWith(it) } &&
            !name.startsWith(TEMP_PREFIX, ignoreCase = true) &&
            !name.startsWith(LOCK_FILE_PREFIX) &&
            SKIPPED_SUFFIXES.none { name.endsWith(it, ignoreCase = true) } &&
            size <= FileTransfer.MAX_FILE_SIZE &&
            isAddressable(name)

    // A name SafeRelativePath would leave as it is, so it can be found again.
    fun isAddressable(name: String): Boolean = FileNames.safe(name) == name

    // '/'-separated, not blank, at most FileTransfer.MAX_NAME_BYTES of UTF-8
    // (a name with half a surrogate pair is no path at all).
    fun fitsTheWire(path: String): Boolean {
        if (path.isBlank()) return false
        return try {
            Charsets.UTF_8.newEncoder()
                .onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT)
                .encode(CharBuffer.wrap(path))
                .remaining() <= FileTransfer.MAX_NAME_BYTES
        } catch (e: CharacterCodingException) {
            false
        }
    }

    fun fitsTheDepth(segments: Int): Boolean = segments <= FileTransfer.MAX_DEPTH

    // The class folder itself is depth 1.
    fun canDescendFrom(depth: Int): Boolean = fitsTheDepth(depth + 1)

    // The path the teacher sent, as safe segments (FileCollection.SafeRelativePath):
    // "." and ".." dropped, every segment through FileNames.safe, the last
    // MAX_DEPTH of them; "arquivo" when nothing is left.
    fun safeRelativePath(requested: String): List<String> {
        val segments = requested.split('/', '\\')
            .map { it.trim() }
            .filter { it.isNotEmpty() && it != "." && it != ".." }
            .map(FileNames::safe)
        if (segments.isEmpty()) return listOf("arquivo")
        return segments.drop(maxOf(0, segments.size - FileTransfer.MAX_DEPTH))
    }

    // "images (2).jpg" for the second file of that name (ClassFolderRules.CopyName).
    fun copyName(name: String, copy: Int): String {
        val extension = FileNames.extensionOf(name).let { if (it.length == name.length) "" else it }
        val stem = name.substring(0, name.length - extension.length)
        return "$stem ($copy)$extension"
    }

    // .NET DateTime ticks (100 ns since 0001-01-01, UTC) and Unix milliseconds.
    private const val UNIX_EPOCH_TICKS = 621_355_968_000_000_000L

    fun ticksOf(unixMillis: Long): Long = maxOf(0L, unixMillis) * 10_000 + UNIX_EPOCH_TICKS

    // Never before 1970: Android can't give a file an earlier time
    // (File.setLastModified throws), and the wire takes any time from year 1.
    fun millisOf(ticks: Long): Long = maxOf(0L, (ticks - UNIX_EPOCH_TICKS) / 10_000)

    // What may be deleted for good when a lesson's folder goes: only what
    // is never anybody's work (a file Android or Chrome is still writing,
    // Midview's own temp). Anything else, listed or not, is the student's.
    fun isJunk(name: String): Boolean =
        name.startsWith(".pending-") ||
            name.endsWith(".crdownload", ignoreCase = true) ||
            (name.startsWith(TEMP_PREFIX, ignoreCase = true) && name.endsWith(TEMP_SUFFIX, ignoreCase = true))
}
