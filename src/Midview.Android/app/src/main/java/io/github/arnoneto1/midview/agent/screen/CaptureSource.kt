package io.github.arnoneto1.midview.agent.screen

// Where the teacher's view of the screen comes from. On a school phone the
// app's accessibility service (AccessibilityCapture), turned on once by
// cable while the phone is prepared, takes the pictures (Android 11 and
// newer) and the student is never asked; otherwise the student's "Iniciar
// agora" (MediaProjection, ScreenCaptureService). AccessibilityStarting: on
// in the settings but not connected yet (the phone or the app just
// started), so the student is not asked meanwhile. No Android classes (JVM
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

        fun choose(sdkInt: Int, enabled: Boolean, connected: Boolean): CaptureSource = when {
            sdkInt < ANDROID_11 -> MediaProjection
            connected -> Accessibility
            enabled -> AccessibilityStarting
            else -> MediaProjection
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
