package io.github.arnoneto1.midview.agent.screen

// Where the teacher's view of the screen comes from. On a school phone the
// app's accessibility service (AccessibilityCapture), turned on once by
// cable while the phone is prepared, takes the pictures (Android 11 and
// newer) and the student is never asked; otherwise the student's "Iniciar
// agora" (MediaProjection, ScreenCaptureService). AccessibilityStarting: on
// in the settings but not connected yet (the phone or the app just
// started), so the student is not asked meanwhile; for STARTING_LIMIT_MILLIS
// at most, then the student is asked after all. No Android classes (JVM
// tests).
enum class CaptureSource {
    MediaProjection,
    AccessibilityStarting,
    Accessibility,
    ;

    companion object {
        const val PACKAGE = "io.github.arnoneto1.midview.agent"
        const val SERVICE_CLASS = "$PACKAGE.screen.AccessibilityCapture"

        // AccessibilityService.takeScreenshot: Android 11.
        const val ANDROID_11 = 30

        // How long a service on in the settings may take to connect before
        // the student is asked instead (it may never connect).
        const val STARTING_LIMIT_MILLIS = 30_000L

        // startingSinceMillis: when the service was first seen on but not
        // connected (null: just now).
        fun choose(
            sdkInt: Int,
            enabled: Boolean,
            connected: Boolean,
            startingSinceMillis: Long? = null,
            nowMillis: Long = 0L,
        ): CaptureSource = when {
            sdkInt < ANDROID_11 -> MediaProjection
            connected -> Accessibility
            !enabled -> MediaProjection
            startingSinceMillis != null && nowMillis - startingSinceMillis >= STARTING_LIMIT_MILLIS -> MediaProjection
            else -> AccessibilityStarting
        }

        // Whether Settings.Secure's enabled_accessibility_services (services
        // as package/class, ':' between them; the class may start with '.'
        // for one in the package) lists this app's service, and
        // accessibility_enabled is on.
        fun enabledInSettings(services: String?, accessibilityOn: Boolean): Boolean {
            if (!accessibilityOn || services.isNullOrEmpty()) return false
            return services.split(':').any { entry ->
                val slash = entry.indexOf('/')
                if (slash <= 0) return@any false
                val pkg = entry.substring(0, slash)
                val cls = entry.substring(slash + 1).let { if (it.startsWith('.')) pkg + it else it }
                pkg == PACKAGE && cls == SERVICE_CLASS
            }
        }
    }
}

// CaptureSource.choose with the time the service was first seen starting
// kept: the count starts then (giveUpLater is asked once to look again
// after the limit, ScreenConsent.sourceChanged) and starts over once it
// connects or is turned off. Safe from any thread.
class CaptureSourceWatch(
    private val sdkInt: Int,
    private val clock: () -> Long,
    private val giveUpLater: (delayMillis: Long) -> Unit,
) {
    private var startingSince: Long? = null

    @Synchronized
    fun current(enabled: Boolean, connected: Boolean): CaptureSource {
        val now = clock()
        if (connected || !enabled || sdkInt < CaptureSource.ANDROID_11) {
            startingSince = null
        } else if (startingSince == null) {
            startingSince = now
            giveUpLater(CaptureSource.STARTING_LIMIT_MILLIS)
        }
        return CaptureSource.choose(sdkInt, enabled, connected, startingSince, now)
    }
}
