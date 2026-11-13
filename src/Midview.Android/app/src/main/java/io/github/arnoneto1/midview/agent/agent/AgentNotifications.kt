package io.github.arnoneto1.midview.agent.agent

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.pm.ServiceInfo
import android.graphics.drawable.Icon
import android.os.Build
import android.util.Log
import io.github.arnoneto1.midview.agent.ui.PairingCodeActivity
import io.github.arnoneto1.midview.agent.ui.SchoolSettingsActivity

// The agent's notifications: its fixed one (the foreground service's) and
// the pairing code's.

// How long the code screen has to come up when it is the only way
// to show the code.
private const val SHOW_TIMEOUT_MILLIS = 3_000L
private const val PAIRING_CHANNEL = "pairing"
private const val STATUS_NOTIFICATION = 1
private const val PAIRING_NOTIFICATION = 2

// True only when the code is really on the screen: the notification
// can show (the app's notifications and the pairing channel are on), or
// else the code screen came up.
internal fun AgentService.showCode(code: String, teacher: String): Boolean {
    PairingCodeActivity.show(this, code, teacher)
    val manager = notifications()
    val notified = manager.areNotificationsEnabled() &&
        manager.getNotificationChannel(PAIRING_CHANNEL)?.importance != NotificationManager.IMPORTANCE_NONE
    if (notified) {
        val open = PendingIntent.getActivity(
            this, 0, PairingCodeActivity.intent(this), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val (title, detail) = PairingCodeActivity.texts(code, teacher)
        val notification = Notification.Builder(this, PAIRING_CHANNEL)
            .setSmallIcon(android.R.drawable.ic_lock_idle_lock)
            .setContentTitle("$title: $code")
            .setContentText(detail)
            .setCategory(Notification.CATEGORY_CALL)
            .setOngoing(true)
            .setContentIntent(open)
            .setFullScreenIntent(open, true)
            .build()
        manager.notify(PAIRING_NOTIFICATION, notification)
        return true
    }
    return PairingCodeActivity.awaitShown(SHOW_TIMEOUT_MILLIS)
}

internal fun AgentService.hideCode() {
    PairingCodeActivity.hide()
    notifications().cancel(PAIRING_NOTIFICATION)
}

// With "Ajustes da escola": the admin password frees the Wi-Fi and airplane
// mode for a while (SchoolSettingsActivity).
internal fun AgentService.goForeground(text: String) {
    val settings = PendingIntent.getActivity(
        this, 0, SchoolSettingsActivity.intent(this), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )
    val notification = Notification.Builder(this, StatusChannel.ID)
        .setSmallIcon(android.R.drawable.ic_lock_idle_lock)
        .setContentTitle(DeviceWord.say(text))
        .setStyle(Notification.BigTextStyle().bigText(DeviceWord.say(text)))
        .setOngoing(true)
        .setVisibility(StatusChannel.LOCK_SCREEN)
        .addAction(
            Notification.Action.Builder(
                Icon.createWithResource(this, android.R.drawable.ic_lock_idle_lock), SchoolSettingsActivity.TITLE, settings,
            ).build(),
        )
        .build()
    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
        startForeground(STATUS_NOTIFICATION, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE)
    } else {
        startForeground(STATUS_NOTIFICATION, notification)
    }
}

// Before the fixed notification goes up (onCreate), so the old status
// channel carries no foreground notification of this process when deleted.
internal fun AgentService.createChannels() {
    val existing = notifications().notificationChannels.map { it.id }
    for (id in StatusChannel.stale(existing)) {
        try {
            notifications().deleteNotificationChannel(id)
        } catch (e: SecurityException) {
            Log.w("Midview", "The old status channel was not deleted", e)
        }
    }
    notifications().createNotificationChannel(
        NotificationChannel(StatusChannel.ID, "Midview", NotificationManager.IMPORTANCE_LOW).apply {
            lockscreenVisibility = StatusChannel.LOCK_SCREEN
        },
    )
    notifications().createNotificationChannel(
        NotificationChannel(PAIRING_CHANNEL, "Pareamento com o professor", NotificationManager.IMPORTANCE_HIGH),
    )
}

private fun Context.notifications() = getSystemService(NotificationManager::class.java)
