package io.github.arnoneto1.midview.agent.protocol

import java.nio.ByteBuffer

// The "Painel do laboratório" as a phone keeps it: of the PC's LabConfig
// (src/Midview.Protocol/LabConfig.cs) only the two functions a phone has,
// "Travar downloads" (Chrome's DownloadRestrictions) and "Bloqueador de
// anúncios" (the ad-free DNS of "Otimizar"). The payload of SET_LAB_CONFIG
// and LAB_CONFIG_STATUS is the PC's, big-endian: format version (1 byte),
// savedAt (int64, Unix seconds on the teacher's PC; the newest wins), flags
// (1 byte). The PC-only flags are never sent to a phone by the teacher's
// program; one that comes anyway is left out, and the phone always answers
// with them off.
data class LabConfig(val savedAt: Long, val blockDownloads: Boolean, val adblock: Boolean) {
    fun encode(): ByteArray =
        ByteBuffer.allocate(PAYLOAD_LENGTH)
            .put(FORMAT_VERSION)
            .putLong(savedAt)
            .put(flags().toByte())
            .array()

    // Same functions on, in any savedAt.
    fun sameFunctionsAs(other: LabConfig): Boolean = flags() == other.flags()

    private fun flags(): Int =
        (if (blockDownloads) FLAG_BLOCK_DOWNLOADS else 0) or (if (adblock) FLAG_ADBLOCK else 0)

    companion object {
        const val FORMAT_VERSION: Byte = 1
        const val PAYLOAD_LENGTH = 10

        // The PC's flag bits (LabConfig.Flag).
        const val FLAG_BLOCK_DOWNLOADS = 4
        const val FLAG_ADBLOCK = 16

        // What a phone has before the teacher ever used the panel on it:
        // downloads free and the ad-free DNS on, as "Otimizar" leaves it.
        val DEFAULT = LabConfig(0, blockDownloads = false, adblock = true)

        // Null for anything that is not this format.
        fun decode(payload: ByteArray): LabConfig? {
            if (payload.size != PAYLOAD_LENGTH || payload[0] != FORMAT_VERSION) return null
            val buffer = ByteBuffer.wrap(payload, 1, PAYLOAD_LENGTH - 1)
            val savedAt = buffer.getLong()
            val flags = buffer.get().toInt() and 0xFF
            return LabConfig(savedAt, flags and FLAG_BLOCK_DOWNLOADS != 0, flags and FLAG_ADBLOCK != 0)
        }
    }
}
