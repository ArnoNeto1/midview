package io.github.arnoneto1.midview.agent.protocol

// "Liberar Wi-Fi e modo avião", as src/Midview.Protocol/PhoneNetworkFree.cs:
// the payload of FREE_PHONE_NETWORK is one byte, the minutes (1 to 60) the
// student may change the Wi-Fi and airplane mode, or 0 to lock them again now.
object PhoneNetworkFree {
    const val LOCK_NOW = 0
    const val MAX_MINUTES = 60

    fun isValid(minutes: Int): Boolean = minutes in LOCK_NOW..MAX_MINUTES

    fun encode(minutes: Int): ByteArray {
        require(isValid(minutes)) { "from 0 to 60 minutes" }
        return byteArrayOf(minutes.toByte())
    }

    // Null for anything but one byte of 0 to 60.
    fun decode(payload: ByteArray): Int? {
        if (payload.size != 1) return null
        val minutes = payload[0].toInt() and 0xFF
        return if (minutes <= MAX_MINUTES) minutes else null
    }
}
