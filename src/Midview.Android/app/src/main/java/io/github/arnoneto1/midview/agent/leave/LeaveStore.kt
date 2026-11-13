package io.github.arnoneto1.midview.agent.leave

import io.github.arnoneto1.midview.agent.identity.StoredText
import io.github.arnoneto1.midview.agent.identity.writeAtomically
import java.io.File
import java.io.IOException

// How far the Midview's leaving of this phone went (PhoneLeave), kept in
// the device-protected storage so even the check at a locked boot sees it:
// STARTED once the teacher (or the admin password on the phone) asked for
// it, UNDONE once the school's locks were lifted, OWNER_CLEARED once the app
// stopped being the Device Owner. No record: the phone is not leaving.
//
// The record is written twice (the file and a copy beside it, each whole or
// not at all), and a damaged one is read from the other. With both damaged
// the phase cannot be known, and the phone counts as not leaving: the agent
// locks it again and stays, never taking itself off a phone on a spoiled
// file; a teacher who still wants it gone asks again. (Counting it as
// leaving would instead either take the Midview off on corruption or, kept
// from finishing, leave the phone unlocked for good.)
class LeaveStore(private val file: File, private val log: (String, Throwable?) -> Unit = { _, _ -> }) {
    enum class Phase(val word: String) { STARTED("started"), UNDONE("undone"), OWNER_CLEARED("owner-cleared") }

    private val copy = File(file.path + COPY_SUFFIX)

    @Volatile var phase: Phase? = load()
        private set

    // Kept before it counts: a phase that cannot be written still holds for
    // this process (the next one may start over, which is safe).
    @Synchronized
    fun save(next: Phase) {
        phase = next
        val bytes = StoredText.encode(KIND, listOf("phase" to next.word))
        for (target in listOf(file, copy)) {
            try {
                target.absoluteFile.parentFile?.mkdirs()
                writeAtomically(target, bytes)
            } catch (e: IOException) {
                log("Couldn't save how far the Midview's leaving went", e)
            }
        }
    }

    private fun load(): Phase? {
        if (!file.exists() && !copy.exists()) return null
        val phase = read(file) ?: read(copy)
        if (phase == null) log("The record of the Midview's leaving is damaged: the phone is not leaving.", null)
        return phase
    }

    private fun read(from: File): Phase? {
        if (!from.exists()) return null
        val word = try {
            StoredText.decode(KIND, from.readBytes())?.first("phase")
        } catch (e: IOException) {
            log("Couldn't read how far the Midview's leaving went", e)
            null
        }
        return Phase.values().firstOrNull { it.word == word }
    }

    companion object {
        private const val KIND = "leaving"
        const val COPY_SUFFIX = ".copy"
    }
}
