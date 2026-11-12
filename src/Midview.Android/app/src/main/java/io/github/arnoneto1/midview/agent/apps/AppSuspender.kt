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

    // Still on the phone (an app may be removed while the block is applied).
    fun isInstalled(pkg: String): Boolean
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
            val suspended = catalog.suspended()
            val target = toSuspend(policy, catalog.launchable(), catalog.protected())
            val lift = toLift(target, suspended)
            if (lift.isNotEmpty()) {
                val stuck = gw.setPackagesSuspended(lift.sorted(), false)
                if (stuck.isNotEmpty()) log("Apps still suspended: $stuck", null)
            }
            // Only what is not suspended yet; an app removed in the meantime
            // has nothing left to block.
            val added = target - suspended
            val refused = if (added.isEmpty()) {
                emptyList()
            } else {
                gw.setPackagesSuspended(added.sorted(), true).filter { catalog.isInstalled(it) }
            }
            if (refused.isNotEmpty()) log("Apps Android would not suspend: $refused", null)
            log("App block applied: ${target.size} blocked (${added.size} new), ${lift.size} lifted.", null)
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
        // "Suspended now" also counts apps another app on the phone suspended
        // (a digital wellbeing timer, say): that is harmless, since Android
        // lifts only the Device Owner's own suspension and leaves theirs (they
        // come back as still suspended, logged and asked again at each apply).
        // Likewise an app to block that is suspended by someone else is not
        // asked again; the next apply catches it once they lift theirs.
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
        // Play Store: the package verifier, which Android refuses to suspend.
        "com.android.vending",
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

    // An app the phone names for a role (home screen, dialer, settings,
    // installer, keyboard), and whether it came with the phone.
    data class Candidate(val pkg: String, val system: Boolean)

    // ALWAYS, the phone's own apps for those roles (system apps), and the
    // ones in use (default home, dialer and keyboard, the WebView, the agent).
    // An app the student installs that only claims a role is not protected:
    // it is listed for the teacher and can be blocked.
    fun resolve(candidates: Iterable<Candidate>, inUse: Iterable<String?>): Set<String> =
        ALWAYS + candidates.filter { it.system }.map { it.pkg } + inUse.filterNotNull()

    // The package of Settings.Secure.DEFAULT_INPUT_METHOD ("package/.Service").
    fun inputMethodPackage(id: String?): String? =
        id?.substringBefore('/')?.takeIf { it.isNotEmpty() }
}
