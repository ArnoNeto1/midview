package io.github.arnoneto1.midview.agent.policy

import io.github.arnoneto1.midview.agent.identity.StoredText
import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.PhoneNetworkFree
import java.io.File
import java.io.IOException

// "Liberar Wi-Fi e modo avião": the Wi-Fi and airplane mode locks
// (Lockdown.networkRestrictions, given as restrictions) lifted for a while, by the teacher's
// command or the admin password typed on the phone (AdminUnlock), and put
// back when the time is up. The end is kept in the app's storage on the wall
// clock, so neither a restart of the agent nor of the phone leaves them open:
// every start checks it (Lockdown asks unlessFree), and so does the watchdog
// every few seconds (check). The student cannot move the end: the date and
// time are locked too (no_config_date_time); a clock that went back anyway
// (an end further away than the longest time allowed) locks again at once.
// Anything unreadable counts as locked. Locking again after a free time
// brings the phone back to the school's Wi-Fi (rejoinWifi, SchoolWifi): the
// student may have turned it off or joined another network meanwhile; while
// locked, every check keeps the Wi-Fi on (keepWifiOn: before Android 13
// nothing else stops the student from turning it off). The agent follows
// the end (follow) to lock again right on time; the watchdog is the fallback.
class NetworkFree(
    private val file: File,
    private val restrictions: List<String>,
    private val isDeviceOwner: () -> Boolean,
    private val addRestriction: (String) -> Unit,
    private val clearRestriction: (String) -> Unit,
    private val now: () -> Long = System::currentTimeMillis,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
    private val keepWifiOn: () -> Unit = {},
    private val rejoinWifi: () -> Unit = {},
) {
    @Volatile private var until: Long? = load()
    private var follower: ((Long?) -> Unit)? = null

    // Told the end of each free time, and null when the network locks (at
    // once with the current end); until what this returns is called.
    @Synchronized
    fun follow(changed: (Long?) -> Unit): () -> Unit {
        follower = changed
        changed(freeUntil())
        return { synchronized(this) { if (follower === changed) follower = null } }
    }

    // The end of the time the network is free (wall clock, milliseconds), or
    // null while it is locked.
    fun freeUntil(): Long? = until?.takeIf { isRunning(it, now()) }

    // The teacher's FREE_PHONE_NETWORK payload in, the CommandAck's result out.
    fun handle(payload: ByteArray): Pair<Boolean, String> =
        PhoneNetworkFree.decode(payload)?.let(::free) ?: (false to INVALID_REQUEST)

    // Frees the Wi-Fi and airplane mode for minutes (1 to 60; 0 locks them
    // now), counted again from now. The CommandAck's result out.
    @Synchronized
    fun free(minutes: Int): Pair<Boolean, String> {
        if (!PhoneNetworkFree.isValid(minutes)) return false to INVALID_REQUEST
        if (minutes == PhoneNetworkFree.LOCK_NOW) return lockNow()
        if (!isDeviceOwner()) return false to NOT_PREPARED
        val end = now() + minutes * MINUTE_MILLIS
        until = end
        save(end)
        follower?.invoke(end)
        try {
            restrictions.forEach(clearRestriction)
        } catch (e: RuntimeException) {
            log("Android did not free the Wi-Fi", e)
            lock()
            return false to NOT_FREED
        }
        log("Wi-Fi and airplane mode free for $minutes minutes.", null)
        return true to freedText(minutes)
    }

    // Locks the Wi-Fi and airplane mode again now.
    @Synchronized
    fun lockNow(): Pair<Boolean, String> {
        if (!isDeviceOwner()) return false to NOT_PREPARED
        return if (lock()) true to LOCKED else false to NOT_LOCKED
    }

    // At each start and each watchdog check: locks again once the time is up.
    // True when it did.
    @Synchronized
    fun check(): Boolean {
        val end = until
        if (end == null) {
            if (isDeviceOwner()) keepWifiOn()
            return false
        }
        if (isRunning(end, now())) return false
        log("The Wi-Fi's free time is up: locked again.", null)
        if (isDeviceOwner()) lock() else clearState()
        return true
    }

    // For Lockdown: locks the network unless its free time runs, under the
    // same lock as free and lockNow, so a start of the agent never puts a
    // lock back that was just lifted.
    @Synchronized
    fun unlessFree(lockNetwork: () -> Unit) {
        check()
        if (until == null) lockNetwork()
    }

    private fun isRunning(end: Long, at: Long): Boolean = at < end && end - at <= MAX_MILLIS

    // The state cleared first: a refusal still leaves the next start locking.
    // The Wi-Fi back on and on the school's network before the locks, which
    // from Android 13 would keep a Wi-Fi the student turned off, off.
    private fun lock(): Boolean {
        val wasFree = until != null
        clearState()
        if (wasFree) rejoinWifi() else keepWifiOn()
        return try {
            restrictions.forEach(addRestriction)
            true
        } catch (e: RuntimeException) {
            log("Android did not lock the Wi-Fi", e)
            false
        }
    }

    private fun clearState() {
        until = null
        follower?.invoke(null)
        if (file.exists() && !file.delete()) log("Couldn't delete the Wi-Fi's free time", null)
    }

    private fun save(end: Long) {
        try {
            file.absoluteFile.parentFile?.mkdirs()
            writeAtomically(file, StoredText.encode(KIND, listOf("until" to end.toString())))
        } catch (e: IOException) {
            // Until the agent starts again, the time still runs out by the watchdog.
            log("Couldn't save the Wi-Fi's free time", e)
        }
    }

    private fun load(): Long? {
        if (!file.exists()) return null
        return try {
            StoredText.decode(KIND, file.readBytes())?.first("until")?.toLongOrNull()
        } catch (e: IOException) {
            log("Couldn't read the Wi-Fi's free time", e)
            null
        }
    }

    companion object {
        const val MINUTE_MILLIS = 60_000L
        const val MAX_MILLIS = PhoneNetworkFree.MAX_MINUTES * MINUTE_MILLIS
        private const val KIND = "network-free"

        const val LOCKED = "Wi-Fi e modo avião travados"
        const val NOT_FREED = "o Android não liberou o Wi-Fi e o modo avião"
        const val NOT_LOCKED = "o Android não travou o Wi-Fi e o modo avião"
        const val NOT_PREPARED = "Este celular não foi preparado pelo Midview."
        const val INVALID_REQUEST = "pedido inválido"

        fun freedText(minutes: Int): String =
            if (minutes == 1) "Wi-Fi e modo avião liberados por 1 minuto" else "Wi-Fi e modo avião liberados por $minutes minutos"
    }
}
