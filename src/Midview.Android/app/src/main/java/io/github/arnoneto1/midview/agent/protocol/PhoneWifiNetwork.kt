package io.github.arnoneto1.midview.agent.protocol

import java.nio.ByteBuffer
import java.nio.charset.CharacterCodingException
import java.nio.charset.CodingErrorAction

// "Rede Wi-Fi nova da escola", as src/Midview.Protocol/PhoneWifiNetwork.cs:
// the payload of ADD_PHONE_WIFI is the network's name (1 byte of length,
// then 1 to 32 bytes of UTF-8) and its password (1 byte of length, then none
// for an open network, or 8 to 63 printable ASCII characters).
object PhoneWifiNetwork {
    const val MAX_NAME_BYTES = 32
    const val MIN_PASSWORD = 8
    const val MAX_PASSWORD = 63

    // The name and the password ("" for an open network).
    data class Network(val name: String, val password: String)

    fun isValid(name: String, password: String): Boolean =
        name.isNotBlank() && name.toByteArray(Charsets.UTF_8).size <= MAX_NAME_BYTES &&
            (password.isEmpty() || (password.length in MIN_PASSWORD..MAX_PASSWORD && password.all { it in ' '..'~' }))

    fun encode(name: String, password: String): ByteArray {
        require(isValid(name, password)) { "not a network the Master would send" }
        val nameBytes = name.toByteArray(Charsets.UTF_8)
        val passwordBytes = password.toByteArray(Charsets.US_ASCII)
        return byteArrayOf(nameBytes.size.toByte()) + nameBytes + byteArrayOf(passwordBytes.size.toByte()) + passwordBytes
    }

    // Null for a payload that is not one name and one password by the rules.
    fun decode(payload: ByteArray): Network? {
        if (payload.size < 2) return null
        val nameLength = payload[0].toInt() and 0xFF
        if (nameLength == 0 || payload.size < 2 + nameLength) return null
        val passwordLength = payload[1 + nameLength].toInt() and 0xFF
        if (payload.size != 2 + nameLength + passwordLength) return null
        val name = try {
            Charsets.UTF_8.newDecoder()
                .onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT)
                .decode(ByteBuffer.wrap(payload, 1, nameLength)).toString()
        } catch (e: CharacterCodingException) {
            return null
        }
        val password = String(payload, 2 + nameLength, passwordLength, Charsets.US_ASCII)
        return if (isValid(name, password)) Network(name, password) else null
    }
}
