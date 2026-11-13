package io.github.arnoneto1.midview.agent.protocol

import java.security.MessageDigest
import javax.crypto.Mac
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.PBEKeySpec
import javax.crypto.spec.SecretKeySpec

// The code of "Permitir novo professor", exactly as
// src/Midview.Protocol/NewTeacherInvite.cs (see there): AllowNewTeacher
// carries it (8 ASCII characters of Crockford's Base32; empty from a Master
// from before it), and a new teacher's PairConfirm carries the proof:
//   key   = PBKDF2-HMAC-SHA256(code, label || agent thumbprint, ITERATIONS, 32)
//   proof = HMAC-SHA256(key, label || master thumbprint || agent thumbprint || agent nonce || master nonce)
// Thumbprints trimmed, upper case, ASCII.
object NewTeacherInvite {
    const val CODE_LENGTH = 8
    const val PROOF_LENGTH = 32
    const val ITERATIONS = 100_000

    // Pairings that bring a code to the screen without a valid proof before
    // the window closes.
    const val MAX_TRIES = 5

    // How a device that knows the code says its window is open.
    const val WITH_CODE_SUFFIX = " com o código"

    const val ALPHABET = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"

    private const val LABEL = "Midview novo professor v1"

    fun isCode(code: String): Boolean = code.length == CODE_LENGTH && code.all { it in ALPHABET }

    // The code in an AllowNewTeacher, or null (empty: an old Master).
    fun decode(payload: ByteArray): String? {
        if (payload.size != CODE_LENGTH) return null
        val code = String(payload, Charsets.US_ASCII)
        return if (isCode(code)) code else null
    }

    fun withCode(untilText: String): String = untilText + WITH_CODE_SUFFIX

    fun key(code: String, agentThumb: String): ByteArray {
        // The code is ASCII: how a password's characters become bytes makes
        // no difference to it.
        val spec = PBEKeySpec(code.toCharArray(), (LABEL + thumb(agentThumb)).toByteArray(Charsets.US_ASCII), ITERATIONS, PROOF_LENGTH * 8)
        try {
            return SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256").generateSecret(spec).encoded
        } finally {
            spec.clearPassword()
        }
    }

    fun proof(key: ByteArray, masterThumb: String, agentThumb: String, agentNonce: ByteArray, masterNonce: ByteArray): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(key, "HmacSHA256"))
        mac.update((LABEL + thumb(masterThumb) + thumb(agentThumb)).toByteArray(Charsets.US_ASCII))
        mac.update(agentNonce)
        mac.update(masterNonce)
        return mac.doFinal()
    }

    fun verifies(
        code: String, proof: ByteArray, masterThumb: String, agentThumb: String, agentNonce: ByteArray, masterNonce: ByteArray,
    ): Boolean =
        proof.size == PROOF_LENGTH &&
            MessageDigest.isEqual(proof, proof(key(code, agentThumb), masterThumb, agentThumb, agentNonce, masterNonce))

    private fun thumb(thumbprint: String) = thumbprint.trim().uppercase()
}
