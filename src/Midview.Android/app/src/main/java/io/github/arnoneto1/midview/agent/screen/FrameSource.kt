package io.github.arnoneto1.midview.agent.screen

import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlin.math.max
import kotlin.math.roundToInt

// One picture of the screen: ARGB pixels, row by row, width x height of
// them. seq grows with every new picture. Never changed once made, so any
// thread may read it.
@Suppress("ArrayInDataClass")
data class Frame(val pixels: IntArray, val width: Int, val height: Int, val seq: Long)

// Where the screen's pictures come from (the capture, or a test's).
interface FrameSource {
    // The newest picture; null while there is none (no capture).
    fun latest(): Frame?

    // A picture newer than afterSeq (0: any), waiting at most timeoutMs for
    // one; null if none came.
    fun awaitNew(afterSeq: Long, timeoutMs: Long): Frame?
}

// A picture just turned into pixels (what the capture's convert gives).
class Picture(val pixels: IntArray, val width: Int, val height: Int)

// The newest picture of the screen, for everyone who wants it (the
// thumbnails and the big window of every teacher). The capture only says
// that the screen changed (changed); its newest picture is turned into
// pixels (convert, a few MB and some milliseconds each time) only when
// someone asks for it: a big window as often as its pace allows, a
// thumbnail every few seconds, nobody: never.
//
// The accessibility service (attachShots) works the other way: it has no
// stream of pictures, it takes one when asked (shoot), and Android allows
// one only every so often. A shot is taken only when someone asks and the
// last one is at least minIntervalMillis old; meanwhile the last one is
// given. While attached it wins over the capture's pictures (attach), which
// go on being kept for when it goes away (detachShots).
class LatestFrame(private val clock: () -> Long = { System.nanoTime() / 1_000_000 }) : FrameSource {
    // java.lang.Object, for wait and notifyAll.
    @Suppress("PLATFORM_CLASS_MAPPED_TO_KOTLIN")
    private val lock = Object()
    private var frame: Frame? = null
    private var seq = 0L
    private var convert: (() -> Picture?)? = null
    private var dirty = false
    private var shoot: (() -> Picture?)? = null
    private var shotInterval = 0L
    private var lastShotAt: Long? = null

    // The capture started: convert turns its newest picture into pixels
    // (null: none to be had). Called by whoever asks, one at a time.
    fun attach(convert: () -> Picture?) = synchronized(lock) {
        this.convert = convert
        if (shoot == null) frame = null
        dirty = true
        lock.notifyAll()
    }

    // The accessibility service connected: shoot takes a picture of the
    // screen now (null: none to be had), called by whoever asks, one at a
    // time, at most once every minIntervalMillis.
    fun attachShots(minIntervalMillis: Long, shoot: () -> Picture?) = synchronized(lock) {
        this.shoot = shoot
        shotInterval = minIntervalMillis
        lastShotAt = null
        frame = null
        lock.notifyAll()
    }

    // The accessibility service went away: the capture's pictures again, if any.
    fun detachShots() = synchronized(lock) {
        if (shoot == null) return@synchronized
        shoot = null
        lastShotAt = null
        frame = null
        dirty = convert != null
        lock.notifyAll()
    }

    // The capture has a newer picture.
    fun changed() = synchronized(lock) {
        dirty = true
        lock.notifyAll()
    }

    // A picture already in pixels (tests).
    fun publish(pixels: IntArray, width: Int, height: Int) = synchronized(lock) {
        keep(Picture(pixels, width, height))
        lock.notifyAll()
    }

    // The capture stopped: no picture until it starts again (the shots, if
    // attached, go on).
    fun clear() = synchronized(lock) {
        convert = null
        dirty = false
        if (shoot == null) frame = null
    }

    override fun latest(): Frame? = synchronized(lock) {
        refresh()
        frame
    }

