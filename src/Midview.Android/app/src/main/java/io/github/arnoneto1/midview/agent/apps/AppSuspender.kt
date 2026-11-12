package io.github.arnoneto1.midview.agent.apps

import io.github.arnoneto1.midview.agent.policy.DevicePolicyGateway
import io.github.arnoneto1.midview.agent.protocol.AppBlockMode
import io.github.arnoneto1.midview.agent.protocol.PhoneAppPolicy

// What the phone knows about its apps, read again at every apply (an app may
// have been installed, removed or the home screen changed since).
interface AppCatalog {
    // Packages with an icon on the home screen: what the student can open.
    fun launchable(): Set<String>

    // Packages never suspended (ProtectedApps and what the phone resolves).
    fun protected(): Set<String>

    // Packages suspended now.
    fun suspended(): Set<String>
}

// The phone's app block: the Device Owner suspends the blocked apps
// (greyed on the home screen, and closed if open) and lifts the suspension of
// any other. Every apply sets the whole state, so applying again changes
// nothing; an app installed later is caught by the next apply (PackageWatcher).
// log: what happened, for the phone's log (android.util.Log in the app).
class AppSuspender(
    private val catalog: AppCatalog,
    private val gw: DevicePolicyGateway,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    // A warning for the teacher when the block does not hold as asked, or null.
    @Synchronized
    fun apply(policy: PhoneAppPolicy): String? {
        if (!gw.isDeviceOwner()) return NOT_DEVICE_OWNER
        return try {
            val target = toSuspend(policy, catalog.launchable(), catalog.protected())
            val lift = toLift(target, catalog.suspended())
            if (lift.isNotEmpty()) {
                val stuck = gw.setPackagesSuspended(lift.sorted(), false)
                if (stuck.isNotEmpty()) log("Apps still suspended: $stuck", null)
            }
            val refused = if (target.isEmpty()) emptyList() else gw.setPackagesSuspended(target.sorted(), true)
            if (refused.isNotEmpty()) log("Apps Android would not suspend: $refused", null)
            log("App block applied: ${target.size} suspended, ${lift.size} lifted.", null)
            if (refused.isEmpty()) null else "${refused.size} app(s) não puderam ser bloqueados"
        } catch (e: RuntimeException) {
            log("The app block was refused", e)
            NOT_APPLIED
        }
    }

    companion object {
        const val NOT_DEVICE_OWNER = "este celular não bloqueia apps"
        const val NOT_APPLIED = "os apps não puderam ser bloqueados"

        // The apps to suspend: the listed ones ("Bloquear só estes") or all the
        // others ("Liberar só estes"), only of those the student can open, and
        // never a protected one.
        fun toSuspend(policy: PhoneAppPolicy, launchable: Set<String>, protected: Set<String>): Set<String> =
            when (policy.mode) {
                AppBlockMode.NONE -> emptySet()
                AppBlockMode.BLOCK_LIST -> launchable.intersect(policy.packages.toSet()) - protected
                AppBlockMode.ALLOW_ONLY -> launchable - protected - policy.packages.toSet()
            }

        // Suspended now and not to be: lifted (a protected app included).
        fun toLift(target: Set<String>, suspended: Set<String>): Set<String> = suspended - target
    }
}

// The apps never suspended, so the phone stays usable and managed: the agent
// itself, Android's own pieces, the home screen, Google Play services, the
// WebView, the package installer, the settings (locked by the Device Owner),
// the phone and emergency calls, the keyboard, and MIUI's power, security and
// notification apps. What a phone resolves (its home screen, dialer,
// keyboards) is added by InstalledApps.
object ProtectedApps {
    val ALWAYS: Set<String> = setOf(
        "io.github.arnoneto1.midview.agent",
        "android",
        "com.android.systemui",
        // Home screens.
        "com.miui.home",
        "com.android.launcher3",
        "com.google.android.apps.nexuslauncher",
        // Google Play services and the WebView.
        "com.google.android.gms",
        "com.google.android.gsf",
        "com.google.android.webview",
        "com.android.webview",
        // Installing and Android's permission questions.
        "com.android.packageinstaller",
        "com.google.android.packageinstaller",
        "com.miui.packageinstaller",
        "com.android.permissioncontroller",
        "com.google.android.permissioncontroller",
        "com.lbe.security.miui",
        "com.android.settings",
        // Calls and emergency.
        "com.android.phone",
        "com.android.server.telecom",
        "com.android.emergency",
        "com.android.incallui",
        "com.android.dialer",
        "com.google.android.dialer",
        // MIUI's power, security and notification apps.
        "com.miui.securitycenter",
        "com.miui.powerkeeper",
        "com.xiaomi.joyose",
        "com.miui.notification",
    )

    fun with(resolved: Iterable<String?>): Set<String> = ALWAYS + resolved.filterNotNull()
}
