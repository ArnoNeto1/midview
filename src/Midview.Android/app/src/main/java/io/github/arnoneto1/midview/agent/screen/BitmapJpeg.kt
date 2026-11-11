package io.github.arnoneto1.midview.agent.screen

import android.graphics.Bitmap
import io.github.arnoneto1.midview.agent.net.RegionEncoder
import java.io.ByteArrayOutputStream

// Pictures of the screen as JPEG, with Android's Bitmap: a region for the
// big window, or the whole screen scaled down for a thumbnail.
object BitmapJpeg : RegionEncoder {
    override fun jpeg(frame: Frame, rect: Rect, quality: Int): ByteArray {
        val bitmap = Bitmap.createBitmap(
            frame.pixels, rect.y * frame.width + rect.x, frame.width, rect.width, rect.height, Bitmap.Config.ARGB_8888,
        )
        return compress(bitmap, quality)
    }

    fun thumbnail(frame: Frame, width: Int, height: Int, quality: Int): ByteArray {
        val whole = Bitmap.createBitmap(frame.pixels, 0, frame.width, frame.width, frame.height, Bitmap.Config.ARGB_8888)
        if (whole.width == width && whole.height == height) return compress(whole, quality)
        val scaled = try {
            Bitmap.createScaledBitmap(whole, width, height, true)
        } finally {
            whole.recycle()
        }
        return compress(scaled, quality)
    }

    private fun compress(bitmap: Bitmap, quality: Int): ByteArray = try {
        ByteArrayOutputStream().also { bitmap.compress(Bitmap.CompressFormat.JPEG, quality, it) }.toByteArray()
    } finally {
        bitmap.recycle()
    }
}
