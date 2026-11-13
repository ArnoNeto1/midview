package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.identity.StoredText
import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.IsoTime
import io.github.arnoneto1.midview.agent.protocol.PhoneDnsStates
import io.github.arnoneto1.midview.agent.protocol.PhoneOptimizeReport
import java.io.File
import java.io.IOException
import java.time.Duration
import java.time.Instant

// The phone's optimization as last done: the report the teacher gets (what
// the optimizer uninstalled and hid, what Android refused, how the DNS stands) and when the
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

        private const val KIND = "optimize"

        // The phone's own format (StoredText "optimize"), not the report's
        // network form: lastDnsAttempt and optimizedAt (ISO-8601, empty for
        // none), a "hidden=", "failed=" and "uninstalled=" per app, dns and
        // dnsHost. A file from before 0.0.74 has no "uninstalled=": none.
        // The report's pending is not kept: after a restart nothing is being
        // uninstalled in the background any more, so it reads as 0.
        fun encode(state: OptimizeState): ByteArray {
            val report = state.report
            return StoredText.encode(
                KIND,
                listOf(
                    "lastDnsAttempt" to (state.lastDnsAttempt?.let { IsoTime.format(it) } ?: ""),
                    "optimizedAt" to (report.optimizedAt?.let { IsoTime.format(it) } ?: ""),
                ) + report.hidden.map { "hidden" to it } + report.failed.map { "failed" to it } +
                    report.uninstalled.map { "uninstalled" to it } +
                    listOf("dns" to report.dns, "dnsHost" to report.dnsHost),
            )
        }

        // Null for a file that is neither the phone's format nor that of
        // 0.0.70 and before. Read tolerantly: a time it cannot read is none,
        // a DNS state it does not know is "not on".
        fun decode(bytes: ByteArray): OptimizeState? {
            val stored = StoredText.decode(KIND, bytes) ?: return decodeOld(bytes)
            fun time(key: String) = stored.first(key)?.takeIf { it.isNotEmpty() }?.let { IsoTime.parse(it) }
            val dns = stored.first("dns")?.takeIf { PhoneDnsStates.isKnown(it) } ?: PhoneDnsStates.OFF_ERROR
            val report = PhoneOptimizeReport(
                time("optimizedAt"), stored.all("hidden"), stored.all("failed"), dns, stored.first("dnsHost") ?: AdblockDns.HOST,
                stored.all("uninstalled"),
            )
            return OptimizeState(report, time("lastDnsAttempt"))
        }

        // 0.0.70 and before: "lastDnsAttempt=<ISO-8601 or nothing>", a line
        // break, then the report as the teacher gets it.
        private fun decodeOld(bytes: ByteArray): OptimizeState? {
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
