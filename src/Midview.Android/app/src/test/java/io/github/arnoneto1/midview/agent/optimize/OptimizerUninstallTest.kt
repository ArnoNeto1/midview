package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.protocol.PhoneDnsStates
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

// "Otimizar" uninstalling the apps the school does not use (UnusedApps).
class OptimizerUninstallTest {
    @get:Rule val folder = TemporaryFolder()

    private val gw = FakeSiteGateway()
    private val now = Instant.parse("2026-11-12T13:00:00Z")
    private var nanos = 0L
    private val catalog = FakeBloatCatalog(
        present = setOf(
            CANDY, TIKTOK, BOOKING, YOUTUBE, GMAIL,
            "com.miui.msa.global", "com.android.chrome", "com.whatsapp", "com.miui.calculator",
        ),
        system = setOf(YOUTUBE, GMAIL, "com.miui.msa.global", "com.android.chrome", "com.miui.calculator"),
    )
    private val remover = FakeRemover(catalog)
    private val file by lazy { File(folder.root, "optimize.txt") }
    private val store by lazy { OptimizeStore(file) }
    private val queued = mutableListOf<Runnable>()
    private val optimizer by lazy {
        Optimizer(
            catalog, gw, AdblockDns(gw, FakeDnsNetwork(), internetBlocked = { false }), store,
            internetBlocked = { false }, clock = { now }, background = { queued += it },
            remover = remover, ticker = { nanos },
        )
    }

    private fun runQueued() {
        while (queued.isNotEmpty()) queued.removeAt(0).run()
    }

    @Test fun theListedAppsAreUninstalledAndThoseOfTheSystemHidden() {
        val report = optimizer.run()

        assertEquals(listOf(CANDY, TIKTOK, BOOKING), report.uninstalled)
        assertEquals(listOf("com.miui.msa.global", GMAIL, YOUTUBE), report.hidden)
        assertEquals(emptyList<String>(), report.failed)
        assertEquals(PhoneDnsStates.ON, report.dns)
        assertEquals(listOf(CANDY, TIKTOK, BOOKING), remover.calls.map { it.first })
        assertEquals(setOf("com.miui.msa.global", GMAIL, YOUTUBE), gw.hidden)
        assertTrue(queued.isEmpty())
        assertEquals(report, OptimizeStore(file).current.report)
    }

    @Test fun appsOffTheListAreNeverTouched() {
        optimizer.run()
        val touched = remover.calls.map { it.first } + gw.hiddenCalls.map { it.first }
        assertTrue(touched.none { it in setOf("com.android.chrome", "com.whatsapp", "com.miui.calculator") })
    }

    // The phone's own never-hidden apps (its camera, the home screen in use)
    // stay, even when listed.
    @Test fun theNeverHiddenAppsOfThePhoneStay() {
        catalog.neverHide = BloatList.NEVER_HIDE + YOUTUBE + BOOKING
        val report = optimizer.run()
        assertEquals(listOf(CANDY, TIKTOK), report.uninstalled)
        assertFalse(YOUTUBE in report.hidden)
        assertTrue(remover.calls.none { it.first == BOOKING })
    }

    @Test fun anAppAndroidWillNotUninstallIsHiddenInstead() {
        remover.refuse = mapOf(TIKTOK to "o Android não deixou desinstalar")
        val report = optimizer.run()
        assertEquals(listOf(CANDY, BOOKING), report.uninstalled)
        assertTrue(TIKTOK in report.hidden)
        assertTrue(TIKTOK in gw.hidden)
    }

    @Test fun anAppNeitherUninstalledNorHiddenIsFailedAndTheRestGoOn() {
        remover.throwing = setOf(CANDY)
        gw.refuseHiding = setOf(CANDY)
        val report = optimizer.run()
        assertEquals(listOf(TIKTOK, BOOKING), report.uninstalled)
        assertEquals(listOf(CANDY), report.failed)
    }

