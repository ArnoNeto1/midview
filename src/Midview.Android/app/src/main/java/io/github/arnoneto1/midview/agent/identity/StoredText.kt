package io.github.arnoneto1.midview.agent.identity

// The phone's own format for what it keeps of the teacher's blocks and
// settings (SiteBlockStore, AppBlockStore, LabConfigStore, ClassLessonStore,
// OptimizeStore), apart from the network's: a change of a message on the
// wire must never make a kept block unreadable (and silently lift it). A
// first line "midview-<kind> <version>", then one "key=value" per line, keys
// repeating for a list. Read tolerantly: a newer version is read for the
// keys this one knows, unknown keys are skipped, and the store drops only an
// entry it cannot use, never the whole file.
object StoredText {
    const val VERSION = 2

    data class Parsed(val version: Int, val entries: List<Pair<String, String>>) {
        fun first(key: String): String? = entries.firstOrNull { it.first == key }?.second

        fun all(key: String): List<String> = entries.filter { it.first == key }.map { it.second }
    }

    // A value's line breaks and % are written as %0A, %0D and %25.
    fun encode(kind: String, entries: List<Pair<String, String>>): ByteArray = buildString {
        append(HEADER).append(kind).append(' ').append(VERSION).append('\n')
        for ((key, value) in entries) {
            require(key.isNotEmpty() && key.none { it == '=' || it == '\n' || it == '\r' }) { "not a stored key: $key" }
            append(key).append('=').append(escape(value)).append('\n')
        }
    }.toByteArray(Charsets.UTF_8)

    // Null when the bytes are not this format for kind (an older file, in
    // the store's old format, or something else).
    fun decode(kind: String, bytes: ByteArray): Parsed? {
        val lines = String(bytes, Charsets.UTF_8).split('\n').map { it.removeSuffix("\r") }
        val header = lines.firstOrNull() ?: return null
        val prefix = "$HEADER$kind "
        if (!header.startsWith(prefix)) return null
        val version = header.removePrefix(prefix).trim().toIntOrNull() ?: return null
        if (version < VERSION) return null
        val entries = lines.drop(1).mapNotNull { line ->
            val at = line.indexOf('=')
            if (at <= 0) null else line.substring(0, at) to unescape(line.substring(at + 1))
        }
        return Parsed(version, entries)
    }

    private const val HEADER = "midview-"

    private fun escape(value: String): String = buildString {
        for (c in value) {
            when (c) {
                '%' -> append("%25")
                '\n' -> append("%0A")
                '\r' -> append("%0D")
                else -> append(c)
            }
        }
    }

    // Anything else after a % is kept as it is.
    private fun unescape(value: String): String = buildString {
        var i = 0
        while (i < value.length) {
            val code = if (value[i] == '%' && i + 3 <= value.length) value.substring(i + 1, i + 3) else null
            when (code) {
                "25" -> append('%')
                "0A" -> append('\n')
                "0D" -> append('\r')
                else -> {
                    append(value[i])
                    i++
                    continue
                }
            }
            i += 3
        }
    }
}
