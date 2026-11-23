package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.apps.ProtectedApps
import io.github.arnoneto1.midview.agent.policy.Lockdown

// An app installed on the device for its user (a hidden one too), as the
// sweep needs it: whether it came with the system image (FLAG_SYSTEM or
// FLAG_UPDATED_SYSTEM_APP, AppOrigin.cameWithPhone) and the package Android
// records as its installer (AppOrigin.installer; null when it records none).
data class DeviceApp(val pkg: String, val cameWithPhone: Boolean, val installer: String?)

// What the sweep reads of the device (InstalledApps), behind an interface so
// the selection is tested on the JVM.
interface DeviceApps {
    // Every app installed for this user, hidden ones too.
    fun all(): List<DeviceApp>

    // The packages of the active device admins (the agent among them).
    fun deviceAdmins(): Set<String>
}

// The school asked that every device stay as a freshly optimized one: "não
// pode ter outros aplicativos". The Play Store stays usable, so a student can
// still install a game (Pou 3D was found); the teacher's "Otimizar" then takes
// it off again. After the listed apps (OptimizePlan), the teacher's
// "Otimizar" uninstalls every app someone installed on the device: one that
// is not of the system image and whose installer Android records (the Play
// Store, the package installer behind a downloaded APK, another store). Only
// at the teacher's press (Optimizer.run), never in the optimization the agent
// runs by itself after pairing: the teacher is told on the "Otimizar" card
// that apps go, and that first run has no teacher who read it.
//
// Kept, whatever was installed:
// - the agent, and every app it installed (the teacher's "Instalar
//   programa": Android records the agent as the installer, AppOrigin), the
//   same record "Desinstalar programa" trusts;
// - an app with no installer recorded: the maker's "data-apps" (MIUI's Notes
//   and Weather come so, not as system apps) and what the cable installed
//   over adb were on the device as it was optimized, and a student cannot
//   install that way (USB debugging stays locked);
// - every app the device needs or the class uses: CableApps.KEPT (the vetted
//   UnusedApps.PROTECTED, BloatList.NEVER_HIDE with Chrome, the Play Store
//   and Google Play services, Android's own launcher and the dock's Chrome,
//   Câmera, Fotos, Files and Calculadora), ProtectedApps.ALWAYS, and this
//   device's never-hidden apps (BloatList.neverHide: the home screen,
//   keyboard and WebView in use, its cameras, a gallery kept);
// - any device admin, whose removal Android refuses or would leave the
//   device managed by nobody;
// - the apps the listed steps see to (handled): uninstalled, or hidden when
//   Android refuses, there.
// Any of these that cannot be read stops the sweep: nothing is removed on a
// guess.
class InstalledAppsSweep(
    private val source: DeviceApps?,
    private val catalog: BloatCatalog,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    // The packages to uninstall now, in a stable order; none without a
    // source (not wired) or when the device cannot be read.
    fun targets(handled: Collection<String>): List<String> {
        if (source == null) return emptyList()
        return try {
            val kept = catalog.neverHide() + source.deviceAdmins() + handled
            toRemove(source.all(), kept)
        } catch (e: Exception) {
            log("The installed apps could not be read: none uninstalled", e)
            emptyList()
        }
    }

    companion object {
        // Kept on every device, on top of each device's own (kept).
        val ALWAYS_KEPT: Set<String> = CableApps.KEPT + ProtectedApps.ALWAYS + Lockdown.PACKAGE

        // The apps someone installed, none kept, the agent's own installs
        // aside; each once, sorted.
        fun toRemove(apps: Collection<DeviceApp>, kept: Set<String>, self: String = Lockdown.PACKAGE): List<String> =
            apps.asSequence()
                .filter { !it.cameWithPhone && it.installer != null && it.installer != self }
                .map { it.pkg }
                .filter { it != self && it !in kept && it !in ALWAYS_KEPT }
                .distinct()
                .sorted()
                .toList()
    }
}
