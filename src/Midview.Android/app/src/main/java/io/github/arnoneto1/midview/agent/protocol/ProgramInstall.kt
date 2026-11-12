package io.github.arnoneto1.midview.agent.protocol

import io.github.arnoneto1.midview.agent.files.FileNames
import java.nio.ByteBuffer
import java.nio.charset.CharacterCodingException
import java.nio.charset.CodingErrorAction

// INSTALL_PROGRAM's request, as src/Midview.Protocol/ProgramInstall.cs: the
// file's size, the installer's arguments (none on a phone) and its name.
data class InstallRequest(val fileName: String, val size: Long, val arguments: String)

// "Instalar programa" and "Desinstalar programa" on the wire, byte for byte
// as src/Midview.Protocol/ProgramInstall.cs and ProgramList.cs.
object ProgramInstall {
    // A PC's Agent takes up to 4 GB; a phone, up to MAX_APK_SIZE.
    const val MAX_FILE_SIZE = 4L shl 30
    const val MAX_ARGUMENTS_BYTES = 2048

    // The biggest app a teacher installs on the phones: ProgramInstall.MaxApkSize.
    const val MAX_APK_SIZE = 200L * 1024 * 1024

    // INSTALL_PROGRAM's payload: the size (int64, big-endian), the
    // arguments' length (uint16, big-endian), the arguments (UTF-8), then the
    // file name (UTF-8). Null for anything a PC's Agent would refuse too: the
    // name must already be a plain, safe file name, the arguments one line of
    // text. Whether it is an APK is the caller's to say (with its own words).
    fun decodeRequest(payload: ByteArray): InstallRequest? {
        if (payload.size < 10) return null
        val buffer = ByteBuffer.wrap(payload)
        val size = buffer.getLong(0)
        val argumentsLength = buffer.getShort(8).toInt() and 0xFFFF
        val nameLength = payload.size - 10 - argumentsLength
        if (size <= 0 || size > MAX_FILE_SIZE || argumentsLength > MAX_ARGUMENTS_BYTES ||
            nameLength <= 0 || nameLength > FileTransfer.MAX_NAME_BYTES
        ) {
            return null
        }
        val arguments = strict(payload, 10, argumentsLength) ?: return null
        val name = strict(payload, 10 + argumentsLength, nameLength) ?: return null
        if (arguments.any { Character.isISOControl(it) } || FileNames.safe(name) != name) return null
        return InstallRequest(name, size, arguments.trim())
    }

    fun isApkName(name: String): Boolean = name.endsWith(".apk", ignoreCase = true)

    // A ProgramList (UNINSTALL_PROGRAMS): format version (1), count (uint16,
    // big-endian), then each name as a length byte and its UTF-8 bytes. Null
    // for anything that is not exactly a valid list.
    fun decodeList(payload: ByteArray): List<String>? {
        if (payload.size < 3 || payload[0].toInt() != LIST_FORMAT) return null
        val count = ((payload[1].toInt() and 0xFF) shl 8) or (payload[2].toInt() and 0xFF)
        if (count > MAX_PROGRAMS) return null
        val names = ArrayList<String>(count)
        var offset = 3
        repeat(count) {
            if (offset >= payload.size) return null
            val length = payload[offset++].toInt() and 0xFF
            if (offset + length > payload.size) return null
            names += strict(payload, offset, length) ?: return null
            offset += length
        }
        return if (offset == payload.size) names else null
    }

    fun encodeList(names: List<String>): ByteArray {
        val bytes = names.map { it.toByteArray(Charsets.UTF_8) }
        require(bytes.size <= MAX_PROGRAMS && bytes.all { it.size <= MAX_NAME_LENGTH })
        val out = java.io.ByteArrayOutputStream()
        out.write(LIST_FORMAT)
        out.write(bytes.size ushr 8)
        out.write(bytes.size and 0xFF)
        for (name in bytes) {
            out.write(name.size)
            out.write(name)
        }
        return out.toByteArray()
    }

    private const val LIST_FORMAT = 1
    private const val MAX_PROGRAMS = 1000
    private const val MAX_NAME_LENGTH = 255

    private fun strict(payload: ByteArray, offset: Int, length: Int): String? =
        try {
            Charsets.UTF_8.newDecoder()
                .onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT)
                .decode(ByteBuffer.wrap(payload, offset, length))
                .toString()
        } catch (e: CharacterCodingException) {
            null
        }
}
