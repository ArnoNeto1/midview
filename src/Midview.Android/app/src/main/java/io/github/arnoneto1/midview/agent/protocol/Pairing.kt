package io.github.arnoneto1.midview.agent.protocol

import java.security.MessageDigest
import java.security.SecureRandom
import java.util.Locale

// How a teacher and this device come to trust each other, exactly as
// src/Midview.Protocol/Pairing.cs (see there for the whole exchange):
//   Master -> PairRequest  the teacher's computer name
//   Agent  -> PairCommit   SHA-256 of the agent's random nonce, or PairRefused
//   Master -> PairNonce    the Master's random nonce
//   Agent  -> PairReveal   the agent's nonce; the code is on the screen now
//   Master -> PairConfirm or PairCancel, once the teacher compared the codes
//   Agent  -> PairDone     the device, for the classroom list, or PairRefused
// Both ends compute the same 6-digit code from both certificates and both
// nonces; a computer in the middle makes the two screens differ.
object Pairing {
    const val NONCE_LENGTH = 32
    const val MAX_TEACHER_NAME_LENGTH = 40

    // How long the teacher has to compare and confirm, from the reveal.
    const val CONFIRM_TIMEOUT_MILLIS = 5 * 60 * 1000L

    // How long "Permitir novo professor" lets one more teacher pair.
    const val NEW_TEACHER_WINDOW_MILLIS = 10 * 60 * 1000L

    private val LABEL = "Midview pairing v1".toByteArray(Charsets.US_ASCII)

    private val random = SecureRandom()

    fun newNonce(): ByteArray = ByteArray(NONCE_LENGTH).also { random.nextBytes(it) }

    fun commit(nonce: ByteArray): ByteArray = sha256(nonce)

    fun opens(commitment: ByteArray, nonce: ByteArray): Boolean =
        nonce.size == NONCE_LENGTH && commitment.size == 32 && MessageDigest.isEqual(commitment, commit(nonce))

    // SHA-256 of the label, both thumbprints (trimmed, upper case, ASCII) and
    // both nonces; the first 4 bytes, big-endian and unsigned, modulo 1 000 000.
    fun code(masterThumb: String, agentThumb: String, agentNonce: ByteArray, masterNonce: ByteArray): Int {
        val digest = MessageDigest.getInstance("SHA-256")
        digest.update(LABEL)
        digest.update(masterThumb.trim().uppercase().toByteArray(Charsets.US_ASCII))
        digest.update(agentThumb.trim().uppercase().toByteArray(Charsets.US_ASCII))
        digest.update(agentNonce)
        digest.update(masterNonce)
        val hash = digest.digest()
        val first = ((hash[0].toLong() and 0xFF) shl 24) or
            ((hash[1].toLong() and 0xFF) shl 16) or
            ((hash[2].toLong() and 0xFF) shl 8) or
            (hash[3].toLong() and 0xFF)
        return (first % 1_000_000).toInt()
    }

    fun format(code: Int): String = String.format(Locale.ROOT, "%03d %03d", code / 1000, code % 1000)

    // The teacher's computer name as shown on the device: letters, digits,
    // '-', '_', '.' and spaces only, never longer than
    // MAX_TEACHER_NAME_LENGTH, and "?" if nothing is left.
    fun cleanTeacherName(name: String): String {
        var kept = name.filter { it.isLetterOrDigit() || it == '-' || it == '_' || it == '.' || it == ' ' }.trim()
        if (kept.length > MAX_TEACHER_NAME_LENGTH) {
            kept = kept.substring(0, MAX_TEACHER_NAME_LENGTH).trimEnd()
        }
        return kept.ifEmpty { "?" }
    }

    fun refused(reason: String) = Message(MessageType.PAIR_REFUSED, reason.toByteArray(Charsets.UTF_8))

    fun refusalText(payload: ByteArray) = String(payload, Charsets.UTF_8)

    // PairDone's payload, .NET's PairingResult: the device as the classroom
    // list keeps it, always with "kind":"android". mac ("AA-BB-CC-DD-EE-FF")
    // is null when the device can't tell. .NET's PairingResult.Decode drops a
    // result without a name or a session, so none is ever sent.
    fun encodeResult(name: String, sessionThumbprint: String, mac: String?): ByteArray {
        require(name.isNotBlank()) { "A pairing result needs the device's name" }
        require(sessionThumbprint.isNotBlank()) { "A pairing result needs the session's thumbprint" }
        return JsonText.obj(
            "name" to JsonText.string(name),
            "session" to JsonText.string(sessionThumbprint),
            "mac" to JsonText.string(mac),
            "kind" to JsonText.string(Discovery.ANDROID_KIND),
        ).toByteArray(Charsets.UTF_8)
    }

    private fun sha256(bytes: ByteArray): ByteArray = MessageDigest.getInstance("SHA-256").digest(bytes)
}
