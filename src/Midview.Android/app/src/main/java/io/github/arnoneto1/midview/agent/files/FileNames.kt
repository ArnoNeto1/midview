package io.github.arnoneto1.midview.agent.files

// The names a file from the network is saved under, as
// src/Midview.Protocol/FileNames.cs: the teacher's name is never trusted to
// be a safe one, and two files never share a name.
object FileNames {
    const val MAX_NAME_LENGTH = 120

    private const val INVALID = "<>:\"/\\|?*"

    private val RESERVED = setOf("CON", "PRN", "AUX", "NUL") +
        (1..9).map { "COM$it" } + (1..9).map { "LPT$it" }

    // Only ever a plain name: the last segment of whatever was sent
    // ("../../x.apk" is "x.apk"), the characters Windows forbids and control
    // characters as '_', no trailing dots or spaces, at most 120 characters
    // (the extension kept). The same name as a PC would save, so a file
    // collected back to the teacher's PC keeps it.
    fun safe(requested: String): String {
        var name = requested.replace('\\', '/')
        name = name.substring(name.lastIndexOf('/') + 1)
        name = name.map { if (it.code < 32 || it in INVALID) '_' else it }.joinToString("")
        name = name.trim().trimEnd('.', ' ')
        if (name.isEmpty()) name = "arquivo"
        if (name.split('.')[0].uppercase() in RESERVED) name = "_$name"
        if (name.length > MAX_NAME_LENGTH) {
            var extension = extensionOf(name)
            if (extension.length > 20) extension = ""
            name = name.substring(0, MAX_NAME_LENGTH - extension.length).trimEnd('.', ' ') + extension
        }
        return name
    }

    // "notas.pdf", then "notas (2).pdf", "notas (3).pdf"... the first one
    // taken says no to (ignoring case, as the phone's storage does).
    fun unique(safeName: String, taken: (String) -> Boolean): String {
        val extension = extensionOf(safeName)
        val stem = safeName.substring(0, safeName.length - extension.length)
        var copy = 1
        while (true) {
            val candidate = if (copy == 1) safeName else "$stem ($copy)$extension"
            if (!taken(candidate)) return candidate
            copy++
        }
    }

    // ".pdf" for "notas.pdf", "" for "notas": from the last dot on, as
    // .NET's Path.GetExtension (".bashrc" is all extension there too).
    fun extensionOf(name: String): String {
        val dot = name.lastIndexOf('.')
        return if (dot < 0) "" else name.substring(dot)
    }
}
