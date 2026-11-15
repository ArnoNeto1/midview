package io.github.arnoneto1.midview.agent.notifications

import android.app.Notification
import android.app.admin.DevicePolicyManager
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import android.util.Log
import io.github.arnoneto1.midview.agent.leave.PhoneLeaving

// Only the Midview's notifications on a school device (OtherNotifications
// says what else stays): every other app's notification is taken away as it
// is posted, and those already shown when the listener connects. Android 10
// to 12 have only this; Android 13 and newer also deny the permission at the
// source (NotificationDenial), and this catches what still gets through (a
// media player's paused notification). A listener needs the user's approval,
// which a Device Owner cannot give itself on Android 10: the cable setup
// gives it ("cmd notification allow_listener", PhoneCable). Nothing on a
// device that is not the school's, nor once the Midview is leaving it (then
// the listener lets go of itself).
class NotificationSweeper : NotificationListenerService() {
    override fun onListenerConnected() {
        if (!sweeping()) return
        val shown = try {
            activeNotifications.orEmpty()
        } catch (e: RuntimeException) {
            Log.w(TAG, "The notifications shown could not be read", e)
            return
        }
        val removed = shown.count(::sweep)
        Log.i(TAG, "Notification sweeper on: $removed notifications of other apps removed.")
    }

    override fun onNotificationPosted(sbn: StatusBarNotification) {
        if (sweeping()) sweep(sbn)
    }

    // A school device the Midview is not leaving. Leaving, the listener asks
    // Android to unbind it; the uninstall ends it anyway.
    private fun sweeping(): Boolean {
        if (PhoneLeaving.isActive(this)) {
            runCatching { requestUnbind() }
            return false
        }
        return getSystemService(DevicePolicyManager::class.java)?.isDeviceOwnerApp(packageName) == true
    }

    // True when the notification was taken away. Android ignores the cancel
    // of an ongoing one: those are never asked for, so nothing is logged.
    private fun sweep(sbn: StatusBarNotification): Boolean {
        val foreground = (sbn.notification.flags and Notification.FLAG_FOREGROUND_SERVICE) != 0
        if (!OtherNotifications.removes(sbn.packageName, sbn.isClearable, foreground, sbn.tag)) return false
        return try {
            cancelNotification(sbn.key)
            true
        } catch (e: RuntimeException) {
            // The listener was disconnected meanwhile.
            false
        }
    }

    companion object {
        private const val TAG = "Midview"
    }
}
