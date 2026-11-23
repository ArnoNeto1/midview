package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.apps.ProtectedApps
import io.github.arnoneto1.midview.agent.home.Launcher3Home
import io.github.arnoneto1.midview.agent.policy.Lockdown
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

// Which apps the teacher's "Otimizar" takes off as installed by someone
// (InstalledAppsSweep), and every app it keeps.
class InstalledAppsSweepTest {
    private fun fromPlayStore(pkg: String) = DeviceApp(pkg, cameWithPhone = false, installer = PLAY_STORE)

    private fun remove(vararg apps: DeviceApp, kept: Set<String> = emptySet()) =
        InstalledAppsSweep.toRemove(apps.toList(), kept)

    @Test fun anAppFromThePlayStoreGoes() {
        assertEquals(listOf(POU), remove(fromPlayStore(POU)))
    }

    // An APK downloaded in the browser, or another store: the installer is recorded.
    @Test fun anAppFromADownloadedApkOrAnotherStoreGoes() {
        val apk = DeviceApp("com.example.apk", cameWithPhone = false, installer = "com.google.android.packageinstaller")
        val store = DeviceApp("com.example.store", cameWithPhone = false, installer = "com.xiaomi.mipicks")
        assertEquals(listOf("com.example.apk", "com.example.store"), remove(apk, store))
    }

    @Test fun anAppOfTheSystemImageStays() {
        assertEquals(emptyList<String>(), remove(DeviceApp("com.miui.compass", cameWithPhone = true, installer = PLAY_STORE)))
    }

    // The maker's data-apps and the cable's adb installs record no installer.
    @Test fun anAppWithNoInstallerRecordedStays() {
        assertEquals(emptyList<String>(), remove(DeviceApp("com.miui.notes.data", cameWithPhone = false, installer = null)))
    }

    // The teacher's "Instalar programa": Android records the agent as installer.
    @Test fun anAppTheMidviewInstalledStays() {
        assertEquals(emptyList<String>(), remove(DeviceApp("br.escola.app", cameWithPhone = false, installer = Lockdown.PACKAGE)))
    }

    @Test fun theAgentStays() {
        assertEquals(emptyList<String>(), remove(DeviceApp(Lockdown.PACKAGE, cameWithPhone = false, installer = "com.android.shell")))
        assertEquals(emptyList<String>(), remove(fromPlayStore(Lockdown.PACKAGE)))
    }

    @Test fun theVettedProtectedAppsStay() {
        for (pkg in UnusedApps.PROTECTED) assertEquals(pkg, emptyList<String>(), remove(fromPlayStore(pkg)))
    }

    @Test fun theNeverHiddenAppsStay() {
        for (pkg in BloatList.NEVER_HIDE) assertEquals(pkg, emptyList<String>(), remove(fromPlayStore(pkg)))
    }

    // Chrome, the Play Store and Google Play services, by name.
    @Test fun chromeThePlayStoreAndPlayServicesStay() {
        val apps = listOf("com.android.chrome", "com.android.vending", "com.google.android.gms", "com.google.android.gsf")
        assertEquals(emptyList<String>(), remove(*apps.map(::fromPlayStore).toTypedArray()))
    }

    @Test fun theDockAppsAndTheLauncherStay() {
        val dock = Launcher3Home.DOCK_APPS.map { it.pkg }
        assertEquals(
            listOf("com.android.chrome", "com.android.camera2", "com.google.android.apps.photos", "com.google.android.apps.nbu.files", "com.google.android.calculator"),
            dock,
        )
        for (pkg in dock + Launcher3Home.LAUNCHER) assertEquals(pkg, emptyList<String>(), remove(fromPlayStore(pkg)))
    }

    @Test fun androidsOwnPiecesStay() {
        for (pkg in ProtectedApps.ALWAYS) assertEquals(pkg, emptyList<String>(), remove(fromPlayStore(pkg)))
    }

    // The device's own: a keyboard in use, the WebView, the default home
    // (BloatList.neverHide), and a device admin, as the sweep is handed them.
    @Test fun whatTheDeviceKeepsStays() {
        val keyboard = fromPlayStore("com.touchtype.swiftkey")
        val webView = fromPlayStore("com.google.android.webview.beta")
        val home = fromPlayStore("com.teslacoilsw.launcher")
        val admin = fromPlayStore("com.example.mdm")
        val kept = setOf(keyboard.pkg, webView.pkg, home.pkg, admin.pkg)
        assertEquals(listOf(POU), remove(keyboard, webView, home, admin, fromPlayStore(POU), kept = kept))
    }

    @Test fun eachOnceInOrder() {
        assertEquals(listOf("a.b", POU), remove(fromPlayStore(POU), fromPlayStore("a.b"), fromPlayStore(POU)))
    }

    // Read through the device: its never-hidden apps, its admins and the
    // apps the listed steps see to are kept.
    @Test fun theTargetsKeepTheDevicesOwnAdminsAndTheListedApps() {
        val catalog = FakeBloatCatalog(present = setOf(POU, KEYBOARD, MDM, TIKTOK), neverHide = BloatList.NEVER_HIDE + KEYBOARD)
        val device = FakeDeviceApps(catalog, listOf(POU, KEYBOARD, MDM, TIKTOK).map(::fromPlayStore), admins = setOf(MDM))
        assertEquals(listOf(POU), InstalledAppsSweep(device, catalog).targets(handled = listOf(TIKTOK)))
    }

    // Nothing is removed on a guess.
    @Test fun whatCannotBeReadRemovesNothing() {
        val catalog = FakeBloatCatalog(present = setOf(POU))
        val device = FakeDeviceApps(catalog, listOf(fromPlayStore(POU)))
        device.failing = IllegalStateException("no")
        val logged = mutableListOf<String>()
        assertEquals(emptyList<String>(), InstalledAppsSweep(device, catalog, log = { m, _ -> logged += m }).targets(emptyList()))
        assertTrue(logged.isNotEmpty())

        val neverHideFails = object : BloatCatalog by catalog {
            override fun neverHide(): Set<String> = throw IllegalStateException("no")
        }
        assertEquals(emptyList<String>(), InstalledAppsSweep(FakeDeviceApps(catalog, listOf(fromPlayStore(POU))), neverHideFails).targets(emptyList()))
        assertEquals(emptyList<String>(), InstalledAppsSweep(null, catalog).targets(emptyList()))
    }

    private companion object {
        const val PLAY_STORE = "com.android.vending"
        const val POU = "me.pou.app"
        const val KEYBOARD = "com.touchtype.swiftkey"
        const val MDM = "com.example.mdm"
        const val TIKTOK = "com.zhiliaoapp.musically"
    }
}
