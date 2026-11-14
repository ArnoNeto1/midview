package io.github.arnoneto1.midview.agent.control

import android.graphics.PixelFormat
import android.graphics.Point
import android.hardware.display.DisplayManager
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.util.Log
import android.view.Display
import android.view.Gravity
import android.view.View
import android.view.WindowManager
import io.github.arnoneto1.midview.agent.screen.AccessibilityCapture
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

// "Travar o toque do aluno" on the screen (TouchGate decides): a
// see-through accessibility overlay over the whole screen, status and
// navigation bars included, that takes every touch while the teacher
// controls the device; the red "being controlled" strip (ControlNotice)
// stays visible under it. For the teacher's own gestures it turns
// untouchable first (show waits until Android has it), so the injected
// finger reaches the app below. It never outlives the control: control's
// end removes it, the accessibility service going away takes its windows
// with it, and a watchdog removes it when no control is on any more (no
// strip on the screen). Several teachers controlling at once each have
// their part (TouchLocks): one ending takes only its own away.
object StudentTouchLock {
    private const val TAG = "Midview"

    // How long Android takes to know the window changed, after the change:
    // with 50 ms the teacher's tap was taken now and then on the school's
    // Multilaser M10 (1 in 4); with 150 ms, none in 6.
    private const val SETTLE_MS = 150L
    private const val WAIT_MS = 500L
    private const val WATCH_EVERY_MS = 5_000L

    private val main = Handler(Looper.getMainLooper())

    // Every connection's part (TouchLocks), under its own lock: the screen
    // shows what they add up to, whatever order the changes come in.
    private val parts = Any()
    private val locks = TouchLocks(SystemClock::elapsedRealtime)

    // What is on the screen now (also read by the key filter, any thread).
    @Volatile var overlay: TouchGate.Overlay = TouchGate.Overlay.NONE
        private set

    // Main thread only.
    private var view: View? = null
    private var owner: WindowManager? = null
    private var params: WindowManager.LayoutParams? = null

    private val watchdog = object : Runnable {
        override fun run() {
            if (overlay == TouchGate.Overlay.NONE) return
            if (!ControlNotice.active || AccessibilityCapture.instance == null) {
                Log.w(TAG, "The student's touch lock was left without a control: removed.")
                synchronized(parts) { locks.clear() }
                set(TouchGate.Overlay.NONE)
                return
            }
            main.postDelayed(this, WATCH_EVERY_MS)
        }
    }

    // An opening whose finger lift never came closes again (TouchLocks).
    private val expiry = Runnable {
        synchronized(parts) { locks.expire() }
        apply()
    }

    // owner's part (one connection's TouchGate) changes; returns once the
    // screen shows what all the parts add up to (opening it for the
    // teacher's finger waits a moment more, so the finger is not taken).
    fun show(owner: Any, next: TouchGate.Overlay) {
        val wanted = synchronized(parts) { locks.set(owner, next) }
        val opening = wanted == TouchGate.Overlay.PASSING && overlay != TouchGate.Overlay.PASSING
        val done = CountDownLatch(1)
        main.post {
            apply()
            done.countDown()
        }
        done.await(WAIT_MS, TimeUnit.MILLISECONDS)
        if (opening) Thread.sleep(SETTLE_MS)
    }

    // Main thread: the latest sum always wins, whatever was posted before.
    private fun apply() {
        val wanted = synchronized(parts) { locks.overlay }
        if (wanted != overlay || (wanted != TouchGate.Overlay.NONE && view == null)) set(wanted)
        main.removeCallbacks(expiry)
        if (overlay == TouchGate.Overlay.PASSING) main.postDelayed(expiry, TouchLocks.PASS_LIMIT_MS)
    }

    // Whether a key the student pressed is taken (AccessibilityCapture.onKeyEvent).
    fun takesKey(keyCode: Int): Boolean = TouchGate.blocks(overlay, keyCode)

    private fun set(next: TouchGate.Overlay) {
        if (next == TouchGate.Overlay.NONE) {
            remove()
        } else if (view == null) {
            add(next)
        } else {
            update(next)
        }
        overlay = if (view == null) TouchGate.Overlay.NONE else next
        main.removeCallbacks(watchdog)
        if (overlay != TouchGate.Overlay.NONE) main.postDelayed(watchdog, WATCH_EVERY_MS)
    }

    private fun flagsFor(next: TouchGate.Overlay): Int {
        var flags = WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
            WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN or
            WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS
        if (next == TouchGate.Overlay.PASSING) flags = flags or WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE
        return flags
    }

    private fun add(next: TouchGate.Overlay) {
        val service = AccessibilityCapture.instance ?: return
        try {
            val windows = service.getSystemService(WindowManager::class.java)
            val size = realSize(service)
            val layout = WindowManager.LayoutParams(
                size?.x ?: WindowManager.LayoutParams.MATCH_PARENT,
                size?.y ?: WindowManager.LayoutParams.MATCH_PARENT,
                WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
                flagsFor(next),
                PixelFormat.TRANSLUCENT,
            ).apply {
                gravity = Gravity.TOP or Gravity.START
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                    layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES
                }
            }
            val catcher = View(service).apply {
                // Every touch of the student's ends here.
                setOnTouchListener { _, _ -> true }
                importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
            }
            windows.addView(catcher, layout)
            view = catcher
            owner = windows
            params = layout
        } catch (e: RuntimeException) {
            Log.w(TAG, "The student's touch lock did not show", e)
        }
    }

    private fun update(next: TouchGate.Overlay) {
        val shown = view ?: return
        val layout = params ?: return
        layout.flags = flagsFor(next)
        try {
            owner?.updateViewLayout(shown, layout)
        } catch (e: RuntimeException) {
            Log.w(TAG, "The student's touch lock did not change", e)
            remove()
        }
    }

    private fun remove() {
        val shown = view ?: return
        try {
            owner?.removeView(shown)
        } catch (e: RuntimeException) {
            // The service went away, and its window with it.
        }
        view = null
        owner = null
        params = null
    }

    private fun realSize(service: AccessibilityCapture): Point? = try {
        val display = service.getSystemService(DisplayManager::class.java).getDisplay(Display.DEFAULT_DISPLAY)
        Point().also {
            @Suppress("DEPRECATION")
            display?.getRealSize(it)
        }.takeIf { it.x > 0 && it.y > 0 }
    } catch (e: RuntimeException) {
        null
    }
}
