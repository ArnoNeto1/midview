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
                file.parentFile?.mkdirs()
                writeAtomically(file, current.joinToString(separator = "\n", postfix = "\n").toByteArray(Charsets.US_ASCII))
            }
        }
    }

    // "Esquecer os outros professores": the list becomes this teacher alone.
    // Returns how many others were dropped; with none the file is left as it
    // is. The memory every instance shares is updated with the file, so no
    // instance trusts a forgotten teacher a moment longer.
    fun keepOnly(thumb: String): Int {
        val kept = normalize(thumb)
        require(kept.isNotEmpty()) { "keepOnly needs a teacher" }

        synchronized(lock) {
            val current = load()
            val dropped = current.count { it != kept }
            if (dropped > 0) {
                file.parentFile?.mkdirs()
                writeAtomically(file, "$kept\n".toByteArray(Charsets.US_ASCII))
                cache[file.absolutePath] = Cached(Stamp(file.lastModified(), file.length()), setOf(kept))
            }
            return dropped
        }
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
