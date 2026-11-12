package io.github.arnoneto1.midview.agent.protocol

import java.io.ByteArrayOutputStream

// One app the student can open: its package name, the label the student sees
// and the launcher icon as a PNG (48x48).
class PhoneApp(val pkg: String, val label: String, val iconPng: ByteArray) {
    override fun equals(other: Any?): Boolean =
        other is PhoneApp && pkg == other.pkg && label == other.label && iconPng.contentEquals(other.iconPng)

    override fun hashCode(): Int = (pkg.hashCode() * 31 + label.hashCode()) * 31 + iconPng.contentHashCode()
}

// The phone's answer to GetPhoneApps, byte for byte as
// src/Midview.Protocol/PhoneAppList.cs (big-endian): format version (1), count
// (u16), then each app as package length (1) + package (UTF-8), label length
// (1) + label (UTF-8, at most 64 chars), icon length (u16) + PNG bytes.
object PhoneAppList {
    const val FORMAT_VERSION = 1
    const val MAX_APPS = 300
    const val MAX_LABEL_LENGTH = 64
    const val MAX_ICON_BYTES = 65535

    // Refuses (IllegalArgumentException) a list that breaks a bound.
    fun encode(apps: List<PhoneApp>): ByteArray {
        require(apps.size <= MAX_APPS) { "too many phone apps" }
        val out = ByteArrayOutputStream()
        out.write(FORMAT_VERSION)
        out.write(apps.size ushr 8)
        out.write(apps.size and 0xFF)

        val seen = HashSet<String>()
        for (app in apps) {
            require(
                PhoneAppPolicy.isValidPackageName(app.pkg) && seen.add(app.pkg) &&
                    app.label.length <= MAX_LABEL_LENGTH && app.iconPng.size <= MAX_ICON_BYTES,
            ) { "invalid phone app" }

            val pkg = app.pkg.toByteArray(Charsets.UTF_8)
            out.write(pkg.size)
            out.write(pkg)
            val label = app.label.toByteArray(Charsets.UTF_8)
            out.write(label.size)
            out.write(label)
            out.write(app.iconPng.size ushr 8)
            out.write(app.iconPng.size and 0xFF)
            out.write(app.iconPng)
        }
        return out.toByteArray()
    }

    // Null for anything that is not exactly a valid list.
    fun decode(payload: ByteArray): List<PhoneApp>? {
        if (payload.size < 3 || payload[0].toInt() != FORMAT_VERSION) {
            return null
        }
        val count = ((payload[1].toInt() and 0xFF) shl 8) or (payload[2].toInt() and 0xFF)
        if (count > MAX_APPS) {
            return null
        }

        val apps = ArrayList<PhoneApp>(count)
        val seen = HashSet<String>()
        var offset = 3

        // A one-byte length and that many UTF-8 bytes; null when they do not fit.
        fun readText(): String? {
            if (offset >= payload.size) {
                return null
            }
            val length = payload[offset++].toInt() and 0xFF
            if (offset + length > payload.size) {
                return null
            }
            val text = strictUtf8(payload, offset, length) ?: return null
            offset += length
            return text
        }

        repeat(count) {
            val pkg = readText() ?: return null
            val label = readText() ?: return null
            if (label.length > MAX_LABEL_LENGTH || !PhoneAppPolicy.isValidPackageName(pkg) || !seen.add(pkg) ||
                offset + 2 > payload.size
            ) {
                return null
            }
            val iconLength = ((payload[offset].toInt() and 0xFF) shl 8) or (payload[offset + 1].toInt() and 0xFF)
            offset += 2
            if (offset + iconLength > payload.size) {
                return null
            }
            apps.add(PhoneApp(pkg, label, payload.copyOfRange(offset, offset + iconLength)))
            offset += iconLength
        }
        return if (offset == payload.size) apps else null
    }
}
