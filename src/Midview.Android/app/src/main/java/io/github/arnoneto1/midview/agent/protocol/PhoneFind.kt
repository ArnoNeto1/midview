package io.github.arnoneto1.midview.agent.protocol

// "Tocar para encontrar", as src/Midview.Protocol/PhoneFind.cs: the payload
// of FIND_PHONE is two bytes, big-endian, the seconds the device rings (1 to
// 300), or 0 to stop it ringing.
object PhoneFind {
    const val STOP = 0
    const val DEFAULT_SECONDS = 60
    const val MAX_SECONDS = 5 * 60

    fun isValid(seconds: Int): Boolean = seconds in STOP..MAX_SECONDS

    fun encode(seconds: Int): ByteArray {
        require(isValid(seconds)) { "from 0 to 300 seconds" }
        return byteArrayOf((seconds shr 8).toByte(), seconds.toByte())
    }

    // Null for anything but two bytes of 0 to 300.
    fun decode(payload: ByteArray): Int? {
        if (payload.size != 2) return null
        val seconds = ((payload[0].toInt() and 0xFF) shl 8) or (payload[1].toInt() and 0xFF)
        return if (isValid(seconds)) seconds else null
    }
}