    // An interrupted wait throws InterruptedException (the stream is ending).
    override fun awaitNew(afterSeq: Long, timeoutMs: Long): Frame? {
        val deadline = System.nanoTime() + timeoutMs * 1_000_000
        synchronized(lock) {
            while (true) {
                refresh()
                val newest = frame
                if (newest != null && newest.seq > afterSeq) return newest
                val leftMs = (deadline - System.nanoTime()) / 1_000_000
                if (leftMs <= 0) return null
                lock.wait(minOf(leftMs, untilNextShot() ?: leftMs))
            }
        }
    }

    // Under the lock.
    private fun refresh() {
        val shots = shoot
        if (shots != null) {
            val now = clock()
            val last = lastShotAt
            if (last == null || now - last >= shotInterval) {
                lastShotAt = now
                shots()?.let { keep(it) }
            }
            return
        }
        if (!dirty) return
        dirty = false
        convert?.invoke()?.let { keep(it) }
    }

    // Under the lock: how long until a shot may be taken again (at least
    // 1 ms); null without shots.
    private fun untilNextShot(): Long? {
        if (shoot == null) return null
        val last = lastShotAt ?: return 1
        return maxOf(1, last + shotInterval - clock())
    }

    private fun keep(picture: Picture) {
        seq++
        frame = Frame(picture.pixels, picture.width, picture.height, seq)
    }
}

// Picture sizes. Plain arithmetic.
object FrameScale {
    // The capture's longest side at most: enough for the big window, and a
    // phone's full resolution would cost far more to compare and send.
    const val MAX_CAPTURE_SIDE = 1280

    // The capture's size for a screen of width x height: the same shape,
    // the long side at most MAX_CAPTURE_SIDE.
    fun capture(width: Int, height: Int): Pair<Int, Int> = fit(width, height, MAX_CAPTURE_SIDE.toDouble() / maxOf(width, height))

    // A thumbnail of a width x height picture in a box: the same shape,
    // never bigger than the box, never enlarged; never empty.
    fun thumbnail(boxWidth: Int, boxHeight: Int, width: Int, height: Int): Pair<Int, Int> =
        fit(width, height, minOf(boxWidth.toDouble() / width, boxHeight.toDouble() / height))

    private fun fit(width: Int, height: Int, scale: Double): Pair<Int, Int> {
        if (scale >= 1.0) return width to height
        return max(1, (width * scale).roundToInt()) to max(1, (height * scale).roundToInt())
    }
}

// The capture's RGBA_8888 rows (R, G, B, A bytes, rows rowStride bytes
// apart, maybe padded) as ARGB ints, always opaque.
object RgbaPixels {
    fun toArgb(buffer: ByteBuffer, width: Int, height: Int, pixelStride: Int, rowStride: Int): IntArray {
        val out = IntArray(width * height)
        val source = buffer.duplicate().order(ByteOrder.LITTLE_ENDIAN)
        val start = buffer.position()
        if (pixelStride == 4) {
            // Little-endian, R G B A reads as 0xAABBGGRR: B and R swap places.
            for (row in 0 until height) {
                source.position(start + row * rowStride)
                source.slice().order(ByteOrder.LITTLE_ENDIAN).asIntBuffer().get(out, row * width, width)
            }
            for (i in out.indices) {
                val abgr = out[i]
                out[i] = OPAQUE or ((abgr and 0xFF) shl 16) or (abgr and 0xFF00) or ((abgr ushr 16) and 0xFF)
            }
        } else {
            for (row in 0 until height) {
                var at = start + row * rowStride
                for (column in 0 until width) {
                    val r = source.get(at).toInt() and 0xFF
                    val g = source.get(at + 1).toInt() and 0xFF
                    val b = source.get(at + 2).toInt() and 0xFF
                    out[row * width + column] = OPAQUE or (r shl 16) or (g shl 8) or b
                    at += pixelStride
                }
            }
        }
        return out
    }

    private const val OPAQUE = 0xFF000000.toInt()
}
