package io.github.arnoneto1.midview.agent.policy

import io.github.arnoneto1.midview.agent.optimize.PrivateDns
import io.github.arnoneto1.midview.agent.sites.AlwaysOnVpn

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

    // The accessibility services allowed (null: all of them).
    fun setPermittedAccessibilityServices(pkgs: List<String>?)

    // Suspends (or lifts the suspension of) apps; returns those Android
    // would not change.
    fun setPackagesSuspended(pkgs: List<String>, suspended: Boolean): List<String>

    // An app's managed configuration (Chrome's policies), each value a
    // String, a Boolean or an Array<String>; empty clears it.
    fun setApplicationRestrictions(pkg: String, restrictions: Map<String, Any>)

    // Hides (or shows again) an app; false when Android would not, and also
    // when the app already was so.
    fun setApplicationHidden(pkg: String, hidden: Boolean): Boolean

    // Whether an app is hidden now.
    fun isApplicationHidden(pkg: String): Boolean

    // The phone's private DNS ("Otimizar"): as Settings has it, set to a
    // host (strict) or back to automatic (opportunistic). Each set returns a
    // DevicePolicyManager.PRIVATE_DNS_SET_* value; setting a host first
    // connects to it, so never on the main thread.
    fun privateDns(): PrivateDns
    fun setPrivateDnsHost(host: String): Int
    fun setPrivateDnsAutomatic(): Int

    // The same two settings written directly (the host, then the mode),
    // without Android's check of the host; throws when Android refuses.
    fun writePrivateDnsHost(host: String)

    // The phone's always-on VPN (null: none), and setting it.
    fun alwaysOnVpn(): AlwaysOnVpn?
    fun setAlwaysOnVpn(vpn: AlwaysOnVpn?)

    // Saves a Wi-Fi network and joins it (WPA2 with a password, open without).
    fun addWifi(ssid: String, password: String?)
    fun isDeviceOwner(): Boolean

    // Erases the phone back to factory settings, keeping the data of a
    // factory reset protection out of it (so the next owner is not asked for
    // the old account). Device Owner only; the phone restarts into setup.
    fun wipe()
    val sdkInt: Int

    // A debug build (ApplicationInfo.FLAG_DEBUGGABLE).
    val debuggable: Boolean
}

// The locks of a school phone, so a student cannot remove the agent or get
// around it: no factory reset, no developer options or USB debugging, no
// safe mode, no other users, no files over USB, no changing the private DNS
// or the VPN (the internet block is the agent's VPN), no installing apps from
// outside a store (an APK downloaded in Chrome) and no adding or removing
// accounts (a Google account would open the Play Store, and a Google or Mi
// account would tie the phone to the student after a reset), the app cannot be
// uninstalled, force-stopped nor have its data cleared. The teacher's
// "Bloquear telas" is allowed too: this app alone may hold the phone in lock
// task mode, with nothing of the system's left to reach (no home, recents,
// notifications nor power menu). Of the accessibility services only the
// app's own (it shows the teacher the screen without asking the student)
// may be turned on, so no app the student installs can tap for them;
// Android keeps the phone's own (TalkBack and the like) anyway. Only a Device Owner
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
        step { gw.setPermittedAccessibilityServices(listOf(packageName)) }
        refused?.let { throw it }
        return true
    }

    companion object {
        const val PACKAGE = "io.github.arnoneto1.midview.agent"

        // The values of UserManager.DISALLOW_FACTORY_RESET,
        // DISALLOW_DEBUGGING_FEATURES, DISALLOW_SAFE_BOOT, DISALLOW_ADD_USER,
        // DISALLOW_USB_FILE_TRANSFER, DISALLOW_CONFIG_PRIVATE_DNS,
        // DISALLOW_CONFIG_VPN, DISALLOW_INSTALL_UNKNOWN_SOURCES,
        // DISALLOW_INSTALL_UNKNOWN_SOURCES_GLOBALLY (Android 10, the oldest
        // the agent runs on) and DISALLOW_MODIFY_ACCOUNTS. no_config_vpn keeps
        // the student out of the VPN settings, where the agent's VPN is while
        // the internet is blocked; Android still starts an always-on VPN the
        // Device Owner sets. The unknown sources ones grey "Instalar apps
        // desconhecidos" for every app, so an APK a student downloads cannot
        // be installed (an app the block does not know, such as another
        // YouTube client). adb is not an unknown source, so debug builds keep
        // them too: `adb install -r` of a debug build still works with them
        // on (checked on the Redmi 10C).
        val RESTRICTIONS = listOf(
            "no_factory_reset",
            "no_debugging_features",
            "no_safe_boot",
            "no_add_user",
            "no_usb_file_transfer",
            "disallow_config_private_dns",
            "no_config_vpn",
            "no_install_unknown_sources",
            "no_install_unknown_sources_globally",
            "no_modify_accounts",
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
