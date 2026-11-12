package io.github.arnoneto1.midview.agent.update

import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.IsoTime
import io.github.arnoneto1.midview.agent.protocol.JsonText
import io.github.arnoneto1.midview.agent.protocol.UpdateAttemptReport
import org.json.JSONException
import java.io.File
import java.io.IOException
import java.time.Duration
import java.time.Instant

// The states of an update attempt on the phone, those of UpdateStates in
// src/Midview.Protocol/NetworkUpdate.cs that a phone goes through (no
// "provisional": Android installs the new version, nothing is put on trial).
// The last two are final.
object UpdateStates {
    const val RECEIVING = "receiving"
    const val VERIFYING = "verifying"
    const val APPLYING = "applying"
    const val COMMITTED = "committed"
    const val FAILED = "failed"

    val ALL = setOf(RECEIVING, VERIFYING, APPLYING, COMMITTED, FAILED)

    fun isFinal(state: String) = state == COMMITTED || state == FAILED
}

// One update of the agent as the phone records it: the Master's attempt id
// (hex as .NET's Convert.ToHexString writes it, since the Master follows the
// attempt by it), its state, a short text for the teacher, when it last
// changed and, from "applying" on, the versionCode of the APK being
// installed (how the next start knows whether it is that version).
data class ApkAttempt(
    val id: String,
    val state: String,
    val detail: String,
    val updatedAt: Instant,
    val expectedVersionCode: Long? = null,
) {
    fun report() = UpdateAttemptReport(id, state, detail, updatedAt)
}

// The last attempt, kept in the app's own storage (written atomically) so
// the next version of the app, or the agent after a restart, can close it.
// Read from the file every time: Android's answer to the install comes to a
// receiver with a store of its own (InstallResult).
class ApkUpdateStore(
    private val file: File,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    val current: ApkAttempt? get() = synchronized(GATE) { load() }

    // False when the disk refused.
    fun save(attempt: ApkAttempt): Boolean = synchronized(GATE) { write(attempt) }

    // change gets the attempt on file and gives the one to save in its place,
    // or null to leave it. Returns what is on file afterwards.
    fun update(change: (ApkAttempt?) -> ApkAttempt?): ApkAttempt? = synchronized(GATE) {
        val held = load()
        val next = change(held) ?: return held
        write(next)
        next
    }

    // Android refused the install of this attempt (InstallResult). False when
    // the attempt on file is another one, or no longer being installed.
    fun installFailed(id: String, detail: String, now: Instant): Boolean {
        var failed = false
        update { held ->
            if (held?.id == id && held.state == UpdateStates.APPLYING) {
                failed = true
                held.copy(state = UpdateStates.FAILED, detail = detail, updatedAt = now)
            } else {
                null
            }
        }
        return failed
    }

    private fun write(attempt: ApkAttempt): Boolean =
        try {
            file.absoluteFile.parentFile?.mkdirs()
            writeAtomically(file, encode(attempt))
            true
        } catch (e: IOException) {
            log("Couldn't save the update attempt", e)
            false
        }

    // Unreadable means no attempt. A file with bad content is kept aside; one
    // that could not be read is left for the next time.
    private fun load(): ApkAttempt? {
        if (!file.exists()) return null
        val bytes = try {
            file.readBytes()
        } catch (e: IOException) {
            log("Couldn't read the update attempt", e)
            return null
        }
        return decode(bytes) ?: run {
            log("Couldn't read the update attempt; setting it aside", null)
            file.renameTo(File(file.path + ".corrupt"))
            null
        }
    }

    companion object {
        // An install Android neither finished nor answered by then never will.
        val APPLY_TIMEOUT: Duration = Duration.ofMinutes(3)

        // Every store of the file, in this process, takes turns.
        private val GATE = Any()

        const val RESTARTED = "interrompida (o Midview reiniciou)"

        fun notFinished(versionName: String) = "a instalação não terminou; o celular continua na versão $versionName"

        // What becomes of the last attempt when the agent starts (null: it
        // stays as it is). An upload or a check that a restart cut short
        // failed. One being installed is done when this is the version it
        // installs, and failed when it is not and Android no longer installs
        // it (the install failed, or the phone restarted while it ran). One
        // that failed (APPLY_TIMEOUT) while Android was still installing it
        // is done too when this is its version: Android finished late.
        fun atStart(attempt: ApkAttempt?, runningVersionCode: Long, versionName: String, installing: Boolean, now: Instant): ApkAttempt? =
            when {
                attempt?.state == UpdateStates.FAILED && attempt.expectedVersionCode == runningVersionCode ->
                    attempt.copy(state = UpdateStates.COMMITTED, detail = "atualizado para $versionName", updatedAt = now)
                attempt == null || UpdateStates.isFinal(attempt.state) -> null
                attempt.state != UpdateStates.APPLYING ->
                    attempt.copy(state = UpdateStates.FAILED, detail = RESTARTED, updatedAt = now)
                attempt.expectedVersionCode == runningVersionCode ->
                    attempt.copy(state = UpdateStates.COMMITTED, detail = "atualizado para $versionName", updatedAt = now)
                installing -> null
                else -> attempt.copy(state = UpdateStates.FAILED, detail = notFinished(versionName), updatedAt = now)
            }

        // An install still "applying" past APPLY_TIMEOUT (either way: a
        // clock set back must not hold the next update off) failed; null
        // when it has not.
        fun expired(attempt: ApkAttempt?, now: Instant, versionName: String): ApkAttempt? =
            if (attempt?.state == UpdateStates.APPLYING && Duration.between(attempt.updatedAt, now).abs() >= APPLY_TIMEOUT) {
                attempt.copy(state = UpdateStates.FAILED, detail = notFinished(versionName), updatedAt = now)
            } else {
                null
            }

        fun encode(attempt: ApkAttempt): ByteArray =
            JsonText.obj(
                "id" to JsonText.string(attempt.id),
                "state" to JsonText.string(attempt.state),
                "detail" to JsonText.string(attempt.detail),
                "updatedAt" to JsonText.string(IsoTime.format(attempt.updatedAt)),
                "expectedVersionCode" to (attempt.expectedVersionCode?.toString() ?: "null"),
            ).toByteArray(Charsets.UTF_8)

        // Null for anything this agent did not write.
        fun decode(bytes: ByteArray): ApkAttempt? {
            val json = JsonText.parseObject(String(bytes, Charsets.UTF_8)) ?: return null
            return try {
                val state = json.getString("state").takeIf { it in UpdateStates.ALL } ?: return null
                ApkAttempt(
                    json.getString("id"),
                    state,
                    json.getString("detail"),
                    IsoTime.parse(json.getString("updatedAt")) ?: return null,
                    if (json.isNull("expectedVersionCode")) null else json.getLong("expectedVersionCode"),
                )
            } catch (_: JSONException) {
                null
            }
        }
    }
}
