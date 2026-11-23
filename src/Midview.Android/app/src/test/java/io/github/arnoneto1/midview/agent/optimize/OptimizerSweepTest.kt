package io.github.arnoneto1.midview.agent.optimize

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
import java.util.concurrent.TimeUnit

// The teacher's "Otimizar" also uninstalling the apps someone installed
// (InstalledAppsSweep), after the listed ones and in the same time; never the
// agent's own first optimization after pairing.
class OptimizerSweepTest {
    @get:Rule val folder = TemporaryFolder()

    private val gw = FakeSiteGateway()
    private var nanos = 0L
    private val catalog = FakeBloatCatalog(present = setOf(CANDY, POU, GAME, BY_MIDVIEW, NOTES, CHROME, "com.miui.msa.global"))
    private val device = FakeDeviceApps(
        catalog,
        listOf(
            DeviceApp(CANDY, cameWithPhone = false, installer = PLAY_STORE),
            DeviceApp(POU, cameWithPhone = false, installer = PLAY_STORE),
            DeviceApp(GAME, cameWithPhone = false, installer = "com.google.android.packageinstaller"),
            DeviceApp(BY_MIDVIEW, cameWithPhone = false, installer = Lockdown.PACKAGE),
            DeviceApp(NOTES, cameWithPhone = false, installer = null),
            DeviceApp(CHROME, cameWithPhone = true, installer = PLAY_STORE),
        ),
    )
    private val remover = FakeRemover(catalog)
    private val file by lazy { File(folder.root, "optimize.txt") }
    private val store by lazy { OptimizeStore(file) }
    private val queued = mutableListOf<Runnable>()
    private val optimizer by lazy {
        Optimizer(
            catalog, gw, AdblockDns(gw, FakeDnsNetwork(), internetBlocked = { false }), store,
            internetBlocked = { false }, clock = { Instant.parse("2026-11-23T13:00:00Z") }, background = { queued += it },
            remover = remover, ticker = { nanos }, deviceApps = device,
        )
    }

    private fun runQueued() {
        while (queued.isNotEmpty()) queued.removeAt(0).run()
    }

    @Test fun theTeachersOptimizeUninstallsTheAppsSomeoneInstalledAfterTheListedOnes() {
        val report = optimizer.run()

        assertEquals(listOf(CANDY, GAME, POU), remover.calls.map { it.first })
        assertEquals(listOf(CANDY, GAME, POU), report.uninstalled)
        assertEquals(listOf(GAME, POU), report.userInstalled)
        assertTrue(catalog.present.containsAll(listOf(BY_MIDVIEW, NOTES, CHROME)))
        assertEquals(report, OptimizeStore(file).current.report)
    }

    @Test fun theAgentsOwnFirstOptimizationLeavesThemAlone() {
        optimizer.teacherSeen()
        runQueued()

        assertEquals(listOf(CANDY), remover.calls.map { it.first })
        assertTrue(POU in catalog.present && GAME in catalog.present)
        assertEquals(emptyList<String>(), store.current.report.userInstalled)
    }

    // After the first run, the teacher's "Otimizar" still takes them off.
    @Test fun theTeachersOptimizeAfterTheFirstOneStillSweeps() {
        optimizer.teacherSeen()
        runQueued()
        val report = optimizer.run()
        assertEquals(listOf(GAME, POU), report.userInstalled)
        assertFalse(POU in catalog.present)
    }

    // Gone ones stay counted; one put back is taken off again, counted once.
    @Test fun anAppInstalledAgainIsTakenOffAgain() {
        optimizer.run()
        remover.calls.clear()
        assertEquals(listOf(GAME, POU), optimizer.run().userInstalled)
        assertTrue(remover.calls.isEmpty())

        catalog.present += POU
        val report = optimizer.run()
        assertEquals(listOf(POU), remover.calls.map { it.first })
        assertEquals(listOf(GAME, POU), report.userInstalled)
    }

    // The same time as the listed ones: the rest go right after, in the
    // background, and are counted as installed by someone there.
    @Test fun whatIsLeftIsUninstalledInTheBackgroundAndCounted() {
        remover.before = { nanos += TimeUnit.SECONDS.toNanos(30) }
        val report = optimizer.run()
        assertEquals(listOf(CANDY, GAME), report.uninstalled)
        assertEquals(listOf(GAME), report.userInstalled)
        assertEquals(1, report.pending)

        runQueued()
        val kept = store.current.report
        assertEquals(listOf(CANDY, GAME, POU), kept.uninstalled)
        assertEquals(listOf(GAME, POU), kept.userInstalled)
        assertEquals(0, kept.pending)
    }

    // The background left by the first run only finishes the listed apps.
    @Test fun theFirstRunsBackgroundLeavesThemAloneToo() {
        remover.before = { nanos += TimeUnit.SECONDS.toNanos(30) }
        catalog.present += setOf(WOW, NETFLIX)
        optimizer.teacherSeen()
        // The first run, then its background for what it left.
        queued.removeAt(0).run()
        assertTrue(NETFLIX in catalog.present)
        assertEquals(1, queued.size)
        runQueued()
        assertFalse(WOW in catalog.present || NETFLIX in catalog.present)
        assertTrue(POU in catalog.present && GAME in catalog.present)
        assertEquals(listOf(CANDY, WOW, NETFLIX), remover.calls.map { it.first })
    }

    // The teacher's "Otimizar" leaves some while the first run's background
    // is still to work: that background goes on to them.
    @Test fun whatTheTeacherLeftWhileTheFirstRunsBackgroundWaitsIsSweptByIt() {
        remover.before = { nanos += TimeUnit.SECONDS.toNanos(30) }
        catalog.present += setOf(WOW, NETFLIX)
        optimizer.teacherSeen()
        queued.removeAt(0).run()
        assertEquals(1, queued.size)

        val report = optimizer.run()
        assertEquals(listOf(GAME), report.userInstalled)
        assertTrue(POU in catalog.present)
        // No second background: the first one takes POU too.
        assertEquals(1, queued.size)
        runQueued()
        assertFalse(POU in catalog.present)
        assertEquals(listOf(GAME, POU), store.current.report.userInstalled)
        assertEquals(0, store.current.report.pending)
    }

    // Android refuses: hidden instead, like a listed app.
    @Test fun anAppAndroidWillNotUninstallIsHidden() {
        remover.refuse = mapOf(POU to "o Android não deixou desinstalar")
        val report = optimizer.run()
        assertTrue(POU in report.hidden && POU in gw.hidden)
        assertEquals(listOf(GAME), report.userInstalled)
    }

    @Test fun aDeviceAdminStays() {
        device.admins = setOf(POU)
        assertEquals(listOf(GAME), optimizer.run().userInstalled)
        assertTrue(POU in catalog.present)
    }

    @Test fun notTheSchoolsDeviceNothingIsUninstalled() {
        gw.owner = false
        val report = optimizer.run()
        assertTrue(remover.calls.isEmpty())
        assertEquals(emptyList<String>(), report.userInstalled)
    }

    private companion object {
        const val PLAY_STORE = "com.android.vending"
        const val CANDY = "com.king.candycrushsaga"
        const val WOW = "com.fugo.wow"
        const val NETFLIX = "com.netflix.mediaclient"
        const val POU = "me.pou.app"
        const val GAME = "com.example.game"
        const val BY_MIDVIEW = "br.escola.app"
        const val NOTES = "com.miui.notes.data"
        const val CHROME = "com.android.chrome"
    }
}
