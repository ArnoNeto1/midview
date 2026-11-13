package io.github.arnoneto1.midview.agent.wallpaper

import android.app.WallpaperManager
import android.content.Context
import android.graphics.BitmapFactory
import java.io.ByteArrayInputStream

// WallpaperTarget on WallpaperManager and BitmapFactory. The picture goes to
// Android as it came (setStream: it scales and crops it to the screen
// itself); the check decodes it at a reduced size, never as a full bitmap.
class AndroidWallpaper(context: Context) : WallpaperTarget {
    private val wallpapers = WallpaperManager.getInstance(context.applicationContext)

    override fun size(image: ByteArray): Pair<Int, Int>? {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(image, 0, image.size, bounds)
        return if (bounds.outWidth > 0 && bounds.outHeight > 0) bounds.outWidth to bounds.outHeight else null
    }

    override fun decodes(image: ByteArray): Boolean {
        val (width, height) = size(image) ?: return false
        val options = BitmapFactory.Options().apply { inSampleSize = TeacherWallpaper.checkSampleSize(width, height) }
        val bitmap = BitmapFactory.decodeByteArray(image, 0, image.size, options) ?: return false
        bitmap.recycle()
        return true
    }

    // setStream answers 0 when the system refused it (no exception).
    override fun set(image: ByteArray, screens: Int): Boolean =
        wallpapers.setStream(ByteArrayInputStream(image), null, true, screens) != 0

    override fun clear(screens: Int) = wallpapers.clear(screens)
}
