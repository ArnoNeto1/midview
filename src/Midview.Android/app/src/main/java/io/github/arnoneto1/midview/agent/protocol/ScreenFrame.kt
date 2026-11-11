package io.github.arnoneto1.midview.agent.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder

// One region of a ScreenFrame: a JPEG to draw at (x, y), width x height.
data class ScreenRegion(val x: Int, val y: Int, val width: Int, val height: Int, val jpeg: ByteArray) {
    override fun equals(other: Any?) = other is ScreenRegion && x == other.x && y == other.y &&
        width == other.width && height == other.height && jpeg.contentEquals(other.jpeg)

    override fun hashCode() = ((((x * 31 + y) * 31 + width) * 31 + height) * 31) + jpeg.contentHashCode()
}

// Byte-compatible with src/Midview.Protocol/ScreenFrame.cs. Payload, little-endian:
// frame id, screen width, screen height, region count (int32 each); then per
// region x, y, width, height, JPEG length (int32 each) and the JPEG.
object ScreenFrame {
    // The JPEG bytes one message carries at most; a bigger update goes as several messages.
    const val MAX_JPEG_BYTES_PER_MESSAGE = 8 * 1024 * 1024

    private const val HEADER_LENGTH = 16
    private const val REGION_HEADER_LENGTH = 20

    fun encode(frameId: Int, screenW: Int, screenH: Int, regions: List<ScreenRegion>): ByteArray {
        val size = HEADER_LENGTH + regions.sumOf { (REGION_HEADER_LENGTH + it.jpeg.size).toLong() }
        val buffer = ByteBuffer.allocate(size.toInt()).order(ByteOrder.LITTLE_ENDIAN)
        buffer.putInt(frameId).putInt(screenW).putInt(screenH).putInt(regions.size)
        for (region in regions) {
            buffer.putInt(region.x).putInt(region.y).putInt(region.width).putInt(region.height)
                .putInt(region.jpeg.size).put(region.jpeg)
        }
        return buffer.array()
    }

    // The regions of one update in groups of at most MAX_JPEG_BYTES_PER_MESSAGE
    // of JPEG each (a region bigger than that goes alone), one ScreenFrame per group.
    fun split(regions: List<ScreenRegion>): List<List<ScreenRegion>> {
        val groups = ArrayList<List<ScreenRegion>>()
        var group = ArrayList<ScreenRegion>()
        var bytes = 0L
        for (region in regions) {
            if (group.isNotEmpty() && bytes + region.jpeg.size > MAX_JPEG_BYTES_PER_MESSAGE) {
                groups.add(group)
                group = ArrayList()
                bytes = 0
            }
            group.add(region)
            bytes += region.jpeg.size
        }
        if (group.isNotEmpty()) groups.add(group)
        return groups
    }
}
