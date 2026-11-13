package io.github.arnoneto1.midview.agent.identity

import java.io.File

// The trusted teachers file in the app's private storage.
const val TRUSTED_MASTERS_FILE = "trusted-masters.txt"

// The thumbprints of the teachers this device trusts: one upper-case
// thumbprint per line, in the app's private storage. The device's
// counterpart of the Windows trusted-masters.json (TrustStore,
// TrustedMastersFile). Kept in memory and read again only when the file
// changed (its time or size), so a flood of discovery packets is no flood of
// disk reads; a teacher added through another instance is trusted at once
// (the add updates the memory every instance shares). Written whole to a
// temporary file and moved over the old one, so a reader never sees half a
// list.
class TrustedMasters(private val file: File) {
    fun isTrusted(thumb: String): Boolean {
        val wanted = normalize(thumb)
        return wanted.isNotEmpty() && synchronized(lock) { wanted in load() }
    }

    // Adds a teacher; one already there (in any case) is kept once.
    fun add(thumb: String) {
        val added = normalize(thumb)
        if (added.isEmpty()) {
            return
        }

        synchronized(lock) {
            val current = load()
            if (current.add(added)) {
                write(current)
            }
        }
    }

    // "Esquecer os outros professores": the list becomes this teacher alone.
    // Returns how many others were dropped; with none the file is left as it
    // is. Null, and the file left alone, when this teacher is no longer on
    // the list (another one made the phone forget it a moment ago): it must
    // not come back as the only one.
    fun keepOnly(thumb: String): Int? {
        val kept = normalize(thumb)
        synchronized(lock) {
            val current = load()
            if (kept.isEmpty() || kept !in current) return null
            val dropped = current.count { it != kept }
            if (dropped > 0) write(linkedSetOf(kept))
            return dropped
        }
    }

    // Writes the list and puts it in the memory every instance shares, so a
    // rewrite of the same size in the same tick of the file clock is never
    // missed (every write goes through here: the file is the app's own).
    private fun write(teachers: Set<String>) {
        file.parentFile?.mkdirs()
        writeAtomically(file, teachers.joinToString(separator = "\n", postfix = "\n").toByteArray(Charsets.US_ASCII))
        cache[file.absolutePath] = Cached(Stamp(file.lastModified(), file.length()), LinkedHashSet(teachers))
    }

    // How many teachers are trusted (none: nobody paired yet).
    fun count(): Int = synchronized(lock) { load().size }

    // A copy of the list (callers may change it), from memory while the
    // file is as it was when last read.
    private fun load(): LinkedHashSet<String> {
        val key = file.absolutePath
        val stamp = Stamp(file.lastModified(), if (file.isFile) file.length() else -1)
        val held = cache[key]
        if (held != null && held.stamp == stamp) return LinkedHashSet(held.teachers)
        val read = if (file.isFile) {
            file.readLines(Charsets.US_ASCII).map(::normalize).filterTo(LinkedHashSet()) { it.isNotEmpty() }
        } else {
            LinkedHashSet()
        }
        cache[key] = Cached(stamp, read)
        return LinkedHashSet(read)
    }

    private data class Stamp(val modified: Long, val length: Long)

    private class Cached(val stamp: Stamp, val teachers: Set<String>)

    private companion object {
        // One lock for every instance: two adds through different instances
        // must not each write a list without the other's teacher.
        val lock = Any()

        // The lists read, by file, shared by every instance (under lock).
        val cache = HashMap<String, Cached>()

        fun normalize(thumb: String) = thumb.trim().uppercase()
    }
}
