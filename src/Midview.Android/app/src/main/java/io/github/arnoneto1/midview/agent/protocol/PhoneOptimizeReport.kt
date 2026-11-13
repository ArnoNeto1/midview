package io.github.arnoneto1.midview.agent.protocol

import org.json.JSONArray
import org.json.JSONObject
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.time.format.DateTimeParseException
import java.util.Locale

// How the ad-free DNS stands on the phone, as src/Midview.Protocol/PhoneOptimizeReport.cs.
object PhoneDnsStates {
    const val ON = "on"
    const val OFF_NO_INTERNET = "offNoInternet"
    const val OFF_NETWORK_BLOCKS = "offNetworkBlocks"
    const val OFF_INTERNET_BLOCKED = "offInternetBlocked"
    const val OFF_ERROR = "offError"

    // Turned off by the teacher's "Painel do laboratório" (Bloqueador de
    // anúncios desativado): "Otimizar" leaves it off until the panel turns it on.
    const val OFF_BY_TEACHER = "offByTeacher"

    fun isKnown(state: String?): Boolean =
        state == ON || state == OFF_NO_INTERNET || state == OFF_NETWORK_BLOCKS ||
            state == OFF_INTERNET_BLOCKED || state == OFF_ERROR || state == OFF_BY_TEACHER
}

// Payload of PHONE_OPTIMIZE_REPORT (UTF-8 JSON), the same text as the .NET
// PhoneOptimizeReport: when the phone was last optimized (null if never),
// the packages hidden and those neither hidden nor uninstalled, the DNS state,
// the DNS server's host name, the packages uninstalled ("uninstalled", since
// 0.0.74) and how many listed apps are still being uninstalled in the
// background ("pending", since 0.0.75). Both are written last; a report
// without them, or with null, has none; older Masters ignore them. pending is
// a whole number, never negative.
data class PhoneOptimizeReport(
    val optimizedAt: Instant?,
    val hidden: List<String>,
    val failed: List<String>,
    val dns: String,
    val dnsHost: String,
    val uninstalled: List<String> = emptyList(),
    val pending: Int = 0,
) {
    // Refuses (IllegalArgumentException) a DNS state the protocol does not have.
    fun encode(): ByteArray {
        require(PhoneDnsStates.isKnown(dns)) { "unknown dns state" }
        require(pending >= 0) { "negative pending" }
        return JsonText.obj(
            "optimizedAt" to JsonText.string(optimizedAt?.let { IsoTime.format(it) }),
            "hidden" to hidden.joinToString(",", "[", "]") { JsonText.string(it) },
            "failed" to failed.joinToString(",", "[", "]") { JsonText.string(it) },
            "dns" to JsonText.string(dns),
            "dnsHost" to JsonText.string(dnsHost),
            "uninstalled" to uninstalled.joinToString(",", "[", "]") { JsonText.string(it) },
            "pending" to pending.toString(),
        ).toByteArray(Charsets.UTF_8)
    }

    companion object {
        // Null for anything that is not exactly a valid report.
        fun decode(payload: ByteArray): PhoneOptimizeReport? {
            val text = strictUtf8(payload, 0, payload.size) ?: return null
            val json = JsonText.parseObject(text) ?: return null

            val optimizedAt = when (val raw = if (json.has("optimizedAt")) json.get("optimizedAt") else JSONObject.NULL) {
                JSONObject.NULL -> null
                is String -> IsoTime.parse(raw) ?: return null
                else -> return null
            }
            val hidden = strings(json, "hidden") ?: return null
            val failed = strings(json, "failed") ?: return null
            val dns = json.opt("dns") as? String ?: return null
            val dnsHost = json.opt("dnsHost") as? String ?: return null
            if (!PhoneDnsStates.isKnown(dns)) {
                return null
            }
            val uninstalled = if (json.isNull("uninstalled")) emptyList() else strings(json, "uninstalled") ?: return null
            val pending = when (val raw = if (json.has("pending")) json.get("pending") else JSONObject.NULL) {
                JSONObject.NULL -> 0
                is Int -> if (raw >= 0) raw else return null
                else -> return null
            }
            return PhoneOptimizeReport(optimizedAt, hidden, failed, dns, dnsHost, uninstalled, pending)
        }

        private fun strings(json: JSONObject, name: String): List<String>? {
            val array = json.opt(name) as? JSONArray ?: return null
            return List(array.length()) { array.opt(it) as? String ?: return null }
        }
    }
}

// Updating a phone's agent reuses UPDATE_OFFER and the other update messages:
// fingerprint = zipSha256 = the SHA-256 of the APK, zipSize = uncompressedSize
// = the APK's size in bytes (see NetworkUpdateCodec).
object PhoneUpdate {
    const val MAX_APK_SIZE = 64L * 1024 * 1024
}

// ISO-8601 with an offset the way System.Text.Json writes a DateTimeOffset:
// seconds, then the fraction only when there is one (trailing zeros dropped,
// at most 7 digits), then the offset (+00:00 for UTC).
internal object IsoTime {
    fun format(instant: Instant): String {
        val z = instant.atOffset(ZoneOffset.UTC)
        val base = String.format(Locale.ROOT, "%04d-%02d-%02dT%02d:%02d:%02d", z.year, z.monthValue, z.dayOfMonth, z.hour, z.minute, z.second)
        val fraction = String.format(Locale.ROOT, "%09d", z.nano).substring(0, 7).trimEnd('0')
        return base + (if (fraction.isEmpty()) "" else ".$fraction") + "+00:00"
    }

    fun parse(text: String): Instant? =
        try {
            OffsetDateTime.parse(text).toInstant()
        } catch (_: DateTimeParseException) {
            null
        }
}
