package io.github.arnoneto1.midview.agent.control

import android.annotation.SuppressLint
import android.graphics.PixelFormat
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Handler
import android.os.Looper
import android.util.Log
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.WindowManager
import android.widget.TextView
import io.github.arnoneto1.midview.agent.agent.DeviceWord
import io.github.arnoneto1.midview.agent.screen.AccessibilityCapture

// The student's "Este celular está sendo controlado pelo professor", as a
// PC's red strip (WinFormsControlNotice): a strip at the top of the screen,
// just under the status bar, while any teacher controls the phone. An
// accessibility overlay (it needs no other permission) that every touch,
// the teacher's included, goes through; while the teacher's mouse is on it
// or close to it, it all but fades away (NoticeFade on a PC), so the teacher
// sees what it covers. Every call is moved to the main thread.
object ControlNotice {
    const val TEXT = "Este celular está sendo controlado pelo professor"

    private const val TAG = "Midview"
    private const val BACKGROUND = 0xFFB00020.toInt()
    private const val INK = 0xFFFFFFFF.toInt()

    private val main = Handler(Looper.getMainLooper())

    // Main thread only.
    private var holders = 0
    private var strip: TextView? = null
    private var stripOwner: WindowManager? = null
    private var faded = false

    fun show() {
        main.post {
            holders++
            if (holders == 1) add()
        }
    }

    fun hide() {
        main.post {
            if (holders > 0) {
                holders--
                if (holders == 0) remove()
            }
        }
    }

    // x and y: the teacher's mouse on the real screen.
    fun pointerAt(x: Float, y: Float) {
        main.post { fade(x, y) }
    }

    private fun fade(x: Float, y: Float) {
        val view = strip ?: return
        val place = IntArray(2)
        view.getLocationOnScreen(place)
        val reach = view.resources.displayMetrics.density * NoticeFadeReach.DP
        val near = NoticeFadeReach.isNear(place[0], place[1], view.width, view.height, x, y, reach)
        if (near == faded) return
        faded = near
        view.animate().alpha(if (near) NoticeFadeReach.FADED else NoticeFadeReach.SHOWN).setDuration(FADE_MS).start()
    }

    @SuppressLint("RtlHardcoded")
    private fun add() {
        val service = AccessibilityCapture.instance ?: return
        try {
            val windows = service.getSystemService(WindowManager::class.java)
            val density = service.resources.displayMetrics.density
            val view = TextView(service).apply {
                text = DeviceWord.say(TEXT)
                setTextColor(INK)
                setTypeface(typeface, Typeface.BOLD)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f)
                gravity = Gravity.CENTER
                val horizontal = (14 * density).toInt()
                val vertical = (6 * density).toInt()
                setPadding(horizontal, vertical, horizontal, vertical)
                background = GradientDrawable().apply {
                    setColor(BACKGROUND)
                    cornerRadius = 6 * density
                }
                alpha = NoticeFadeReach.SHOWN
                importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_YES
            }
            val params = WindowManager.LayoutParams(
                WindowManager.LayoutParams.WRAP_CONTENT,
                WindowManager.LayoutParams.WRAP_CONTENT,
                WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
                    WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE or
                    WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                PixelFormat.TRANSLUCENT,
            ).apply {
                gravity = Gravity.TOP or Gravity.CENTER_HORIZONTAL
                y = statusBarHeight(service) + (4 * density).toInt()
            }
            windows.addView(view, params)
            strip = view
            stripOwner = windows
            faded = false
        } catch (e: RuntimeException) {
            Log.w(TAG, "The control strip did not show", e)
        }
    }

    private fun remove() {
        val view = strip ?: return
        try {
            stripOwner?.removeView(view)
        } catch (e: RuntimeException) {
            // The service went away, and its window with it.
        }
        strip = null
        stripOwner = null
    }

    @SuppressLint("DiscouragedApi", "InternalInsetResource")
    private fun statusBarHeight(service: AccessibilityCapture): Int {
        val id = service.resources.getIdentifier("status_bar_height", "dimen", "android")
        return if (id > 0) service.resources.getDimensionPixelSize(id) else 0
    }

    private const val FADE_MS = 120L
}

// When the strip gets out of the teacher's way: the PC's NoticeFade, on the
// phone's pixels. Plain arithmetic.
object NoticeFadeReach {
    const val SHOWN = 0.9f
    const val FADED = 0.1f

    // How close the mouse must come, in dp.
    const val DP = 24f

    fun isNear(left: Int, top: Int, width: Int, height: Int, x: Float, y: Float, reach: Float): Boolean =
        x >= left - reach && x <= left + width + reach && y >= top - reach && y <= top + height + reach
}
