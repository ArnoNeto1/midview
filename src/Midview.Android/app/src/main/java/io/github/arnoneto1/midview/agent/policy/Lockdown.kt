package io.github.arnoneto1.midview.agent.policy

// What the school phone's locks need from Android's DevicePolicyManager,
// behind an interface so the decisions are tested on the JVM.
interface DevicePolicyGateway {
    fun addUserRestriction(r: String)
    fun setUninstallBlocked(pkg: String, blocked: Boolean)
    fun setUserControlDisabled(pkgs: List<String>)
    fun grantPermission(pkg: String, permission: String)
    fun isDeviceOwner(): Boolean
    val sdkInt: Int
}

// The locks of a school phone, so a student cannot remove the agent or get
// around it: no factory reset, no developer options or USB debugging, no
// safe mode, no other users, no files over USB, the app cannot be
// uninstalled and, from Android 11, cannot be force-stopped nor have its
// data cleared. Only a Device Owner can set these; on any other phone it
// does nothing. Every call sets a state, so applying again changes nothing.
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

        RESTRICTIONS.forEach { r -> step { gw.addUserRestriction(r) } }
        step { gw.setUninstallBlocked(packageName, true) }
        if (gw.sdkInt >= ANDROID_11) {
            step { gw.setUserControlDisabled(listOf(packageName)) }
        }
        // The pairing code goes in a notification; on a school phone nobody
        // is there to answer the permission question.
        if (gw.sdkInt >= ANDROID_13) {
            step { gw.grantPermission(packageName, POST_NOTIFICATIONS) }
        }
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

        // Manifest.permission.POST_NOTIFICATIONS.
        const val POST_NOTIFICATIONS = "android.permission.POST_NOTIFICATIONS"

        private const val ANDROID_11 = 30
        private const val ANDROID_13 = 33
    }
}
