package io.github.arnoneto1.midview.agent.sites

import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.MessageType
import java.io.File
import java.io.IOException

// What the teacher blocked on this phone: sites (normalized names) and
// whether the internet as a whole is blocked.
data class SiteBlockState(val domains: Set<String>, val internetBlocked: Boolean) {
    fun encode(): ByteArray =
        (listOf("internet=${if (internetBlocked) 1 else 0}") + domains.sorted())
            .joinToString("") { "$it\n" }
            .toByteArray(Charsets.UTF_8)

    companion object {
        val EMPTY = SiteBlockState(emptySet(), false)

        // Null for anything this agent did not write.
        fun decode(bytes: ByteArray): SiteBlockState? {
            val lines = String(bytes, Charsets.UTF_8).split('\n').dropLastWhile { it.isEmpty() }
            val internet = when (lines.firstOrNull()) {
                "internet=0" -> false
                "internet=1" -> true
                else -> return null
            }
            val domains = lines.drop(1)
            if (domains.any { DomainNormalizer.normalize(it) != it }) return null
            return SiteBlockState(domains.toSet(), internet)
        }
    }
}

// The phone's side of the teacher's "Bloquear sites" window (BlockDomain,
// UnblockDomain, BlockInternet, UnblockInternet), as the Windows
// SiteBlockCommandHandler: the agent keeps the blocked set in the app's own
// storage, and apply makes the phone follow it (Chrome's policy and the
// internet block, SitePolicy). Each command answers true or false, which the
// teacher gets as the single byte of the CommandAck. A site is normalized as
// on a PC (DomainNormalizer); an invalid one is refused, and freeing a site
// that was not blocked is done. A state the phone could not apply or save is
// a failure: the previous one is applied again and stays.
class SiteBlockStore(
    private val file: File,
    private val apply: (SiteBlockState) -> Boolean,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    private val gate = Any()

    @Volatile private var state: SiteBlockState = load(file, log) ?: SiteBlockState.EMPTY

    val current: SiteBlockState get() = state

    fun handle(type: Int, payload: ByteArray): Boolean {
        synchronized(gate) { return change(type, payload) }
    }

    private fun change(type: Int, payload: ByteArray): Boolean {
        val held = state
        val next = when (type) {
            MessageType.BLOCK_INTERNET -> held.copy(internetBlocked = true)
            MessageType.UNBLOCK_INTERNET -> held.copy(internetBlocked = false)
            MessageType.BLOCK_DOMAIN -> {
                val domain = DomainNormalizer.normalize(String(payload, Charsets.UTF_8)) ?: return false
                if (domain !in held.domains && held.domains.size >= ChromePolicy.MAX_DOMAINS) {
                    log("Too many sites blocked already; $domain not added", null)
                    return false
                }
                held.copy(domains = held.domains + domain)
            }
            MessageType.UNBLOCK_DOMAIN -> {
                val domain = DomainNormalizer.normalize(String(payload, Charsets.UTF_8)) ?: return false
                held.copy(domains = held.domains - domain)
            }
            else -> return false
        }

        if (!apply(next)) {
            log("The site block could not be applied; keeping the previous one", null)
            apply(held)
            return false
        }
        try {
            file.absoluteFile.parentFile?.mkdirs()
            writeAtomically(file, next.encode())
        } catch (e: IOException) {
            log("Couldn't save the site block; keeping the previous one", e)
            apply(held)
            return false
        }
        state = next
        return true
    }

    // The held state applied again (the agent started, after the phone
    // restarted or MIUI ended it), under the same lock as a command.
    fun reapply(): Boolean = synchronized(gate) { apply(state) }

    private companion object {
        // Unreadable means nothing blocked, the side the teacher can always
        // fix remotely (as on Windows). A file with bad content is kept aside.
        fun load(file: File, log: (String, Throwable?) -> Unit): SiteBlockState? {
            if (!file.exists()) return null
            val bytes = try {
                file.readBytes()
            } catch (e: IOException) {
                log("Couldn't read the site block file, blocking nothing", e)
                return null
            }
            return SiteBlockState.decode(bytes) ?: run {
                log("Couldn't read the site block, blocking nothing", null)
                file.renameTo(File(file.path + ".corrupt"))
                null
            }
        }
    }
}
