package io.github.arnoneto1.midview.agent.status

import android.content.res.Resources
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Typeface
import java.io.ByteArrayOutputStream
import kotlin.math.max
import kotlin.math.roundToInt

// What the teacher's tile shows of a phone until its screen can be seen
// (plan 2): a picture of the phone standing up, with its name and a short
// status, as a JPEG that fits the box the teacher asked for. The layout
// (size, wrap) is plain arithmetic; only render touches Android's graphics.
object StatusImage {
    // A phone's screen when the real one can't be read: the Master's tile
    // shape (TileSize.PhoneAspect, 9:19.5).
    private const val DEFAULT_ASPECT = 9.0 / 19.5

    private const val JPEG_QUALITY = 80
    // ARGB, as plain numbers: the JVM tests load this object without
    // Android's Color.
    private const val BACKGROUND = 0xFF202124.toInt()
    private const val INK = 0xFFEBEBEB.toInt()
    private const val MUTED_INK = 0xFFBEBEBE.toInt()
    private const val ONLINE = 0xFF4CAF50.toInt()

    // The biggest picture of the screen, standing up (whatever way the
    // phone is held), that fits in boxWidth x boxHeight; never empty. An
    // unknown screen (0) has the tile's shape.
    fun size(boxWidth: Int, boxHeight: Int, screenWidth: Int, screenHeight: Int): Pair<Int, Int> {
        val short = minOf(screenWidth, screenHeight)
        val long = maxOf(screenWidth, screenHeight)
        val aspect = if (short > 0) short.toDouble() / long else DEFAULT_ASPECT

        var height = boxHeight.toDouble()
        var width = height * aspect
        if (width > boxWidth) {
            width = boxWidth.toDouble()
            height = width / aspect
        }
        return max(1, width.roundToInt()).coerceAtMost(max(1, boxWidth)) to
            max(1, height.roundToInt()).coerceAtMost(max(1, boxHeight))
    }

    // text in lines no wider than maxWidth (as measure tells), broken
    // between words; a word wider than a line gets a line of its own.
    fun wrap(text: String, maxWidth: Float, measure: (String) -> Float): List<String> {
        val lines = mutableListOf<String>()
        var line = ""
        for (word in text.split(' ').filter { it.isNotEmpty() }) {
            val longer = if (line.isEmpty()) word else "$line $word"
            if (line.isEmpty() || measure(longer) <= maxWidth) {
                line = longer
            } else {
                lines += line
                line = word
            }
        }
        if (line.isNotEmpty()) {
            lines += line
        }
        return lines
    }

    // The JPEG for a RequestThumbnail of boxWidth x boxHeight.
    fun render(boxWidth: Int, boxHeight: Int, deviceName: String, status: String): ByteArray {
        val screen = Resources.getSystem().displayMetrics
        val (width, height) = size(boxWidth, boxHeight, screen.widthPixels, screen.heightPixels)
        val bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        try {
            draw(Canvas(bitmap), width.toFloat(), height.toFloat(), deviceName, status)
            return ByteArrayOutputStream().also { bitmap.compress(Bitmap.CompressFormat.JPEG, JPEG_QUALITY, it) }
                .toByteArray()
        } finally {
            bitmap.recycle()
        }
    }

    private fun draw(canvas: Canvas, width: Float, height: Float, deviceName: String, status: String) {
        canvas.drawColor(BACKGROUND)
        val margin = width * 0.1f
        val textWidth = width - 2 * margin

        val namePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = INK
            typeface = Typeface.DEFAULT_BOLD
            textSize = max(6f, width * 0.12f)
        }
        val statusPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = MUTED_INK
            textSize = max(5f, width * 0.09f)
        }

        // The green dot of "online", then the name and the status, a third
        // of the way down.
        var y = height / 3
        val dot = max(1.5f, width * 0.04f)
        canvas.drawCircle(margin + dot, y - dot, dot, Paint(Paint.ANTI_ALIAS_FLAG).apply { color = ONLINE })
        y += namePaint.textSize
        for (line in wrap(deviceName, textWidth) { namePaint.measureText(it) }.take(2)) {
            canvas.drawText(line, margin, y, namePaint)
            y += namePaint.textSize * 1.2f
        }
        y += statusPaint.textSize * 0.5f
        for (line in wrap(status, textWidth) { statusPaint.measureText(it) }) {
            if (y > height - margin / 2) break
            canvas.drawText(line, margin, y, statusPaint)
            y += statusPaint.textSize * 1.25f
        }
    }
}
