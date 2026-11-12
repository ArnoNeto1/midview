package io.github.arnoneto1.midview.agent.control

import android.accessibilityservice.AccessibilityService
import android.accessibilityservice.GestureDescription
import android.graphics.Path
import android.graphics.Point
import android.hardware.display.DisplayManager
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.HandlerThread
import android.util.Log
import android.view.Display
import android.view.accessibility.AccessibilityNodeInfo
import io.github.arnoneto1.midview.agent.screen.AccessibilityCapture
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

// The teacher's input carried out through the Midview accessibility service
// (AccessibilityCapture, turned on by cable): the finger as gestures
// (dispatchGesture; a touch that stays down is a continued stroke, so a drag
// and a long press are one gesture), Android's buttons as global actions, and
// typing as the focused text field's new text (ACTION_SET_TEXT). Only the
// accessibility service can do these without the student; with it off, the
// phone says so instead of being controlled. One connection's control
// (RemoteControl's thread) at a time uses one of these.
class AndroidControlTarget(private val isLocked: () -> Boolean) : ControlTarget {
    // The stroke whose finger is down now.
    private var held: GestureDescription.StrokeDescription? = null
    private var heldAt: Spot? = null

    // A password field shows dots: what the teacher typed into it is kept here.
    private var passwordField: AccessibilityNodeInfo? = null
    private var passwordText = ""

    override val locked: Boolean get() = isLocked()

    override fun refusal(): String? = if (AccessibilityCapture.instance == null) ACCESSIBILITY_OFF else null

    override fun screenSize(): Size? {
        val service = AccessibilityCapture.instance ?: return null
        return try {
            val display = service.getSystemService(DisplayManager::class.java).getDisplay(Display.DEFAULT_DISPLAY) ?: return null
            val size = Point()
            @Suppress("DEPRECATION")
            display.getRealSize(size)
            Size(size.x, size.y)
        } catch (e: RuntimeException) {
            null
        }
    }

    override fun showNotice() = ControlNotice.show()

    override fun hideNotice() = ControlNotice.hide()

    override fun pointerAt(spot: Spot) = ControlNotice.pointerAt(spot.x, spot.y)

    override fun perform(step: TouchStep) {
        val service = AccessibilityCapture.instance ?: return
        when (step) {
            is TouchStep.Down -> {
                held = null
                dispatch(service, GestureDescription.StrokeDescription(point(step.at), 0, TOUCH_MS, true))?.let {
                    held = it
                    heldAt = step.at
                }
            }
            is TouchStep.Move -> {
                val stroke = held
                val from = heldAt
                if (stroke == null || from == null) {
                    // The finger was lost (the student touched the screen): down again where the mouse is.
                    perform(TouchStep.Down(step.to))
                    return
                }
                held = null
                dispatch(service, stroke.continueStroke(line(from, step.to), 0, step.durationMs, true))?.let {
                    held = it
                    heldAt = step.to
                }
            }
            is TouchStep.Up -> {
                val stroke = held ?: return
                val from = heldAt ?: step.at
                held = null
                heldAt = null
                val path = if (from == step.at) point(step.at) else line(from, step.at)
                dispatch(service, stroke.continueStroke(path, 0, TOUCH_MS, false))
            }
            is TouchStep.Swipe -> {
                held = null
                val moved = dispatch(service, GestureDescription.StrokeDescription(line(step.from, step.to), 0, step.durationMs, true))
                    ?: return
                // Still for a moment before lifting: no fling, the list stops where the wheel said.
                dispatch(service, moved.continueStroke(point(step.to), 0, SWIPE_HOLD_MS, false))
            }
            is TouchStep.LongPress -> {
                held = null
                dispatch(service, GestureDescription.StrokeDescription(point(step.at), 0, LONG_PRESS_MS, false))
            }
            is TouchStep.Global -> service.performGlobalAction(
                when (step.key) {
                    GlobalKey.BACK -> AccessibilityService.GLOBAL_ACTION_BACK
                    GlobalKey.HOME -> AccessibilityService.GLOBAL_ACTION_HOME
                    GlobalKey.RECENTS -> AccessibilityService.GLOBAL_ACTION_RECENTS
                },
            )
            is TouchStep.Type -> edit(service) { text, start, end -> TextEdit.insert(text, start, end, step.text) }
            is TouchStep.Edit -> when (step.key) {
                EditKey.BACKSPACE -> edit(service, TextEdit::backspace)
                EditKey.DELETE -> edit(service, TextEdit::delete)
                EditKey.ENTER -> enter(service)
                EditKey.LEFT, EditKey.RIGHT -> {
                    val field = focusedField(service) ?: return
                    val (text, start, end) = read(field)
                    select(field, TextEdit.move(text, start, end, step.key == EditKey.RIGHT))
                }
            }
        }
    }

