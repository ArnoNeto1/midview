package io.github.arnoneto1.midview.agent.screen

// Android 10's "Iniciar agora": a school device prepared over the cable has
// PROJECT_MEDIA allowed (PhoneCable), so the system never asks. Should it ask
// all the same, the agent's own accessibility service presses "Iniciar
// agora" (ConsentTapper), but only on the system's request for this app
// (shown by SystemUI and naming the Midview) and only for a few seconds after
// the agent itself asked (ConsentActivity): never another app's request,
// never one the agent did not make. No Android classes (JVM tests).
internal object ConsentTap {
    const val SYSTEM_UI = "com.android.systemui"
    const val APP_NAME = "Midview"
    const val WITHIN_MILLIS = 5_000L
    const val POSITIVE_BUTTON = "android:id/button1"

    // The app's own label as a word of the request ("O Midview iniciará a
    // captura..."), not inside another app's name.
    private val OWN_LABEL = Regex("(^|\\s)" + APP_NAME + "(\\s|$)")

    // The agent asked a moment ago (requestedAt, from the same clock as now).
    fun due(requestedAt: Long?, now: Long): Boolean = requestedAt != null && now - requestedAt in 0..WITHIN_MILLIS

    // A window of SystemUI's (its MediaProjectionPermissionActivity's dialog)
    // whose texts name the Midview by its label: its capture request.
    fun isOwnRequest(windowPackage: CharSequence?, texts: List<CharSequence?>): Boolean =
        windowPackage?.toString() == SYSTEM_UI && texts.any { it != null && OWN_LABEL.containsMatchIn(it) }

    // The button that starts the capture: the dialog's positive one only.
    fun isStartButton(viewId: String?): Boolean = viewId == POSITIVE_BUTTON
}
