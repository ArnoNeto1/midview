package io.github.arnoneto1.midview.agent.protocol

// The teacher's lock screen PIN for a school phone, as
// src/Midview.Protocol/PhoneScreenPassword.cs: the payload of
// SET_PHONE_SCREEN_PASSWORD is the PIN's ASCII digits, exactly 4 of them, or
// nothing (empty) to remove it.
object PhoneScreenPassword {
    const val PIN_LENGTH = 4

    // No PIN at all.
    const val NONE = ""

    // What the teacher reads for anything else.
    const val INVALID = "o PIN tem que ter 4 números"

    fun isValid(pin: String): Boolean =
        pin.isEmpty() || (pin.length == PIN_LENGTH && pin.all { it in '0'..'9' })

    fun encode(pin: String): ByteArray {
        require(isValid(pin)) { "invalid phone screen PIN" }
        return pin.toByteArray(Charsets.US_ASCII)
    }

    // Null for anything that is not exactly 4 ASCII digits or empty.
    fun decode(payload: ByteArray): String? {
        if (payload.isNotEmpty() && (payload.size != PIN_LENGTH || payload.any { it < '0'.code.toByte() || it > '9'.code.toByte() })) {
            return null
        }
        return String(payload, Charsets.US_ASCII)
    }
}
