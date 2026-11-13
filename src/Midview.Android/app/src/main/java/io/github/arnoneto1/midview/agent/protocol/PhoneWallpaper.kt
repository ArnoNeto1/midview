package io.github.arnoneto1.midview.agent.protocol

// "Papel de parede dos celulares e tablets", as
// src/Midview.Protocol/PhoneWallpaper.cs: the payload of SET_PHONE_WALLPAPER
// is one byte, the screens (WallpaperManager's FLAG_SYSTEM 1, FLAG_LOCK 2, or
// both 3), then the picture, a JPEG or PNG of at most MAX_IMAGE_BYTES; with
// no picture the phone puts its own wallpaper back on those screens.
object PhoneWallpaper {
    const val HOME = 1
    const val LOCK = 2
    const val BOTH = HOME or LOCK

    const val MAX_IMAGE_BYTES = 6 * 1024 * 1024

    // The agent refuses a picture bigger than this on either side.
    const val MAX_SIDE = 4096

    class Request(val screens: Int, val image: ByteArray)

    fun isValid(screens: Int): Boolean = screens in HOME..BOTH

    // A JPEG (FF D8 FF) or a PNG (its 8-byte signature), by the first bytes.
    fun isImage(image: ByteArray): Boolean = image.startsWith(JPEG_START) || image.startsWith(PNG_START)

    fun encode(screens: Int, image: ByteArray): ByteArray {
        require(isValid(screens)) { "screens 1, 2 or 3" }
        require(image.size <= MAX_IMAGE_BYTES) { "picture too big" }
        require(image.isEmpty() || isImage(image)) { "JPEG or PNG only" }
        return byteArrayOf(screens.toByte()) + image
    }

    // Null for anything but valid screens followed by nothing or a JPEG or
    // PNG of at most MAX_IMAGE_BYTES.
    fun decode(payload: ByteArray): Request? {
        if (payload.isEmpty() || payload.size - 1 > MAX_IMAGE_BYTES) return null
        val screens = payload[0].toInt() and 0xFF
        if (!isValid(screens)) return null
        val image = payload.copyOfRange(1, payload.size)
        return if (image.isEmpty() || isImage(image)) Request(screens, image) else null
    }

    private val JPEG_START = byteArrayOf(0xFF.toByte(), 0xD8.toByte(), 0xFF.toByte())
    private val PNG_START = byteArrayOf(0x89.toByte(), 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)

    private fun ByteArray.startsWith(prefix: ByteArray): Boolean =
        size >= prefix.size && prefix.indices.all { this[it] == prefix[it] }
}
