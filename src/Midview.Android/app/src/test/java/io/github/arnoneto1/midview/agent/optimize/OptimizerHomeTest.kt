package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.protocol.PhoneHomeStates
import io.github.arnoneto1.midview.agent.sites.FakeSiteGateway
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.time.Instant
import java.util.concurrent.TimeUnit

// The home screen as the last step of the teacher's "Otimizar" (HomeStep):
// in the answer when it is done in time, otherwise still being arranged and
// kept for the next answer; never at the agent's own first optimization.
class OptimizerHomeTest {
    @get:Rule val folder = TemporaryFolder()

    private val gw = FakeSiteGateway()
    private val catalog = FakeBloatCatalog(present = setOf("com.miui.msa.global"))
    private val file by lazy { File(folder.root, "optimize.txt") }
    private val store by lazy { OptimizeStore(file) }
    private var outcome: () -> String = { PhoneHomeStates.ARRANGED }
    private var arranged = 0
    private val arranger = HomeArranger {
        arranged++
        outcome()
    }

    // The home's thread: at once (inline), or queued to run later.
    private var inline = true
    private val homeQueue = mutableListOf<Runnable>()
    private val queued = mutableListOf<Runnable>()

    // Each reading of the clock after the first is past the answer's time.
    private var reads = 0
    private val ticker = { if (reads++ == 0) 0L else TimeUnit.SECONDS.toNanos(71) }

    private val optimizer by lazy {
        Optimizer(
            catalog, gw, AdblockDns(gw, FakeDnsNetwork(), internetBlocked = { false }), store,
            internetBlocked = { false }, clock = { Instant.parse("2026-11-19T13:00:00Z") }, background = { queued += it },
            ticker = ticker, homeArranger = arranger,
            homeBackground = { if (inline) it.run() else homeQueue += it },
        )
    }

    @Test fun theTeachersOptimizeArrangesTheHomeAfterTheApps() {
        val report = optimizer.run()

        assertEquals(PhoneHomeStates.ARRANGED, report.home)
        assertEquals(1, arranged)
        assertEquals(listOf("com.miui.msa.global"), report.hidden)
        assertEquals(report, OptimizeStore(file).current.report)
    }

    @Test fun whyItWasNotArrangedIsTheAnswer() {
        for (state in listOf(PhoneHomeStates.BY_HAND, PhoneHomeStates.SCREEN_OFF, PhoneHomeStates.NO_ACCESSIBILITY, PhoneHomeStates.OTHER_LAUNCHER)) {
            outcome = { state }
            assertEquals(state, optimizer.run().home)
        }
    }

    @Test fun anArrangementThatFailsIsLeftForTheTeachersHands() {
        outcome = { throw IllegalStateException("the service went away") }
        assertEquals(PhoneHomeStates.BY_HAND, optimizer.run().home)
    }

    @Test fun aSlowArrangementIsStillWorkingInTheAnswerAndKeptWhenItEnds() {
        inline = false
        val report = optimizer.run()

        assertEquals(PhoneHomeStates.WORKING, report.home)
        assertEquals(0, arranged)
        homeQueue.removeAt(0).run()
        assertEquals(1, arranged)
        assertEquals(PhoneHomeStates.ARRANGED, store.current.report.home)
    }

    @Test fun oneArrangementAtATime() {
        inline = false
        optimizer.run()
        // A second "Otimizar" while the first arranges: still working, none started.
        reads = 0
        assertEquals(PhoneHomeStates.WORKING, optimizer.run().home)
        assertEquals(1, homeQueue.size)
        homeQueue.removeAt(0).run()
        assertEquals(1, arranged)
        assertEquals(PhoneHomeStates.ARRANGED, store.current.report.home)
    }

    @Test fun theAgentsOwnFirstOptimizationLeavesTheHomeAlone() {
        optimizer.teacherSeen()
        while (queued.isNotEmpty()) queued.removeAt(0).run()

        assertTrue(store.current.report.optimizedAt != null)
        assertEquals(0, arranged)
        assertNull(store.current.report.home)
    }

    @Test fun notTheSchoolsDeviceArrangesNothing() {
        gw.owner = false
        assertNull(optimizer.run().home)
        assertEquals(0, arranged)
    }

    @Test fun theHomeStaysInTheReportWhenOnlyTheAppsAreSeenToAgain() {
        optimizer.run()
        outcome = { PhoneHomeStates.SCREEN_OFF }
        // Another "Otimizar" whose arrangement is left for later: the apps
        // seen to again keep the last outcome until this one is known.
        inline = false
        reads = 0
        val report = optimizer.run()
        assertEquals(PhoneHomeStates.WORKING, report.home)
        homeQueue.removeAt(0).run()
        assertEquals(PhoneHomeStates.SCREEN_OFF, store.current.report.home)
    }

    @Test fun aStillWorkingHomeIsNotKeptAcrossARestart() {
        inline = false
        optimizer.run()
        assertNull(OptimizeStore(file).current.report.home)

        homeQueue.removeAt(0).run()
        assertEquals(PhoneHomeStates.ARRANGED, OptimizeStore(file).current.report.home)
    }
}
