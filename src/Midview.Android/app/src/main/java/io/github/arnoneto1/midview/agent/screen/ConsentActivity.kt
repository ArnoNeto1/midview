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

// Puts Android's own request on the screen ("Iniciar agora"), with nothing
// of its own to see, and hands the answer on: yes starts the capture
// (ScreenCaptureService), anything else is consent.denied(). The agent
// starts it from the background (show): a Device Owner may; on any other
// phone Android may hold the start back, so a notification that opens it
// goes up too.
class ConsentActivity : Activity() {
    private val token = Any()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // Recreated (the phone turned, say): the system's request is already up.
        if (savedInstanceState != null) {
            open = token
            return
        }
        // Opened twice (the agent's start and the notification both got
        // through): the first one asks, this one goes.
        if (open != null) {
            finish()
            return
        }
        open = token
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

    override fun onDestroy() {
        if (open === token) open = null
        super.onDestroy()
    }

    private fun answered(resultCode: Int, data: Intent?) {
        hideNotice(this)
        if (resultCode == Activity.RESULT_OK && data != null) {
            // From the screen in front, as a mediaProjection service must start.
            ScreenCaptureService.start(this, resultCode, data)
        } else {
            ScreenCaptureService.consent.denied()
        }
        finish()
    }

    companion object {
        const val NOTICE_TITLE = "O professor quer ver a tela"
        const val NOTICE_TEXT = "Toque aqui e depois em \"Iniciar agora\"."

        private const val REQUEST = 1
        private const val CHANNEL = "consent"
        private const val NOTICE_ID = 4

        // The token of the request on the screen now, if any (not the
        // activity itself, which must not outlive its screen).
        @Volatile private var open: Any? = null

        // Asks the student, unless the request is already on the screen.
        fun show(context: Context) {
            if (open != null) return
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
