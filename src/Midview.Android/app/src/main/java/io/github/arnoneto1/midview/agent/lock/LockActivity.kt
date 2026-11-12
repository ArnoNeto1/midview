package io.github.arnoneto1.midview.agent.lock

import android.app.Activity
import android.app.ActivityManager
import android.content.Context
import android.content.Intent
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.util.Log
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import io.github.arnoneto1.midview.agent.screen.ScreenCaptureService

// What the student sees while the teacher keeps the phone locked: the whole
// screen dark grey with a light-grey padlock in the middle, no text, as on a
// PC (WinFormsScreenLock). Held in lock task mode (Lockdown allows it for
// this app, with nothing of the system's left), so back, home, recents and
// the notifications lead nowhere; if the student still gets out of it, it
// comes back. show/hide come from ScreenLock (first hold, last release);
// while it is up a request to see the screen waits (ScreenConsent), and the
// capture, if running, goes on: the teacher's tile shows the padlock.
class LockActivity : Activity() {
    private val comeBack = Runnable { if (wanted) start(applicationContext) }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // Always drawn and registered, so a lock that comes back before
        // onResume still has its padlock and hide() can release it; onResume
        // lets go if it was hidden again before coming up.
        current = this
        setContentView(PadlockView(this))
    }

    override fun onStart() {
        super.onStart()
        main.removeCallbacks(comeBack)
    }

    override fun onResume() {
        super.onResume()
        if (!wanted) {
            release()
            return
        }
        hideSystemBars()
        val activities = getSystemService(ActivityManager::class.java)
        if (activities.lockTaskModeState == ActivityManager.LOCK_TASK_MODE_NONE) {
            try {
                startLockTask()
            } catch (e: RuntimeException) {
                // Not allowed after all: the padlock still covers the screen.
                Log.w(TAG, "startLockTask failed; the padlock stays up without lock task", e)
            }
        }
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) hideSystemBars()
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        // Nowhere to go back to while locked.
    }

    override fun onStop() {
        super.onStop()
        // Left somehow while still locked (not the screen going off): back on top.
        if (wanted && !isFinishing && getSystemService(PowerManager::class.java).isInteractive) {
            main.postDelayed(comeBack, COME_BACK_MILLIS)
        }
    }

    override fun onDestroy() {
        main.removeCallbacks(comeBack)
        if (current === this) current = null
        super.onDestroy()
        // Closed while still locked: up again.
        if (wanted) start(applicationContext)
    }

    private fun release() {
        try {
            stopLockTask()
        } catch (e: RuntimeException) {
            // It was not in lock task mode.
        }
        finish()
        @Suppress("DEPRECATION")
        overridePendingTransition(0, 0)
    }

    @Suppress("DEPRECATION")
    private fun hideSystemBars() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            window.insetsController?.let {
                it.hide(WindowInsets.Type.systemBars())
                it.systemBarsBehavior = WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            }
        } else {
            window.decorView.systemUiVisibility = View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or
                View.SYSTEM_UI_FLAG_FULLSCREEN or
                View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or
                View.SYSTEM_UI_FLAG_LAYOUT_STABLE or
                View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or
                View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
        }
    }

    // The padlock of WinFormsScreenLock.DrawPadlock: 5 wide by 6 tall, 28 %
    // of the screen's height, the lower half a solid body, the upper half an
    // arch half a unit thick. Drawn, so it stays sharp on any screen.
    private class PadlockView(context: Context) : View(context) {
        private val body = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = PADLOCK
            style = Paint.Style.FILL
        }
        private val shackle = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = PADLOCK
            style = Paint.Style.STROKE
        }
        private val arch = Path()

        override fun onDraw(canvas: Canvas) {
            canvas.drawColor(BACKGROUND)
            val height = this.height * 0.28f
            val unit = height / 6f
            val width = unit * 5f
            val left = (this.width - width) / 2f
            val top = (this.height - height) / 2f

            canvas.drawRect(left, top + unit * 3f, left + width, top + height, body)

            // The arch's centre line: legs 1.25 units in from each side,
            // rising from the body to a round top.
            val legLeft = left + unit * 1.25f
            val legRight = left + width - unit * 1.25f
            val radius = (legRight - legLeft) / 2f
            val archTop = top + unit / 4f
            arch.reset()
            arch.moveTo(legLeft, top + unit * 3f)
            arch.lineTo(legLeft, archTop + radius)
            arch.arcTo(RectF(legLeft, archTop, legRight, archTop + radius * 2f), 180f, 180f)
            arch.lineTo(legRight, top + unit * 3f)
            shackle.strokeWidth = unit / 2f
            canvas.drawPath(arch, shackle)
        }
    }

    companion object {
        private const val TAG = "LockActivity"
        private val BACKGROUND = Color.rgb(64, 64, 64)
        private val PADLOCK = Color.rgb(220, 220, 220)

        // Long enough for whatever came up over it to settle first.
        private const val COME_BACK_MILLIS = 500L

        private val main = Handler(Looper.getMainLooper())
        private val gate = Any()

        // Whether the teacher keeps the phone locked now.
        @Volatile private var wanted = false

        // The lock screen up now, if any (on the main thread only).
        private var current: LockActivity? = null

        // ScreenLock's first hold. Throws when the lock screen cannot be
        // started, wanting nothing.
        fun show(context: Context) {
            synchronized(gate) {
                wanted = true
                ScreenCaptureService.consent.lockedChanged(true)
            }
            try {
                start(context)
            } catch (e: RuntimeException) {
                synchronized(gate) {
                    wanted = false
                    ScreenCaptureService.consent.lockedChanged(false)
                }
                throw e
            }
        }

        // ScreenLock's last release: the lock task ends and the screen is
        // the student's again, then a request to see it may come up.
        fun hide() {
            synchronized(gate) { wanted = false }
            main.post {
                synchronized(gate) {
                    // Locked again meanwhile.
                    if (wanted) return@post
                    current?.release()
                    ScreenCaptureService.consent.lockedChanged(false)
                }
            }
        }

        private fun start(context: Context) {
            context.startActivity(
                Intent(context, LockActivity::class.java)
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_NO_ANIMATION),
            )
        }
    }
}
