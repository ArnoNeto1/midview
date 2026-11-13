package io.github.arnoneto1.midview.agent.leave

import io.github.arnoneto1.midview.agent.identity.StoredText
import io.github.arnoneto1.midview.agent.identity.writeAtomically
import java.io.File
import java.io.IOException

// How far the Midview's leaving of this phone went (PhoneLeave), kept in
// the device-protected storage so even the check at a locked boot sees it:
// STARTED once the teacher (or the admin password on the phone) asked for
// it, UNDONE once the school's locks were lifted, OWNER_CLEARED once the app
// stopped being the Device Owner. No file: the phone is not leaving. A file
// that cannot be read counts as STARTED: a phone that was leaving must never
// lock itself again because its record was spoiled.
class LeaveStore(private val file: File, private val log: (String, Throwable?) -> Unit = { _, _ -> }) {
    enum class Phase(val word: String) { STARTED("started"), UNDONE("undone"), OWNER_CLEARED("owner-cleared") }

    @Volatile var phase: Phase? = load()
        private set

    // Kept before it counts: a phase that cannot be written still holds for
    // this process (the next one may start over, which is safe).
    @Synchronized
    fun save(next: Phase) {
        phase = next
        try {
            file.absoluteFile.parentFile?.mkdirs()
            writeAtomically(file, StoredText.encode(KIND, listOf("phase" to next.word)))
        } catch (e: IOException) {
            log("Couldn't save how far the Midview's leaving went", e)
        }
    }

    private fun load(): Phase? {
        if (!file.exists()) return null
        val word = try {
            StoredText.decode(KIND, file.readBytes())?.first("phase")
        } catch (e: IOException) {
            log("Couldn't read how far the Midview's leaving went", e)
            null
        }
        return Phase.values().firstOrNull { it.word == word } ?: Phase.STARTED
    }

    private companion object {
        const val KIND = "leaving"
    }
}
