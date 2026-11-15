package io.github.arnoneto1.midview.agent.notifications

// What denying the other apps' notifications needs from Android, behind an
// interface so it is tested on the JVM.
interface NotificationPermissions {
    val sdkInt: Int
    fun isDeviceOwner(): Boolean

    // The Midview is leaving the phone (PhoneLeaving): nothing is denied again.
    fun leaving(): Boolean

    // The apps installed for this user.
    fun installed(): List<String>

    // The phone's dialer apps (the default and the system's), none on a tablet.
    fun calls(): Set<String>

    // Whether the Device Owner already denies POST_NOTIFICATIONS to the app
    // (its grant state is PERMISSION_GRANT_STATE_DENIED), and denying it;
    // false when Android would not (a system app's fixed permission).
    fun isDenied(pkg: String): Boolean
    fun deny(pkg: String): Boolean
}

// Android 13 and newer: the Device Owner denies POST_NOTIFICATIONS to every
// other app (OtherNotifications says which), so they post nothing at all;
// the student cannot turn it on again in Settings. Run at the agent's start
// and whenever an app is installed or updated (PackageWatcher); an app
// already denied is not asked again, nor, until the agent starts again, one
// Android refused. Android 10 to 12 have no such permission: there only the
// listener (NotificationSweeper) takes the notifications away. "Tirar o
// Midview" sets every app back to the default (LeaveGateway.allowNotifications).
class NotificationDenial(
    private val gw: NotificationPermissions,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    private val refused = mutableSetOf<String>()

    @Synchronized
    fun apply() {
        if (gw.sdkInt < ANDROID_13 || gw.leaving() || !gw.isDeviceOwner()) return
        try {
            val calls = gw.calls()
            val todo = gw.installed()
                .filter { OtherNotifications.denies(it, calls) && it !in refused }
                .filterNot { gw.isDenied(it) }
                .sorted()
            if (todo.isEmpty()) return
            val notDenied = todo.filterNot { pkg ->
                try {
                    gw.deny(pkg)
                } catch (e: RuntimeException) {
                    false
                }
            }
            refused += notDenied
            val denied = todo.size - notDenied.size
            log("Notifications denied to $denied apps" + if (notDenied.isEmpty()) "." else "; Android kept ${notDenied.size}.", null)
        } catch (e: RuntimeException) {
            log("The other apps' notifications could not be denied", e)
        }
    }

    companion object {
        // Manifest.permission.POST_NOTIFICATIONS, Android 13 and newer.
        const val ANDROID_13 = 33
    }
}
