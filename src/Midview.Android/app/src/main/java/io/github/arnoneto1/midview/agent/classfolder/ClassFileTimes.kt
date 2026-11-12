package io.github.arnoneto1.midview.agent.classfolder

import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.LessonId
import java.io.File
import java.io.IOException

// The time the teacher's PC gave each file it pushed, as the phone keeps it.
// A PC's file system stores the time to the tick (100 ns); a phone's to the
// millisecond at best, and may not take it at all. The Master tells a
// student's edit from its own copy by size and time, so a phone that listed
// its own, rounded time would look edited every round and its "edit" would be
// copied back. So for each pushed file the phone remembers the ticks it was
// sent and what the file looked like once written (size, last-modified
// millis), and lists those ticks for as long as the file still looks so.
// Any change (the student saved it) shows the phone's own time.
//
// Kept in the app's storage, one line per file:
// lesson (hex) TAB size TAB millis TAB ticks TAB path ('/'-separated, as
// listed). A path never holds a tab or a line break (FileNames.safe).
class ClassFileTimes(private val file: File, private val log: (String, Throwable?) -> Unit = { _, _ -> }) {
    private data class Key(val lesson: LessonId, val path: String)

    private data class Seen(val size: Long, val millis: Long, val ticks: Long)

    private val gate = Any()
    private var records: MutableMap<Key, Seen>? = null

    // The ticks to list for that file: the teacher's, if it is still as written.
    fun ticksOf(lesson: LessonId, path: String, size: Long, millis: Long): Long = synchronized(gate) {
        val seen = loaded()[Key(lesson, path.lowercase())]
        if (seen != null && seen.size == size && seen.millis == millis) seen.ticks else ClassFolderRules.ticksOf(millis)
    }

    fun remember(lesson: LessonId, path: String, size: Long, millis: Long, ticks: Long) = synchronized(gate) {
        loaded()[Key(lesson, path.lowercase())] = Seen(size, millis, ticks)
        save()
    }

    fun move(lesson: LessonId, path: String, toLesson: LessonId, newPath: String) = synchronized(gate) {
        val records = loaded()
        val seen = records.remove(Key(lesson, path.lowercase())) ?: return@synchronized
        records[Key(toLesson, newPath.lowercase())] = seen
        save()
    }

    fun forget(lesson: LessonId, path: String) = synchronized(gate) {
        if (loaded().remove(Key(lesson, path.lowercase())) != null) save()
    }

    // Only what is still there (each lesson's listed paths): the rest goes.
    fun keepOnly(listed: Set<Pair<LessonId, String>>) = synchronized(gate) {
        val wanted = listed.map { Key(it.first, it.second.lowercase()) }.toSet()
        val records = loaded()
        if (records.keys.retainAll(wanted)) save()
    }

    private fun loaded(): MutableMap<Key, Seen> = records ?: read().also { records = it }

    private fun read(): MutableMap<Key, Seen> {
        val map = mutableMapOf<Key, Seen>()
        if (!file.exists()) return map
        try {
            for (line in file.readLines(Charsets.UTF_8)) {
                val parts = line.split('\t', limit = 5)
                if (parts.size != 5) continue
                val lesson = LessonId.fromHex(parts[0]) ?: continue
                val size = parts[1].toLongOrNull() ?: continue
                val millis = parts[2].toLongOrNull() ?: continue
                val ticks = parts[3].toLongOrNull() ?: continue
                map[Key(lesson, parts[4])] = Seen(size, millis, ticks)
            }
        } catch (e: IOException) {
            log("The class folder's file times could not be read; the phone's own times are listed", e)
        }
        return map
    }

    private fun save() {
        val text = loaded().entries.joinToString("") { (key, seen) ->
            "${key.lesson.hex}\t${seen.size}\t${seen.millis}\t${seen.ticks}\t${key.path}\n"
        }
        try {
            file.absoluteFile.parentFile?.mkdirs()
            writeAtomically(file, text.toByteArray(Charsets.UTF_8))
        } catch (e: IOException) {
            // Kept in memory for this run; at worst a file is copied once more.
            log("The class folder's file times could not be saved", e)
        }
    }
}
