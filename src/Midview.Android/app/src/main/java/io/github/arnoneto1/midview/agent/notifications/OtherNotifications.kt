package io.github.arnoneto1.midview.agent.notifications

import io.github.arnoneto1.midview.agent.policy.Lockdown

// The school's choice (2026-11-15): on a school device only the Midview's own
// notifications show. Kept, never touched: the Midview's; Android's own
// ("android" and the system UI: a low battery, the USB cable); and the
// emergency alerts (the cell broadcast receivers, AOSP's and Google's, also
// as the Android 11+ module), which carry the Defesa Civil's alerts. Kept by
// the app, never by the notification's category: an alarm that rings is an
// ongoing notification, which nothing removes anyway, and any app could call
// its notifications alarms to get past.
object OtherNotifications {
    val KEPT: Set<String> = setOf(
        Lockdown.PACKAGE,
        "android",
        "com.android.systemui",
        "com.android.cellbroadcastreceiver",
        "com.android.cellbroadcastreceiver.module",
        "com.google.android.cellbroadcastreceiver",
    )

    // A notification shown now (or just posted) the Midview takes away:
    // another app's, and one the student could swipe away. An ongoing or
    // foreground one cannot be removed by a listener (Android ignores it),
    // so it is left alone.
    // Also Android's "Atualizado pelo seu administrador", which Android
    // posts after each update of the Midview, tagged with the Midview's
    // package (seen on the school's Multilaser M10: pkg "android", channel
    // DEVICE_ADMIN_ALERTS, tag the agent's package).
    fun removes(pkg: String, clearable: Boolean, foreground: Boolean, tag: String? = null): Boolean =
        clearable && !foreground && (pkg !in KEPT || (pkg == ANDROID && tag == Lockdown.PACKAGE))

    private const val ANDROID = "android"

    // An app whose notifications Android 13+ denies at the source
    // (POST_NOTIFICATIONS): every other app, except the phone's dialer
    // (calls), whose incoming call screen goes through a notification.
    fun denies(pkg: String, calls: Set<String>): Boolean = pkg !in KEPT && pkg !in calls
}
