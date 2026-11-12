package io.github.arnoneto1.midview.agent.apps

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.graphics.PixelFormat
import android.os.Handler
import android.os.Looper
import android.util.Log
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.WindowManager
import android.widget.TextView
import io.github.arnoneto1.midview.agent.screen.AccessibilityCapture

// The student's warning before "Fechar programas" (CloseCountdown), in two
// places, both of which a Device Owner can count on from Android 10 to 14:
// - a notification on a channel of its own with high importance, so Android
//   shows it at the top of the screen over any app (heads-up) and keeps it in
//   the shade with the time left; alerting only the first time;
// - while the accessibility service is connected (the lab's phones, turned on
//   by cable), a strip across the top of the screen (an accessibility overlay,
//   which needs no other permission) that stays up and counts down, and lets
//   every touch through to the student's app. A Device Owner cannot grant
//   "draw over other apps", and a screen of our own would take the app from
//   the student, so without the service the notification is all.
// Every call is moved to the main thread.
class AndroidCloseNotice(context: Context) : CloseNotice {
    private val app = context.applicationContext
    private val main = Handler(Looper.getMainLooper())
    private val manager = app.getSystemService(NotificationManager::class.java)

    // Main thread only.
    private var strip: TextView? = null
    private var stripOwner: WindowManager? = null

    init {
        manager.createNotificationChannel(
            NotificationChannel(CHANNEL, "Avisos do professor", NotificationManager.IMPORTANCE_HIGH).apply {
                description = "O professor vai fechar os apps"
                setShowBadge(false)
            },
        )
    }

    override fun show(headline: String, text: String, alert: Boolean) {
        main.post {
            try {
                notify(headline, text, alert)
            } catch (e: RuntimeException) {
                Log.w(TAG, "The warning notification did not show", e)
            }
            showStrip(text)
        }
    }

    override fun hide() {
        main.post {
            manager.cancel(NOTIFICATION)
            removeStrip()
        }
    }

    private fun notify(headline: String, text: String, alert: Boolean) {
        val notification = Notification.Builder(app, CHANNEL)
            .setSmallIcon(android.R.drawable.ic_dialog_alert)
            .setContentTitle(headline)
            .setContentText(text)
            .setStyle(Notification.BigTextStyle().bigText(text))
            .setCategory(Notification.CATEGORY_REMINDER)
            .setOngoing(true)
            // The heads-up comes when asked; otherwise only the words change.
            .setOnlyAlertOnce(!alert)
            .setShowWhen(false)
            .build()
        manager.notify(NOTIFICATION, notification)
    }

    private fun showStrip(text: String) {
        val service = AccessibilityCapture.instance
        if (service == null) {
            removeStrip()
            return
        }
        strip?.let {
            it.text = text
            return
        }
        try {
            val windows = service.getSystemService(WindowManager::class.java)
            val view = TextView(service).apply {
                this.text = text
                setTextColor(INK)
                setBackgroundColor(BACKGROUND)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 17f)
                gravity = Gravity.CENTER
                val pad = dp(16f)
                setPadding(pad, dp(36f), pad, dp(14f))
                importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_YES
            }
            val params = WindowManager.LayoutParams(
                WindowManager.LayoutParams.MATCH_PARENT,
                WindowManager.LayoutParams.WRAP_CONTENT,
                WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
                    WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE or
                    WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                PixelFormat.TRANSLUCENT,
            ).apply { gravity = Gravity.TOP }
            windows.addView(view, params)
            strip = view
            stripOwner = windows
        } catch (e: RuntimeException) {
            Log.w(TAG, "The warning strip did not show", e)
        }
    }

    private fun removeStrip() {
        val view = strip ?: return
        try {
            stripOwner?.removeView(view)
        } catch (e: RuntimeException) {
            // The service went away, and its window with it.
        }
        strip = null
        stripOwner = null
    }

    private fun dp(value: Float) =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, value, app.resources.displayMetrics).toInt()

    private companion object {
        const val TAG = "Midview"
        const val CHANNEL = "close-apps"
        const val NOTIFICATION = 3
        const val BACKGROUND = 0xF0121A26.toInt()
        const val INK = 0xFFF2F5F8.toInt()
    }
}
