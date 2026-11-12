package io.github.arnoneto1.midview.agent.policy

import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.PhoneScreenPassword
import java.io.File
import java.io.IOException

// What the agent keeps for the phone's lock screen: the reset token it gave
// Android (DevicePolicyManager.setResetPasswordToken, hex of 32 random
// bytes) and on which boot it gave it, the teacher's PIN (null: the teacher
// wants no PIN), and whether someone other than the agent changed the lock
// since the agent last set it. pinUnknown: the file was damaged, so whether
// the teacher set a PIN is not known; the lock is left as it is (never
// removed) until the teacher sends the PIN again.
data class ScreenPasswordState(
    val token: String?,
    val tokenBoot: Int?,
    val teacherPin: String?,
    val changedByOther: Boolean,
    val pinUnknown: Boolean = false,
) {
    companion object {
        val EMPTY = ScreenPasswordState(null, null, null, false)

        // What a damaged file (and its copy) leaves.
        val UNKNOWN = ScreenPasswordState(null, null, null, false, pinUnknown = true)
    }
}

// ScreenPasswordState in a file of its own, written atomically, with a copy
// beside it (.bak) for a file the storage damaged. With both unreadable the
// teacher's PIN is unknown (ScreenPasswordState.UNKNOWN): the safer side is
// to leave the lock as it is, never to remove a PIN the teacher may have
// set; the teacher's next PIN settles it.
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

    private val copy = File(file.path + BACKUP_SUFFIX)

    @Volatile private var state: ScreenPasswordState = load(file, copy, log)

    val current: ScreenPasswordState get() = state

    // Kept and written to file; false when the disk refused (it then still
    // counts until the agent starts again).
    fun save(next: ScreenPasswordState): Boolean = synchronized(gate) {
        state = next
        try {
            file.absoluteFile.parentFile?.mkdirs()
            val bytes = encode(next)
            writeAtomically(file, bytes)
            writeAtomically(copy, bytes)
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
            if (state.pinUnknown) append("unknown=1\n")
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
            val unknown = when (values["unknown"]) {
                "1" -> pin == null
                "0", null -> false
                else -> return null
            }
            return ScreenPasswordState(token, tokenBoot, pin, changed, unknown)
        }

        private const val BACKUP_SUFFIX = ".bak"

        // Nothing written yet reads as nothing kept. A damaged file is read
        // from its copy; with both damaged the teacher's PIN is unknown (a
        // new token is made, and the lock waits for the teacher's PIN).
        private fun load(file: File, copy: File, log: (String, Throwable?) -> Unit): ScreenPasswordState {
            if (!file.exists() && !copy.exists()) return ScreenPasswordState.EMPTY
            read(file, log)?.let { return it }
            read(copy, log)?.let {
                log("The lock screen state file is damaged; read from its copy", null)
                return it
            }
            log("The lock screen state file and its copy are damaged: the teacher's PIN is unknown until sent again", null)
            return ScreenPasswordState.UNKNOWN
        }

        private fun read(file: File, log: (String, Throwable?) -> Unit): ScreenPasswordState? {
            if (!file.exists()) return null
            return try {
                decode(file.readBytes()) ?: run {
                    log("The lock screen state file ${file.name} is damaged", null)
                    null
                }
            } catch (e: IOException) {
                log("Couldn't read the lock screen state ${file.name}", e)
                null
            }
        }
    }
}