    // Sends one stroke and waits until Android has played it; the stroke
    // when it played whole, null when it was cancelled (the student touched
    // the screen) or refused.
    private fun dispatch(
        service: AccessibilityService,
        stroke: GestureDescription.StrokeDescription,
    ): GestureDescription.StrokeDescription? {
        val done = CountDownLatch(1)
        val completed = AtomicBoolean(false)
        val gesture = GestureDescription.Builder().addStroke(stroke).build()
        val sent = try {
            service.dispatchGesture(
                gesture,
                object : AccessibilityService.GestureResultCallback() {
                    override fun onCompleted(gestureDescription: GestureDescription?) {
                        completed.set(true)
                        done.countDown()
                    }

                    override fun onCancelled(gestureDescription: GestureDescription?) = done.countDown()
                },
                callbacks,
            )
        } catch (e: RuntimeException) {
            Log.w(TAG, "A gesture was refused", e)
            false
        }
        if (!sent) return null
        done.await(stroke.duration + GESTURE_SLACK_MS, TimeUnit.MILLISECONDS)
        return if (completed.get()) stroke else null
    }

    private fun focusedField(service: AccessibilityService): AccessibilityNodeInfo? {
        val node = try {
            service.findFocus(AccessibilityNodeInfo.FOCUS_INPUT)
        } catch (e: RuntimeException) {
            null
        }
        if (node == null || !node.isEditable) {
            Log.d(TAG, "Typing with no text field in focus (${node?.className})")
            return null
        }
        return node
    }

    private data class FieldText(val text: String, val start: Int, val end: Int)

    private fun read(field: AccessibilityNodeInfo): FieldText {
        if (field.isPassword) {
            // Dots, as many as the characters: ours if the count matches.
            val shown = field.text?.length ?: 0
            if (field != passwordField || shown != passwordText.length) {
                passwordField = field
                passwordText = ""
            }
            return FieldText(passwordText, -1, -1)
        }
        val text = if (field.isShowingHintText) "" else field.text?.toString().orEmpty()
        return FieldText(text, field.textSelectionStart, field.textSelectionEnd)
    }

    private fun edit(service: AccessibilityService, change: (String, Int, Int) -> EditedText?) {
        val field = focusedField(service) ?: return
        val (text, start, end) = read(field)
        val edited = change(text, start, end) ?: return
        val arguments = Bundle().apply {
            putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE, edited.text)
        }
        if (!field.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, arguments)) {
            Log.d(TAG, "The text field took no text (${field.className})")
            return
        }
        if (field.isPassword) passwordText = edited.text
        select(field, edited.cursor)
    }

    private fun select(field: AccessibilityNodeInfo, cursor: Int) {
        val arguments = Bundle().apply {
            putInt(AccessibilityNodeInfo.ACTION_ARGUMENT_SELECTION_START_INT, cursor)
            putInt(AccessibilityNodeInfo.ACTION_ARGUMENT_SELECTION_END_INT, cursor)
        }
        field.performAction(AccessibilityNodeInfo.ACTION_SET_SELECTION, arguments)
    }

    // The keyboard's Enter (go, search, send) where the field has one; a
    // new line in a field of several lines.
    private fun enter(service: AccessibilityService) {
        val field = focusedField(service) ?: return
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            val imeEnter = AccessibilityNodeInfo.AccessibilityAction.ACTION_IME_ENTER
            if (field.actionList.any { it.id == imeEnter.id } && field.performAction(imeEnter.id)) return
        }
        if (field.isMultiLine) edit(service) { text, start, end -> TextEdit.insert(text, start, end, "\n") }
    }

    private fun point(at: Spot) = Path().apply { moveTo(at.x, at.y) }

    private fun line(from: Spot, to: Spot) = Path().apply {
        moveTo(from.x, from.y)
        lineTo(to.x, to.y)
    }

    companion object {
        private const val TAG = "Midview"

        const val ACCESSIBILITY_OFF = "a acessibilidade do Midview está desligada neste celular: ligue pelo cabo"

        // A finger touching down or lifting: as short as Android takes.
        private const val TOUCH_MS = 1L

        // Longer than Android's long press (400 to 500 ms on most phones).
        private const val LONG_PRESS_MS = 700L
        private const val SWIPE_HOLD_MS = 80L

        // How much longer than the stroke Android may take to say it played.
        private const val GESTURE_SLACK_MS = 1_000L

        // The gestures' answers come on a thread of their own, not the main
        // one: the control's thread waits for them.
        private val callbacks: Handler by lazy {
            Handler(HandlerThread("Midview gestures").apply { start() }.looper)
        }
    }
}
