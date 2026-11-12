package io.github.arnoneto1.midview.agent.apps

import android.content.pm.ApplicationInfo
import android.content.pm.PackageManager
import android.os.Build

// Where an app on the phone came from, for "Desinstalar programa", which
// removes only what the Midview itself installed ("Instalar programa"): a
// list of what may go, never a guess at what came with the phone (install
// dates vary by maker and move with every system update).
// cameWithPhone: a package of the system image, or an update of one (what
// PackageManager's MATCH_FACTORY_ONLY, hidden from apps, would find).
// installedByMidview: Android records the agent as the app's installer. A
// maker's "data-app" (MIUI's Notes, Weather) is neither: it has no installer,
// so it stays; one the Midview installed again over its name is the Midview's.
class AppOrigin(private val pm: PackageManager, private val selfPackage: String) {
    fun cameWithPhone(info: ApplicationInfo): Boolean =
        (info.flags and (ApplicationInfo.FLAG_SYSTEM or ApplicationInfo.FLAG_UPDATED_SYSTEM_APP)) != 0

    fun installedByMidview(pkg: String): Boolean = installer(pkg) == selfPackage

    // Null when Android does not say (an app the maker put on the phone, one
    // installed over adb), or the app is not there.
    @Suppress("DEPRECATION")
    fun installer(pkg: String): String? =
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                pm.getInstallSourceInfo(pkg).installingPackageName
            } else {
                pm.getInstallerPackageName(pkg)
            }
        } catch (_: PackageManager.NameNotFoundException) {
            null
        } catch (_: IllegalArgumentException) {
            null
        }
}
