package io.github.arnoneto1.midview.agent.identity

import java.io.File
import java.io.FileOutputStream
import java.nio.file.Files
import java.nio.file.StandardCopyOption

// Writes to a temporary file next to the target, flushes it to the disk and
// moves it over the target, so a reader (or a crash) never sees half a file.
internal fun writeAtomically(target: File, bytes: ByteArray) {
    val temp = File(target.path + ".tmp")
    FileOutputStream(temp).use { out ->
        out.write(bytes)
        out.fd.sync()
    }
    Files.move(temp.toPath(), target.toPath(), StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE)
}
