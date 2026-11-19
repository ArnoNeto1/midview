package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.home.Launcher3Home
import io.github.arnoneto1.midview.agent.policy.Lockdown
import io.github.arnoneto1.midview.agent.sites.FakeSiteGateway
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.time.Instant

// "Otimizar" over the network removing what the cable's "Midview otimizado"
// removes (CableApps): the system's apps hidden, the student's uninstalled,
// by the device's kind, never what is kept.
class CableAppsTest {
    @get:Rule val folder = TemporaryFolder()

    private val gw = FakeSiteGateway()
    private var tablet = false
    private val catalog = FakeBloatCatalog(
        present = setOf(
            // On every device: system apps.
            "com.google.android.googlequicksearchbox", "com.miui.daemon", "com.android.egg",
            // Only a tablet's.
            "com.google.android.keep", "com.sprd.engineermode", "com.android.fmradio",
            // Only a phone's.
            "com.miui.player", "com.android.thememanager",
            // A user app the cable's list names (an update the student installed).
            "com.google.android.apps.safetyhub",
            // Kept whatever the lists say.
            "com.google.android.apps.messaging", "com.android.dialer", "com.android.deskclock", "com.android.chrome",
        ),
        system = setOf(
            "com.google.android.googlequicksearchbox", "com.miui.daemon", "com.android.egg", "com.google.android.keep",
            "com.sprd.engineermode", "com.android.fmradio", "com.miui.player", "com.android.thememanager",
            "com.google.android.apps.messaging", "com.android.dialer", "com.android.deskclock", "com.android.chrome",
        ),
    )
    private val remover = FakeRemover(catalog)
    private val optimizer by lazy {
        Optimizer(
            catalog, gw, AdblockDns(gw, FakeDnsNetwork(), internetBlocked = { false }), OptimizeStore(File(folder.root, "optimize.txt")),
            internetBlocked = { false }, clock = { Instant.parse("2026-11-19T13:00:00Z") }, background = { it.run() },
            remover = remover, tablet = { tablet },
        )
    }

    @Test fun aPhoneLosesTheCommonListAndThePhonesOwn() {
        val report = optimizer.run()

        assertEquals(
            setOf("com.google.android.googlequicksearchbox", "com.miui.daemon", "com.android.egg", "com.miui.player", "com.android.thememanager"),
            gw.hidden,
        )
        assertEquals(listOf("com.google.android.apps.safetyhub"), report.uninstalled)
        assertTrue(report.failed.isEmpty())
    }

    @Test fun aTabletLosesTheCommonListAndTheTabletsOwn() {
        tablet = true
        val report = optimizer.run()

        assertEquals(
            setOf(
                "com.google.android.googlequicksearchbox", "com.miui.daemon", "com.android.egg",
                "com.google.android.keep", "com.sprd.engineermode", "com.android.fmradio",
            ),
            gw.hidden,
        )
        // The phone's list is not a tablet's.
        assertFalse("com.miui.player" in gw.hidden)
        assertTrue(report.uninstalled.isEmpty())
        assertTrue(remover.calls.isEmpty())
    }

    @Test fun theKeptAppsAreNeverTouched() {
        tablet = true
        optimizer.run()
        val touched = gw.hiddenCalls.map { it.first } + remover.calls.map { it.first }
        for (pkg in listOf("com.google.android.apps.messaging", "com.android.dialer", "com.android.deskclock", "com.android.chrome")) {
            assertFalse(pkg, pkg in touched)
        }
    }

    @Test fun theDevicesOwnNeverHiddenAppsStay() {
        catalog.neverHide = BloatList.NEVER_HIDE + "com.miui.daemon"
        optimizer.run()
        assertFalse("com.miui.daemon" in gw.hidden)
    }

    @Test fun whatTheCableRemovedAlreadyIsSkipped() {
        catalog.present = setOf("com.android.chrome")
        val report = optimizer.run()
        assertTrue(gw.hiddenCalls.isEmpty())
        assertTrue(report.hidden.isEmpty() && report.uninstalled.isEmpty() && report.failed.isEmpty())
    }

    @Test fun keptCoversTheAgentTheLauncherTheDockAndTheEverydayApps() {
        val kept = CableApps.KEPT
        assertTrue(Lockdown.PACKAGE in kept)
        assertTrue(Launcher3Home.LAUNCHER in kept)
        assertTrue(Launcher3Home.DOCK_APPS.all { it.pkg in kept })
        for (pkg in listOf("com.android.chrome", "com.google.android.webview", "com.android.settings", "com.google.android.inputmethod.latin", "com.google.android.gms")) {
            assertTrue(pkg, pkg in kept)
        }
        val everything = (CableApps.listed(tablet = true) + CableApps.listed(tablet = false)).toSet()
        assertEquals(emptyList<String>(), CableApps.toRemove(everything, emptySet(), tablet = true).filter { it in kept })
    }

    @Test fun eachListNamesAnAppOnce() {
        for (list in listOf(CableApps.REMOVED, CableApps.TABLET, CableApps.PHONE)) {
            assertEquals(list.size, list.toSet().size)
        }
        assertTrue(CableApps.REMOVED.none { it in CableApps.TABLET || it in CableApps.PHONE })
    }
}
