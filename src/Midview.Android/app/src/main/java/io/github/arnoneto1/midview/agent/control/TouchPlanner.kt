package io.github.arnoneto1.midview.agent.control

import io.github.arnoneto1.midview.agent.protocol.InputEvent
import kotlin.math.abs

// Turns the teacher's mouse and keys (InputEvent, in pixels of the picture
// the teacher sees) into what a finger would do on the phone (TouchStep, in
// pixels of the real screen):
// - left button down, moves, up: one finger that touches, follows the mouse
//   and lifts, so a click is a tap, a drag is a swipe or a drag, and holding
//   still is Android's long press;
// - right button: a long press where it is;
// - the wheel: a short swipe up or down where the mouse is;
// - the special keys: KeyMap; the typed characters: TEXT.
// frameSize is the picture the teacher sees, screenSize the phone's screen
// now (both turn with the phone); while either is unknown the pointer is
// not placed. One thread only.
class TouchPlanner(
    private val frameSize: () -> Size?,
    private val screenSize: () -> Size?,
) {
    // Where the teacher's mouse is on the phone's screen; null before it is known.
    var pointer: Spot? = null
        private set

    private var pressed = false
    private var lastMoveAt = 0L
    private var pendingHigh: Char? = null
    private var lastScreen: Size? = null

    fun plan(event: InputEvent, nowMs: Long): List<TouchStep> = when (event.kind) {
        InputEvent.MOUSE_MOVE -> {
            val spot = place(event)
            val from = pointer
            if (spot != null) pointer = spot
            if (pressed && spot != null && from != null && spot != from) {
                listOf(TouchStep.Move(from, spot, moveDuration(nowMs)))
            } else {
                emptyList()
            }
        }
        InputEvent.LEFT_DOWN -> {
            val spot = place(event)
            if (pressed || spot == null) {
                emptyList()
            } else {
                pressed = true
                pointer = spot
                lastMoveAt = nowMs
                listOf(TouchStep.Down(spot))
            }
        }
        InputEvent.LEFT_UP -> if (!pressed) emptyList() else {
            pressed = false
            val from = pointer!!
            val spot = place(event) ?: from
            pointer = spot
            if (spot != from) {
                listOf(TouchStep.Move(from, spot, moveDuration(nowMs)), TouchStep.Up(spot))
            } else {
                listOf(TouchStep.Up(spot))
            }
        }
        InputEvent.RIGHT_DOWN -> {
            val spot = place(event)
            if (pressed || spot == null) emptyList() else {
                pointer = spot
                listOf(TouchStep.LongPress(spot))
            }
        }
        InputEvent.WHEEL -> wheel(event.x)
        InputEvent.KEY_DOWN -> listOfNotNull(KeyMap.of(event.key))
        InputEvent.TEXT -> text(event.key.toChar())
        // RIGHT_UP, KEY_UP and kinds a newer teacher may add: nothing.
        else -> emptyList()
    }

    // The finger that is still down lifts where it is: control ending mid-drag.
    fun release(): List<TouchStep> {
        if (!pressed) return emptyList()
        pressed = false
        return listOf(TouchStep.Up(pointer!!))
    }

    // Steps that piled up while the phone was busy with the one before, made
    // fewer: moves in a row become one move, characters in a row one text,
    // and wheel swipes from the same place one longer swipe.
    fun merge(steps: List<TouchStep>): List<TouchStep> {
        val merged = mutableListOf<TouchStep>()
        for (step in steps) {
            val last = merged.lastOrNull()
            val joined = when {
                last is TouchStep.Move && step is TouchStep.Move && last.to == step.from ->
                    TouchStep.Move(last.from, step.to, minOf(MAX_MERGED_MOVE_MS, last.durationMs + step.durationMs))
                last is TouchStep.Type && step is TouchStep.Type -> TouchStep.Type(last.text + step.text)
                last is TouchStep.Swipe && step is TouchStep.Swipe && last.from == step.from -> {
                    val height = lastScreen?.height ?: Int.MAX_VALUE
                    val y = clampSwipe(last.from.y, last.to.y - last.from.y + (step.to.y - step.from.y), height)
                    TouchStep.Swipe(last.from, Spot(last.to.x, y), last.durationMs)
                }
                else -> null
            }
            if (joined != null) {
                merged[merged.size - 1] = joined
            } else {
                merged += step
            }
        }
        return merged
    }

    private fun wheel(delta: Int): List<TouchStep> {
        val at = pointer ?: return emptyList()
        val screen = screenSize() ?: return emptyList()
        if (pressed || delta == 0) return emptyList()
        lastScreen = screen
        // Wheel away from the teacher (positive) shows what is above: the
        // finger goes down the screen.
        val distance = delta / WHEEL_NOTCH * screen.height * WHEEL_STEP
        val y = clampSwipe(at.y, distance, screen.height)
        if (abs(y - at.y) < 1f) return emptyList()
        return listOf(TouchStep.Swipe(at, Spot(at.x, y), SWIPE_MS))
    }

    private fun clampSwipe(fromY: Float, distance: Float, height: Int): Float {
        val most = height * MAX_SWIPE
        val limited = distance.coerceIn(-most, most)
        return (fromY + limited).coerceIn(0f, (height - 1).toFloat())
    }

    private fun text(c: Char): List<TouchStep> {
        if (c.isHighSurrogate()) {
            pendingHigh = c
            return emptyList()
        }
        val high = pendingHigh
        pendingHigh = null
        if (c.isLowSurrogate()) return if (high == null) emptyList() else listOf(TouchStep.Type("$high$c"))
        // Control characters (Enter, Backspace, Ctrl+letter) come as keys.
        if (c < ' ' || c.code == 0x7F) return emptyList()
        return listOf(TouchStep.Type(c.toString()))
    }

    private fun moveDuration(nowMs: Long): Long {
        val elapsed = nowMs - lastMoveAt
        lastMoveAt = nowMs
        return elapsed.coerceIn(MIN_MOVE_MS, MAX_MOVE_MS)
    }

    // The picture's pixel (its center) on the real screen, inside it.
    private fun place(event: InputEvent): Spot? {
        val frame = frameSize() ?: return null
        val screen = screenSize() ?: return null
        if (frame.width <= 0 || frame.height <= 0 || screen.width <= 0 || screen.height <= 0) return null
        lastScreen = screen
        return ScreenMap.toScreen(event.x, event.y, frame, screen)
    }

    companion object {
        const val MIN_MOVE_MS = 8L
        const val MAX_MOVE_MS = 80L
        const val MAX_MERGED_MOVE_MS = 120L

        // A notch of the wheel (Windows' WHEEL_DELTA) moves the finger a
        // tenth of the screen; one swipe never more than half of it.
        const val WHEEL_NOTCH = 120f
        const val WHEEL_STEP = 0.1f
        const val MAX_SWIPE = 0.5f
        const val SWIPE_MS = 200L
    }
}

// The teacher's picture is the whole screen (status and navigation bars
// included), only smaller: each axis scales on its own.
object ScreenMap {
    fun toScreen(x: Int, y: Int, frame: Size, screen: Size): Spot {
        val sx = ((x + 0.5f) * screen.width / frame.width).coerceIn(0f, (screen.width - 1).toFloat())
        val sy = ((y + 0.5f) * screen.height / frame.height).coerceIn(0f, (screen.height - 1).toFloat())
        return Spot(sx, sy)
    }
}
