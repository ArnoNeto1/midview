package io.github.arnoneto1.midview.agent.identity

import java.io.File

// The trusted teachers file in the app's private storage.
const val TRUSTED_MASTERS_FILE = "trusted-masters.txt"

// The thumbprints of the teachers this device trusts: one upper-case
// thumbprint per line, in the app's private storage. The device's
// counterpart of the Windows trusted-masters.json (TrustStore,
// TrustedMastersFile). Read on every question, so a teacher added through
// another instance is trusted at once; written whole to a temporary file and
// moved over the old one, so a reader never sees half a list.
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

    // How many teachers are trusted (none: nobody paired yet).
    fun count(): Int = synchronized(lock) { load().size }

    private fun load(): LinkedHashSet<String> =
        if (file.isFile) {
            file.readLines(Charsets.US_ASCII).map(::normalize).filterTo(LinkedHashSet()) { it.isNotEmpty() }
        } else {
            LinkedHashSet()
        }

    private companion object {
        // One lock for every instance: two adds through different instances
        // must not each write a list without the other's teacher.
        val lock = Any()

        fun normalize(thumb: String) = thumb.trim().uppercase()
    }
}
