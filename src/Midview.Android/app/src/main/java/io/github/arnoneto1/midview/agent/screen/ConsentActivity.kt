package io.github.arnoneto1.midview.agent.screen

import android.app.Activity
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.admin.DevicePolicyManager
import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.media.projection.MediaProjectionConfig
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.util.Log
import java.lang.ref.WeakReference

// Puts Android's own request on the screen ("Iniciar agora"), with nothing
// of its own to see, and hands the answer on: yes starts the capture
// (ScreenCaptureService), anything else is consent.denied(). The agent
// starts it from the background (show): a Device Owner may; on any other
// phone Android may hold the start back, so a notification that opens it
// goes up too. A request the student leaves without answering (home, another
// app, the teacher's lock screen over it) is closed, the system's request
// with it, and counts as no answer (ScreenConsent.dismissed), so the next ask
// puts up a new one instead of finding a stale one hidden behind. Android 13
// takes its request down itself when the student goes home, with the same
// "no" as the Cancel button: a "no" counts only if the student is still
// here a moment later (REFUSAL_GRACE_MILLIS).
class ConsentActivity : Activity() {
    private val token = Any()
    private var recreated = false

    // Answered, or closed as no answer: nothing more to do.
    private var settled = false
    private val main = Handler(Looper.getMainLooper())
    private var refusalPending = false
    private val refusal = Runnable {
        if (!settled) {
            settled = true
            hideNotice(this)
            Log.i(TAG, "screen request refused")
            ScreenCaptureService.consent.denied()
            finish()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // Recreated (the phone turned, say): the system's request is already up.
        if (savedInstanceState != null) {
            open = token
            shown = WeakReference(this)
            recreated = true
            return
        }
        // Opened twice (the agent's start and the notification both got
        // through): the first one asks, this one goes.
        if (open != null) {
            finish()
            return
        }
        open = token
        shown = WeakReference(this)
        // The teacher's lock screen came up meanwhile: the request waits for the unlock.
        if (ScreenCaptureService.consent.isLocked) {
            settled = true
            ScreenCaptureService.consent.dismissed()
            finish()
            return
        }
        val manager = getSystemService(MediaProjectionManager::class.java)
        val request = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            // The whole screen, never a single app.
            manager.createScreenCaptureIntent(MediaProjectionConfig.createConfigForDefaultDisplay())
        } else {
            manager.createScreenCaptureIntent()
        }
        try {
            startActivityForResult(request, REQUEST)
        } catch (e: ActivityNotFoundException) {
            answered(Activity.RESULT_CANCELED, null)
        }
    }

    @Deprecated("Deprecated in Java")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == REQUEST) answered(resultCode, data)
    }

    override fun onResume() {
        super.onResume()
        // Recreated and in front with no answer: the system's request went
        // away while the phone turned (its "no" went to the old instance).
        if (recreated && !settled && !refusalPending) closeUnanswered()
    }

    override fun onStop() {
        super.onStop()
        if (isFinishing || isChangingConfigurations) return
        val screenOn = getSystemService(PowerManager::class.java).isInteractive
        if (ScreenConsent.leftUnanswered(isChangingConfigurations, settled, screenOn, refusalPending)) closeUnanswered()
    }

    // No answer: the system's request goes too, and the next ask shows a new one.
    private fun closeUnanswered() {
        main.removeCallbacks(refusal)
        Log.i(TAG, "screen request left without an answer")
        settled = true
        if (open === token) open = null
        try {
            finishActivity(REQUEST)
        } catch (e: RuntimeException) {
            // Gone already.
        }
        ScreenCaptureService.consent.dismissed()
        finish()
    }

    override fun onDestroy() {
        main.removeCallbacks(refusal)
        if (open === token) open = null
        if (shown?.get() === this) shown = null
        super.onDestroy()
    }

    private fun answered(resultCode: Int, data: Intent?) {
        if (settled) return
        if (resultCode == Activity.RESULT_OK && data != null) {
            settled = true
            hideNotice(this)
            // From the screen in front, as a mediaProjection service must start.
            ScreenCaptureService.start(this, resultCode, data)
            finish()
        } else if (!refusalPending) {
            // A no, unless the screen goes away meanwhile (onStop).
            refusalPending = true
            main.postDelayed(refusal, REFUSAL_GRACE_MILLIS)
        }
    }

    companion object {
        const val NOTICE_TITLE = "O professor quer ver a tela"
        const val NOTICE_TEXT = "Toque aqui e depois em \"Iniciar agora\"."

        private const val TAG = "Midview"
        private const val REQUEST = 1
        private const val REFUSAL_GRACE_MILLIS = 500L
        private const val CHANNEL = "consent"
        private const val NOTICE_ID = 4

        // The token of the request on the screen now, if any (not the
        // activity itself, which must not outlive its screen).
        @Volatile private var open: Any? = null

        // The request on the screen now, if any, to close it (closeIfOpen).
        @Volatile private var shown: WeakReference<ConsentActivity>? = null

        // The screen is seen without asking now (the accessibility service
        // connected): a request still on the screen goes, as one left
        // without an answer, and its notice with it. On the main thread.
        fun closeIfOpen(context: Context) {
            shown?.get()?.let { if (!it.settled && !it.isFinishing) it.closeUnanswered() }
            hideNotice(context)
        }

        // Asks the student. With the request already up, the same start
        // brings it back in front (a second ConsentActivity only finishes,
        // see onCreate) and the notice goes up again.
        fun show(context: Context) {
            try {
                context.startActivity(intent(context))
            } catch (e: RuntimeException) {
                // Refused outright: the notification is the way.
            }
            if (!isDeviceOwner(context)) showNotice(context)
        }

        private fun intent(context: Context): Intent =
            Intent(context, ConsentActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)

        private fun isDeviceOwner(context: Context): Boolean =
            context.getSystemService(DevicePolicyManager::class.java).isDeviceOwnerApp(context.packageName)

        private fun showNotice(context: Context) {
            val manager = context.getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(
                NotificationChannel(CHANNEL, "Pedido para ver a tela", NotificationManager.IMPORTANCE_HIGH),
            )
            val opens = PendingIntent.getActivity(
                context, 0, intent(context), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
            val notice = Notification.Builder(context, CHANNEL)
                .setSmallIcon(android.R.drawable.ic_menu_view)
                .setContentTitle(NOTICE_TITLE)
                .setContentText(NOTICE_TEXT)
                .setCategory(Notification.CATEGORY_REMINDER)
                .setAutoCancel(true)
                .setContentIntent(opens)
                .setFullScreenIntent(opens, true)
                .build()
            manager.notify(NOTICE_ID, notice)
        }

        private fun hideNotice(context: Context) {
            context.getSystemService(NotificationManager::class.java).cancel(NOTICE_ID)
        }
    }
}
