package io.github.arnoneto1.midview.agent.policy

import io.github.arnoneto1.midview.agent.identity.StoredText
import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.PhoneNetworkFree
import java.io.File
import java.io.IOException

// Some of the school's locks (restrictions, Android's user restrictions)
// lifted for a while by the teacher's command, and put back when the time is
// up: "Liberar Wi-Fi e modo avião" (NetworkFree), "Contas do aparelho"
// (AccountsFree) and "Depuração USB" (DebuggingFree). The end is kept in the
// app's storage (file, as kind) on the wall clock, so neither a restart of
// the agent nor of the device leaves them open: every start checks it
// (Lockdown asks unlessFree), and so does the watchdog every few seconds
// (check). The student cannot move the end:
// the date and time are locked too (no_config_date_time); a clock that went
// back anyway (an end further away than the longest time allowed) locks
// again at once. Anything unreadable counts as locked. whileLocked runs at
// each check while locked, afterFree when a free time ends (NetworkFree
// keeps the school's Wi-Fi with them), whenFreed once the locks are lifted
// (DebuggingFree turns USB debugging on). The agent follows the end (follow)
// to lock again right on time; the watchdog is the fallback.
open class TimedFree(
    private val file: File,
    private val kind: String,
    private val restrictions: List<String>,
    private val isDeviceOwner: () -> Boolean,
    private val addRestriction: (String) -> Unit,
    private val clearRestriction: (String) -> Unit,
    private val texts: Texts,
    private val now: () -> Long = System::currentTimeMillis,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
    private val whileLocked: () -> Unit = {},
    private val afterFree: () -> Unit = {},
    private val whenFreed: () -> Unit = {},
) {
    // What the teacher reads (in Portuguese), and the name the log gives
    // the locks (in English: "the device accounts").
    class Texts(
        val locked: String,
        val notFreed: String,
        val notLocked: String,
        val freed: (minutes: Int) -> String,
        val logName: String,
    )

    @Volatile private var until: Long? = load()
    private var follower: ((Long?) -> Unit)? = null

    // Told the end of each free time, and null when the locks come back (at
    // once with the current end); until what this returns is called.
    @Synchronized
    fun follow(changed: (Long?) -> Unit): () -> Unit {
        follower = changed
        changed(freeUntil())
        return { synchronized(this) { if (follower === changed) follower = null } }
    }

    // The end of the free time (wall clock, milliseconds), or null while locked.
    fun freeUntil(): Long? = until?.takeIf { isRunning(it, now()) }

    // The teacher's payload (PhoneNetworkFree's: the minutes) in, the
    // CommandAck's result out.
    fun handle(payload: ByteArray): Pair<Boolean, String> =
        PhoneNetworkFree.decode(payload)?.let(::free) ?: (false to INVALID_REQUEST)

    // Frees the locks for minutes (1 to 60; 0 locks them now), counted again
    // from now. The CommandAck's result out.
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
            whenFreed()
        } catch (e: RuntimeException) {
            log("Android did not free ${texts.logName}", e)
            lock()
            return false to texts.notFreed
        }
        log("Free for $minutes minutes: ${texts.logName}.", null)
        return true to texts.freed(minutes)
    }

    // Locks them again now.
    @Synchronized
    fun lockNow(): Pair<Boolean, String> {
        if (!isDeviceOwner()) return false to NOT_PREPARED
        return if (lock()) true to texts.locked else false to texts.notLocked
    }

    // At each start and each watchdog check: locks again once the time is up.
    // True when it did.
    @Synchronized
    fun check(): Boolean {
        val end = until
        if (end == null) {
            if (isDeviceOwner()) whileLocked()
            return false
        }
        if (isRunning(end, now())) return false
        log("The free time of ${texts.logName} is up: locked again.", null)
        if (isDeviceOwner()) lock() else clearState()
        return true
    }

    // For Lockdown: locks unless the free time runs, under the same lock as
    // free and lockNow, so a start of the agent never puts a lock back that
    // was just lifted.
    @Synchronized
    fun unlessFree(lockThem: () -> Unit) {
        check()
        if (until == null) lockThem()
    }

    // The Midview leaving the phone (PhoneLeave): the free time dropped
    // without locking again. True when one was kept (running or not).
    @Synchronized
    fun forget(): Boolean {
        if (until == null && !file.exists()) return false
        clearState()
        return true
    }

    private fun isRunning(end: Long, at: Long): Boolean = at < end && end - at <= MAX_MILLIS

    // The state cleared first: a refusal still leaves the next start locking.
    // afterFree (or whileLocked) before the locks, which may keep what it
    // does from being done.
    private fun lock(): Boolean {
        val wasFree = until != null
        clearState()
        if (wasFree) afterFree() else whileLocked()
        return try {
            restrictions.forEach(addRestriction)
            true
        } catch (e: RuntimeException) {
            log("Android did not lock ${texts.logName}", e)
            false
        }
    }

    private fun clearState() {
        until = null
        follower?.invoke(null)
        if (file.exists() && !file.delete()) log("Couldn't delete the free time of ${texts.logName}", null)
    }

    private fun save(end: Long) {
        try {
            file.absoluteFile.parentFile?.mkdirs()
            writeAtomically(file, StoredText.encode(kind, listOf("until" to end.toString())))
        } catch (e: IOException) {
            // Until the agent starts again, the time still runs out by the watchdog.
            log("Couldn't save the free time of ${texts.logName}", e)
        }
    }

    private fun load(): Long? {
        if (!file.exists()) return null
        return try {
            StoredText.decode(kind, file.readBytes())?.first("until")?.toLongOrNull()
        } catch (e: IOException) {
            log("Couldn't read the free time of ${texts.logName}", e)
            null
        }
    }

    companion object {
        const val MINUTE_MILLIS = 60_000L
        const val MAX_MILLIS = PhoneNetworkFree.MAX_MINUTES * MINUTE_MILLIS

        const val NOT_PREPARED = "Este celular não foi preparado pelo Midview."
        const val INVALID_REQUEST = "pedido inválido"
    }
}
