package io.github.arnoneto1.midview.agent.policy

// What the school phone's locks need from Android's DevicePolicyManager,
// behind an interface so the decisions are tested on the JVM.
interface DevicePolicyGateway {
    fun addUserRestriction(r: String)
    fun setUninstallBlocked(pkg: String, blocked: Boolean)
    fun setUserControlDisabled(pkgs: List<String>)
    fun grantPermission(pkg: String, permission: String)

    // The apps allowed to hold the phone in lock task mode.
    fun setLockTaskPackages(pkgs: List<String>)

    // What the system keeps in lock task mode (DevicePolicyManager.LOCK_TASK_FEATURE_*).
    fun setLockTaskFeatures(features: Int)
    fun isDeviceOwner(): Boolean
    val sdkInt: Int

    // A debug build (ApplicationInfo.FLAG_DEBUGGABLE).
    val debuggable: Boolean
}

// The locks of a school phone, so a student cannot remove the agent or get
// around it: no factory reset, no developer options or USB debugging, no
// safe mode, no other users, no files over USB, the app cannot be
// uninstalled, force-stopped nor have its data cleared. The teacher's
// "Bloquear telas" is allowed too: this app alone may hold the phone in lock
// task mode, with nothing of the system's left to reach (no home, recents,
// notifications nor power menu). Only a Device Owner
// can set these; on any other phone it does nothing. Every call sets a
// state, so applying again changes nothing.
//
// A debug build keeps adb and factory reset, so a test phone or emulator
// made Device Owner can still be reached and reset. The APK published for
// the Master's QR code must be the release build.
class Lockdown(
    private val gw: DevicePolicyGateway,
    private val packageName: String = PACKAGE,
) {
    // True when the locks were applied (the app is the Device Owner). A step
    // the system refuses does not stop the others; the first refusal is
    // thrown at the end.
    fun apply(): Boolean {
        if (!gw.isDeviceOwner()) return false
        var refused: RuntimeException? = null
        fun step(action: () -> Unit) {
            try {
                action()
            } catch (e: RuntimeException) {
                if (refused == null) refused = e
            }
        }

        RESTRICTIONS
            .filterNot { gw.debuggable && it in KEPT_ON_DEBUG_BUILDS }
            .forEach { r -> step { gw.addUserRestriction(r) } }
        step { gw.setUninstallBlocked(packageName, true) }
        if (gw.sdkInt >= ANDROID_11) {
            step { gw.setUserControlDisabled(listOf(packageName)) }
        } else {
            // Android 10 has no per-app control lock: no app can be
            // force-stopped nor have its data cleared.
            step { gw.addUserRestriction(DISALLOW_APPS_CONTROL) }
        }
        // The pairing code goes in a notification; on a school phone nobody
        // is there to answer the permission question.
        if (gw.sdkInt >= ANDROID_13) {
            step { gw.grantPermission(packageName, POST_NOTIFICATIONS) }
        }
        step { gw.setLockTaskPackages(listOf(packageName)) }
        step { gw.setLockTaskFeatures(LOCK_TASK_FEATURE_NONE) }
        refused?.let { throw it }
        return true
    }

    companion object {
        const val PACKAGE = "io.github.arnoneto1.midview.agent"

        // The values of UserManager.DISALLOW_FACTORY_RESET,
        // DISALLOW_DEBUGGING_FEATURES, DISALLOW_SAFE_BOOT, DISALLOW_ADD_USER
        // and DISALLOW_USB_FILE_TRANSFER.
        val RESTRICTIONS = listOf(
            "no_factory_reset",
            "no_debugging_features",
            "no_safe_boot",
            "no_add_user",
            "no_usb_file_transfer",
        )

        // UserManager.DISALLOW_FACTORY_RESET and DISALLOW_DEBUGGING_FEATURES,
        // left off on debug builds.
        val KEPT_ON_DEBUG_BUILDS = setOf("no_factory_reset", "no_debugging_features")

        // UserManager.DISALLOW_APPS_CONTROL, only before Android 11.
        const val DISALLOW_APPS_CONTROL = "no_control_apps"

        // Manifest.permission.POST_NOTIFICATIONS.
        const val POST_NOTIFICATIONS = "android.permission.POST_NOTIFICATIONS"

        // DevicePolicyManager.LOCK_TASK_FEATURE_NONE.
        const val LOCK_TASK_FEATURE_NONE = 0

        private const val ANDROID_11 = 30
        private const val ANDROID_13 = 33
    }
}
