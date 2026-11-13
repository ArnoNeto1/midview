package io.github.arnoneto1.midview.agent.policy

import io.github.arnoneto1.midview.agent.identity.StoredText
import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.PhoneAdminPassword
import java.io.File
import java.io.IOException

// The phones' admin password on this phone ("Ajustes da escola"): the
// teacher sends only its salted hash (PhoneAdminPassword), kept in the app's
// storage; whoever types the password here frees the Wi-Fi and airplane mode
// for 10 minutes (free, NetworkFree). Five wrong passwords in a row lock the
// screen out for 5 minutes; the count is kept in storage and raised before
// the check, so neither closing the screen nor killing the app gets around
// it. Nothing typed is logged or kept.
class AdminUnlock(
    private val passwordFile: File,
    private val attemptsFile: File,
    private val free: (minutes: Int) -> Pair<Boolean, String>,
    private val now: () -> Long = System::currentTimeMillis,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    sealed interface State {
        object NoPassword : State
        data class LockedOut(val untilMillis: Long) : State
        object Ready : State
    }

    sealed interface Attempt {
        object NoPassword : Attempt
        data class LockedOut(val untilMillis: Long) : Attempt
        data class Wrong(val triesLeft: Int) : Attempt
        object Freed : Attempt

        // The password is right; nothing was freed (checkOnly).
        object Verified : Attempt
        data class Refused(val detail: String) : Attempt
    }

    private var failures = 0
    private var lockedUntil: Long? = null

    init {
        loadAttempts()
    }

    // The teacher's SET_PHONE_ADMIN_PASSWORD payload in (empty removes the
    // password), the CommandAck's result out. A new password starts the
    // count of wrong ones over.
    @Synchronized
    fun setPassword(payload: ByteArray): Pair<Boolean, String> {
        if (payload.isEmpty()) {
            if (passwordFile.exists() && !passwordFile.delete()) return false to NOT_SAVED
            resetAttempts()
            log("Admin password removed.", null)
            return true to REMOVED
        }
        if (PhoneAdminPassword.decode(payload) == null) return false to PhoneAdminPassword.INVALID
        try {
            passwordFile.absoluteFile.parentFile?.mkdirs()
            writeAtomically(passwordFile, payload)
        } catch (e: IOException) {
            log("Couldn't save the admin password", e)
            return false to NOT_SAVED
        }
        resetAttempts()
        log("Admin password set.", null)
        return true to SET
    }

    @Synchronized
    fun state(): State {
        if (stored() == null) return State.NoPassword
        return lockout()?.let { State.LockedOut(it) } ?: State.Ready
    }

    // Slow on purpose (PBKDF2): never on the main thread.
    @Synchronized
    fun tryPassword(typed: String): Attempt {
        val checked = check(typed)
        if (checked != Attempt.Verified) return checked
        val (freed, detail) = free(MINUTES)
        log("Admin password typed on the phone: Wi-Fi freed=$freed.", null)
        return if (freed) Attempt.Freed else Attempt.Refused(detail)
    }

    // The password checked alone, with the same count of wrong ones and the
    // same wait ("Tirar o Midview deste celular"): Verified when it is right.
    @Synchronized
    fun checkOnly(typed: String): Attempt {
        val checked = check(typed)
        if (checked == Attempt.Verified) log("Admin password typed on the phone to take the Midview off.", null)
        return checked
    }

    private fun check(typed: String): Attempt {
        val stored = stored() ?: return Attempt.NoPassword
        lockout()?.let { return Attempt.LockedOut(it) }
        failures++
        saveAttempts()
        if (PhoneAdminPassword.verify(typed, stored)) {
            resetAttempts()
            return Attempt.Verified
        }
        if (failures >= MAX_TRIES) {
            failures = 0
            val end = now() + LOCKOUT_MILLIS
            lockedUntil = end
            saveAttempts()
            return Attempt.LockedOut(end)
        }
        return Attempt.Wrong(MAX_TRIES - failures)
    }

    // The end of a lockout that runs, or null. A clock set back (an end
    // further away than a whole lockout) starts a whole one from now.
    private fun lockout(): Long? {
        val end = lockedUntil ?: return null
        val at = now()
        if (at >= end) {
            lockedUntil = null
            saveAttempts()
            return null
        }
        if (end - at > LOCKOUT_MILLIS) {
            lockedUntil = at + LOCKOUT_MILLIS
            saveAttempts()
        }
        return lockedUntil
    }

    private fun stored(): PhoneAdminPassword.Stored? {
        if (!passwordFile.exists()) return null
        return try {
            PhoneAdminPassword.decode(passwordFile.readBytes())
        } catch (e: IOException) {
            log("Couldn't read the admin password", e)
            null
        }
    }

    private fun resetAttempts() {
        failures = 0
        lockedUntil = null
        saveAttempts()
    }

    private fun saveAttempts() {
        try {
            attemptsFile.absoluteFile.parentFile?.mkdirs()
            writeAtomically(
                attemptsFile,
                StoredText.encode(KIND, listOf("failures" to failures.toString(), "lockedUntil" to (lockedUntil?.toString() ?: ""))),
            )
        } catch (e: IOException) {
            log("Couldn't save the admin password's attempts", e)
        }
    }

    // A file that cannot be read counts as a lockout from now: guessing must
    // not get easier by spoiling it.
    private fun loadAttempts() {
        if (!attemptsFile.exists()) return
        val stored = try {
            StoredText.decode(KIND, attemptsFile.readBytes())
        } catch (e: IOException) {
            null
        }
        if (stored == null) {
            lockedUntil = now() + LOCKOUT_MILLIS
            return
        }
        failures = stored.first("failures")?.toIntOrNull()?.coerceAtLeast(0) ?: 0
        lockedUntil = stored.first("lockedUntil")?.toLongOrNull()
        // The fifth wrong try was counted but the app ended before its check
        // did (a restart in the middle): the wait starts now.
        if (failures >= MAX_TRIES) {
            failures = 0
            if ((lockedUntil ?: 0L) <= now()) lockedUntil = now() + LOCKOUT_MILLIS
            saveAttempts()
        }
    }

    companion object {
        const val MINUTES = 10
        const val MAX_TRIES = 5
        const val LOCKOUT_MILLIS = 5 * 60_000L
        private const val KIND = "admin-attempts"

        const val SET = "senha de administrador posta no celular"
        const val REMOVED = "senha de administrador tirada do celular"
        const val NOT_SAVED = "o celular não conseguiu guardar a senha de administrador"
    }
}
