package io.github.arnoneto1.midview.agent.policy

import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.PhoneScreenPassword
import java.io.File
import java.io.IOException

// What the agent keeps for the phone's lock screen: the reset token it gave
// Android (DevicePolicyManager.setResetPasswordToken, hex of 32 random
// bytes) and on which boot it gave it, the teacher's PIN (null: the teacher
// wants no PIN), and whether someone other than the agent changed the lock
// since the agent last set it.
data class ScreenPasswordState(
    val token: String?,
    val tokenBoot: Int?,
    val teacherPin: String?,
    val changedByOther: Boolean,
) {
    companion object {
        val EMPTY = ScreenPasswordState(null, null, null, false)
    }
}

// ScreenPasswordState in a file of its own, written atomically.
//
// The file lives in the app's device-protected storage
// (Context.createDeviceProtectedStorageContext), not the usual
// credential-protected one: after a restart the phone is still locked with
// the PIN, and Android only opens credential-protected storage once the PIN
// is entered. The agent must read the token and the teacher's PIN before
// that, at the locked boot, to put the PIN back if the student changed it.
// The trade-off: device-protected storage is encrypted with a key that does
// not depend on the PIN, so the token and the PIN are readable by this app
// (and by root or someone who breaks Android's file encryption) without the
// PIN. Only this app, as the Device Owner, can use the token; the PIN is the
// classroom PIN the teacher picked for the phones, not anyone's own secret.
class ScreenPasswordStore(
    private val file: File,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    private val gate = Any()

    @Volatile private var state: ScreenPasswordState = load(file, log) ?: ScreenPasswordState.EMPTY

    val current: ScreenPasswordState get() = state

    // Kept and written to file; false when the disk refused (it then still
    // counts until the agent starts again).
    fun save(next: ScreenPasswordState): Boolean = synchronized(gate) {
        state = next
        try {
            file.absoluteFile.parentFile?.mkdirs()
            writeAtomically(file, encode(next))
            true
        } catch (e: IOException) {
            log("Couldn't save the lock screen state", e)
            false
        }
    }

    companion object {
        private val HEX_TOKEN = Regex("[0-9A-F]{64}")

        // "key=value" lines; a key left out is null (or false).
        fun encode(state: ScreenPasswordState): ByteArray = buildString {
            state.token?.let { append("token=").append(it).append('\n') }
            state.tokenBoot?.let { append("tokenBoot=").append(it).append('\n') }
            state.teacherPin?.let { append("pin=").append(it).append('\n') }
            append("changed=").append(if (state.changedByOther) 1 else 0).append('\n')
        }.toByteArray(Charsets.US_ASCII)

        // Null for a file that is not one this store wrote.
        fun decode(bytes: ByteArray): ScreenPasswordState? {
            val values = HashMap<String, String>()
            for (line in String(bytes, Charsets.US_ASCII).split('\n')) {
                if (line.isEmpty()) continue
                val at = line.indexOf('=')
                if (at <= 0) return null
                values[line.substring(0, at)] = line.substring(at + 1)
            }
            val token = values["token"]?.also { if (!HEX_TOKEN.matches(it)) return null }
            val tokenBoot = values["tokenBoot"]?.let { it.toIntOrNull() ?: return null }
            val pin = values["pin"]?.also { if (it.isEmpty() || !PhoneScreenPassword.isValid(it)) return null }
            val changed = when (values["changed"]) {
                "1" -> true
                "0", null -> false
                else -> return null
            }
            return ScreenPasswordState(token, tokenBoot, pin, changed)
        }

        // A file damaged or missing reads as nothing kept: a new token is
        // made, and the teacher sends the PIN again.
        private fun load(file: File, log: (String, Throwable?) -> Unit): ScreenPasswordState? {
            if (!file.exists()) return null
            return try {
                decode(file.readBytes()) ?: run {
                    log("The lock screen state file is damaged; starting over", null)
                    null
                }
            } catch (e: IOException) {
                log("Couldn't read the lock screen state", e)
                null
            }
        }
    }
}