    @Test fun runningAgainKeepsCountingWhatWasUninstalled() {
        val first = optimizer.run()
        remover.calls.clear()
        val second = optimizer.run()
        assertEquals(first.uninstalled, second.uninstalled)
        assertEquals(first.hidden, second.hidden)
        assertTrue(remover.calls.isEmpty())
    }

    // An app put back on the phone is uninstalled again, and counted once.
    @Test fun anAppPutBackIsUninstalledAgain() {
        optimizer.run()
        catalog.present += CANDY
        val report = optimizer.run()
        assertEquals(setOf(CANDY, TIKTOK, BOOKING), report.uninstalled.toSet())
        assertEquals(1, report.uninstalled.count { it == CANDY })
        assertFalse(CANDY in catalog.present)
    }

    // The teacher waits at most until 60 s after asking: each uninstall
    // waits 15 s at most, and never past that.
    @Test fun theLastAppWaitsOnlyWhatIsLeftOfTheTeachersWait() {
        remover.before = { nanos += TimeUnit.SECONDS.toNanos(25) }
        val report = optimizer.run()

        assertEquals(listOf(CANDY, TIKTOK, BOOKING), remover.calls.map { it.first })
        // The third one waited only what was left of the 60 s.
        assertEquals(listOf(15_000L, 15_000L, 10_000L), remover.calls.map { it.second })
        assertEquals(listOf(CANDY, TIKTOK, BOOKING), report.uninstalled)
        assertTrue(queued.isEmpty())
    }

    // What is not started by then is uninstalled right after, in the
    // background, and kept for the next report.
    @Test fun whatIsLeftIsUninstalledInTheBackgroundAndKept() {
        catalog.present += setOf(WOW, NETFLIX)
        remover.before = { nanos += TimeUnit.SECONDS.toNanos(25) }
        val report = optimizer.run()
        assertEquals(listOf(CANDY, WOW, TIKTOK), report.uninstalled)
        assertTrue(NETFLIX in catalog.present && BOOKING in catalog.present)
        assertEquals(1, queued.size)

        runQueued()

        val kept = store.current.report
        assertEquals(listOf(CANDY, WOW, TIKTOK, NETFLIX, BOOKING), kept.uninstalled)
        assertEquals(15_000L, remover.calls.last().second)
        assertFalse(NETFLIX in catalog.present || BOOKING in catalog.present)
    }

    // No answer in time: left for the background, where it is tried again.
    @Test fun anUninstallWithoutAnswerIsTriedAgainInTheBackground() {
        remover.noAnswer = setOf(TIKTOK)
        val report = optimizer.run()
        assertFalse(TIKTOK in report.uninstalled || TIKTOK in report.hidden || TIKTOK in report.failed)
        assertEquals(1, queued.size)

        remover.noAnswer = emptySet()
        runQueued()
        assertTrue(TIKTOK in store.current.report.uninstalled)
    }

    // Still no answer in the background: hidden, so it is gone from the screen.
    @Test fun noAnswerInTheBackgroundEitherHidesIt() {
        remover.noAnswer = setOf(TIKTOK)
        optimizer.run()
        runQueued()
        assertTrue(TIKTOK in store.current.report.hidden)
        assertTrue(TIKTOK in gw.hidden)
    }

    @Test fun notTheSchoolsPhoneNothingIsUninstalled() {
        gw.owner = false
        val report = optimizer.run()
        assertTrue(remover.calls.isEmpty())
        assertEquals(emptyList<String>(), report.uninstalled)
        assertTrue(report.failed.containsAll(listOf(CANDY, TIKTOK, BOOKING, YOUTUBE, GMAIL)))
    }

    private companion object {
        const val CANDY = "com.king.candycrushsaga"
        const val WOW = "com.fugo.wow"
        const val TIKTOK = "com.zhiliaoapp.musically"
        const val NETFLIX = "com.netflix.mediaclient"
        const val BOOKING = "com.booking"
        const val YOUTUBE = "com.google.android.youtube"
        const val GMAIL = "com.google.android.gm"
    }
}
