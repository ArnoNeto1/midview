package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.IsoTime
import io.github.arnoneto1.midview.agent.protocol.PhoneDnsStates
import io.github.arnoneto1.midview.agent.protocol.PhoneOptimizeReport
import java.io.File
import java.io.IOException
import java.time.Duration
import java.time.Instant

// The phone's optimization as last done: the report the teacher gets (what
// the optimizer hid, what Android refused, how the DNS stands) and when the
// DNS was last tried, for the retry at most every 30 minutes.
data class OptimizeState(val report: PhoneOptimizeReport, val lastDnsAttempt: Instant?)

// OptimizeState kept in the app's own storage (written atomically), so a
// restart neither runs the first optimization again nor loses when to retry
// the DNS.
class OptimizeStore(
    private val file: File,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    private val gate = Any()

    @Volatile private var state: OptimizeState = load(file, log) ?: NEVER

    val current: OptimizeState get() = state

    // Kept and written to file; false when the disk refused, though what the
    // phone did still counts until the agent starts again.
    fun save(next: OptimizeState): Boolean = synchronized(gate) {
        state = next
        try {
            file.absoluteFile.parentFile?.mkdirs()
            writeAtomically(file, encode(next))
            true
        } catch (e: IOException) {
            log("Couldn't save the optimization", e)
            false
        }
    }

    companion object {
        val RETRY_INTERVAL: Duration = Duration.ofMinutes(30)

        // Never optimized: no report yet (its DNS state just means "not on").
        val NEVER = OptimizeState(
            PhoneOptimizeReport(null, emptyList(), emptyList(), PhoneDnsStates.OFF_ERROR, AdblockDns.HOST),
            lastDnsAttempt = null,
        )

        // Whether a DNS left off may be tried again: never tried, 30 minutes
        // past, or a clock set back since (that must not hold it off).
        fun retryDue(now: Instant, lastDnsAttempt: Instant?): Boolean =
            lastDnsAttempt == null || now.isBefore(lastDnsAttempt) || !now.isBefore(lastDnsAttempt.plus(RETRY_INTERVAL))

        // "lastDnsAttempt=<ISO-8601 or nothing>", a line break, then the
        // report as the teacher gets it.
        fun encode(state: OptimizeState): ByteArray =
            (ATTEMPT + (state.lastDnsAttempt?.let { IsoTime.format(it) } ?: "") + "\n").toByteArray(Charsets.UTF_8) +
                state.report.encode()

        // Null for anything this agent did not write.
        fun decode(bytes: ByteArray): OptimizeState? {
            val end = bytes.indexOf('\n'.code.toByte())
            if (end < 0) return null
            val header = String(bytes, 0, end, Charsets.UTF_8)
            if (!header.startsWith(ATTEMPT)) return null
            val attemptText = header.removePrefix(ATTEMPT)
            val attempt = if (attemptText.isEmpty()) null else IsoTime.parse(attemptText) ?: return null
            val report = PhoneOptimizeReport.decode(bytes.copyOfRange(end + 1, bytes.size)) ?: return null
            return OptimizeState(report, attempt)
        }

        private const val ATTEMPT = "lastDnsAttempt="

        // Unreadable means never optimized: the first optimization runs again
        // and changes nothing already done. A file with bad content is kept
        // aside; one that could not be read is left for the next start.
        private fun load(file: File, log: (String, Throwable?) -> Unit): OptimizeState? {
            if (!file.exists()) return null
            val bytes = try {
                file.readBytes()
            } catch (e: IOException) {
                log("Couldn't read the optimization file", e)
                return null
            }
            return decode(bytes) ?: run {
                log("Couldn't read the optimization; starting over", null)
                file.renameTo(File(file.path + ".corrupt"))
                null
            }
        }
    }
}
