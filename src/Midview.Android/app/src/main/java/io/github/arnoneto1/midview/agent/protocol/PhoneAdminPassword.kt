package io.github.arnoneto1.midview.agent.protocol

import java.nio.ByteBuffer
import java.security.MessageDigest
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

// The phones' admin password, as src/Midview.Protocol/PhoneAdminPassword.cs:
// the payload of SET_PHONE_ADMIN_PASSWORD is a version byte (1), the
// PBKDF2-HMAC-SHA256 iterations (uint32, big-endian), a 16-byte salt and the
// 32-byte hash of the password's UTF-8 bytes; empty removes the password. The
// password itself (6 to 12 ASCII letters or digits) never comes over the
// network: the phone keeps the hash and checks what is typed against it.
object PhoneAdminPassword {
    const val MIN_LENGTH = 6
    const val MAX_LENGTH = 12
    const val VERSION: Byte = 1
    const val SALT_LENGTH = 16
    const val HASH_LENGTH = 32
    const val MIN_ITERATIONS = 10_000
    const val MAX_ITERATIONS = 1_000_000
    const val PAYLOAD_LENGTH = 1 + 4 + SALT_LENGTH + HASH_LENGTH

    // What the teacher reads for anything else.
    const val INVALID = "a senha tem que ter de 6 a 12 letras ou números"

    class Stored(val iterations: Int, val salt: ByteArray, val hash: ByteArray)

    fun isAllowed(c: Char): Boolean = c in '0'..'9' || c in 'a'..'z' || c in 'A'..'Z'

    fun isValid(password: String): Boolean = password.length in MIN_LENGTH..MAX_LENGTH && password.all(::isAllowed)

    // As the Master does it (for the tests and the phone's own checks).
    fun encode(password: String, salt: ByteArray, iterations: Int): ByteArray {
        require(isValid(password)) { "invalid admin password" }
        require(salt.size == SALT_LENGTH && iterations in MIN_ITERATIONS..MAX_ITERATIONS) { "salt or iterations out of range" }
        return ByteBuffer.allocate(PAYLOAD_LENGTH)
            .put(VERSION).putInt(iterations).put(salt).put(hash(password, salt, iterations))
            .array()
    }

    // Null for an empty payload (no password) or anything this version cannot use.
    fun decode(payload: ByteArray): Stored? {
        if (payload.size != PAYLOAD_LENGTH || payload[0] != VERSION) return null
        val iterations = ByteBuffer.wrap(payload, 1, 4).int
        if (iterations !in MIN_ITERATIONS..MAX_ITERATIONS) return null
        return Stored(
            iterations,
            payload.copyOfRange(5, 5 + SALT_LENGTH),
            payload.copyOfRange(5 + SALT_LENGTH, PAYLOAD_LENGTH),
        )
    }

    // Constant time over the hash; a password that could never be valid is
    // refused without hashing.
    fun verify(password: String, stored: Stored): Boolean =
        isValid(password) && MessageDigest.isEqual(hash(password, stored.salt, stored.iterations), stored.hash)

    // PBKDF2-HMAC-SHA256 (RFC 8018), the 32 bytes of its first block, over
    // the password's UTF-8 bytes: written out so it is the same on every
    // Android version and on the JVM (the providers' PBKDF2 differ in how
    // they turn the password's chars into bytes).
    fun hash(password: String, salt: ByteArray, iterations: Int): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(password.toByteArray(Charsets.UTF_8), "HmacSHA256"))
        mac.update(salt)
        var u = mac.doFinal(byteArrayOf(0, 0, 0, 1))
        val result = u.copyOf()
        repeat(iterations - 1) {
            u = mac.doFinal(u)
            for (i in result.indices) result[i] = (result[i].toInt() xor u[i].toInt()).toByte()
        }
        return result
    }
}
