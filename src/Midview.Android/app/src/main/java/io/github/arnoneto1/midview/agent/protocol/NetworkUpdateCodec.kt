package io.github.arnoneto1.midview.agent.protocol

import java.nio.ByteBuffer
import java.time.Instant

// UpdateOffer as in src/Midview.Protocol/NetworkUpdate.cs.
class UpdateOffer(
    val attemptId: ByteArray,
    val fingerprint: ByteArray,
    val zipSize: Long,
    val zipSha256: ByteArray,
    val uncompressedSize: Long,
)

// One update attempt as the agent records it (UpdateAttemptReport in C#):
// id in hex, a state (receiving, verifying, applying, committed, failed),
// a detail text for the teacher and when it last changed.
data class UpdateAttemptReport(val id: String, val state: String, val detail: String, val updatedAt: Instant)

// Payload of UPDATE_STATUS (UpdateStatusReport in C#). For a phone,
// fingerprint is the SHA-256 hex of the installed APK and build its versionName.
data class UpdateStatusReport(
    val fingerprint: String?,
    val build: String?,
    val ready: Boolean,
    val attempt: UpdateAttemptReport?,
)

object NetworkUpdateCodec {
    const val ATTEMPT_ID_LENGTH = 16
    const val HASH_LENGTH = 32
    const val OFFER_LENGTH = ATTEMPT_ID_LENGTH + HASH_LENGTH + 8 + HASH_LENGTH + 8

    // UPDATE_OFFER payload (96 bytes, big-endian): attempt id, fingerprint,
    // zip size, zip SHA-256, uncompressed size. Null for a wrong length or a
    // size that is not positive.
    fun decodeOffer(payload: ByteArray): UpdateOffer? {
        if (payload.size != OFFER_LENGTH) {
            return null
        }
        val buffer = ByteBuffer.wrap(payload)
        val zipSize = buffer.getLong(ATTEMPT_ID_LENGTH + HASH_LENGTH)
        val uncompressed = buffer.getLong(ATTEMPT_ID_LENGTH + 2 * HASH_LENGTH + 8)
        if (zipSize <= 0 || uncompressed <= 0) {
            return null
        }
        return UpdateOffer(
            payload.copyOfRange(0, ATTEMPT_ID_LENGTH),
            payload.copyOfRange(ATTEMPT_ID_LENGTH, ATTEMPT_ID_LENGTH + HASH_LENGTH),
            zipSize,
            payload.copyOfRange(ATTEMPT_ID_LENGTH + HASH_LENGTH + 8, ATTEMPT_ID_LENGTH + 2 * HASH_LENGTH + 8),
            uncompressed,
        )
    }

    // The JSON the Master's UpdateStatusReport.TryDecode reads, in the same
    // field order and shape System.Text.Json writes.
    fun encodeStatus(report: UpdateStatusReport): ByteArray {
        val attempt = report.attempt?.let {
            JsonText.obj(
                "id" to JsonText.string(it.id),
                "state" to JsonText.string(it.state),
                "detail" to JsonText.string(it.detail),
                "updatedAt" to JsonText.string(IsoTime.format(it.updatedAt)),
            )
        } ?: "null"
        return JsonText.obj(
            "fingerprint" to JsonText.string(report.fingerprint),
            "build" to JsonText.string(report.build),
            "ready" to report.ready.toString(),
            "attempt" to attempt,
        ).toByteArray(Charsets.UTF_8)
    }
}
