package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.protocol.PhoneDnsStates
import io.github.arnoneto1.midview.agent.sites.FakeSiteGateway
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.time.Duration
import java.time.Instant
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

class OptimizerTest {
    @get:Rule val folder = TemporaryFolder()

    private val ours = PrivateDns(PrivateDns.MODE_PROVIDER_HOSTNAME, AdblockDns.HOST)
    private val gw = FakeSiteGateway()
    private val network = FakeDnsNetwork()
    private var blocked = false
    private var now = Instant.parse("2026-11-12T13:00:00Z")
    private val catalog = FakeBloatCatalog(
        present = setOf("com.miui.msa.global", "com.xiaomi.glgm", "com.facebook.system", "com.whatsapp", "com.android.chrome"),
    )
    private val file by lazy { File(folder.root, "optimize.txt") }
    private val store by lazy { OptimizeStore(file) }
    private val queued = mutableListOf<Runnable>()
    private val optimizer by lazy {
        Optimizer(
            catalog, gw, AdblockDns(gw, network, internetBlocked = { blocked }), store,
            internetBlocked = { blocked }, clock = { now }, background = { queued += it },
        )
    }

    private fun runQueued() {
        while (queued.isNotEmpty()) queued.removeAt(0).run()
    }

    @Test fun optimizeHidesTheListedAppsThePhoneHasAndTurnsTheDnsOn() {
        val report = optimizer.run()

        assertEquals(now, report.optimizedAt)
        assertEquals(listOf("com.miui.msa.global", "com.xiaomi.glgm", "com.facebook.system"), report.hidden)
        assertEquals(emptyList<String>(), report.failed)
        assertEquals(PhoneDnsStates.ON, report.dns)
        assertEquals(AdblockDns.HOST, report.dnsHost)
        assertEquals(setOf("com.miui.msa.global", "com.xiaomi.glgm", "com.facebook.system"), gw.hidden)
        assertEquals(ours, gw.privateDns)
    }

    @Test fun theReportIsKeptForTheNextStart() {
        val report = optimizer.run()
        val saved = OptimizeStore(file).current
        assertEquals(report, saved.report)
        assertEquals(now, saved.lastDnsAttempt)
    }

    @Test fun whatAndroidRefusesIsFailed() {
        gw.refuseHiding = setOf("com.xiaomi.glgm")
        val report = optimizer.run()
        assertEquals(listOf("com.miui.msa.global", "com.facebook.system"), report.hidden)
        assertEquals(listOf("com.xiaomi.glgm"), report.failed)
    }

    // Android answers false for an app already hidden: it is hidden, not
    // refused, and is not asked again.
    @Test fun anAppAlreadyHiddenIsHiddenNotFailed() {
        gw.hidden += "com.xiaomi.glgm"
        gw.refuseHiding = setOf("com.xiaomi.glgm")
        val report = optimizer.run()
        assertEquals(listOf("com.miui.msa.global", "com.xiaomi.glgm", "com.facebook.system"), report.hidden)
        assertEquals(emptyList<String>(), report.failed)
        assertTrue(gw.hiddenCalls.none { it.first == "com.xiaomi.glgm" })
    }

    @Test fun neverHiddenAppsAreLeftAlone() {
        catalog.neverHide = BloatList.NEVER_HIDE + "com.xiaomi.glgm"
        val report = optimizer.run()
        assertEquals(listOf("com.miui.msa.global", "com.facebook.system"), report.hidden)
        assertTrue(gw.hiddenCalls.none { it.first == "com.xiaomi.glgm" || it.first == "com.android.chrome" })
    }

    @Test fun runningAgainChangesNothing() {
        val first = optimizer.run()
        gw.hiddenCalls.clear()
        gw.dnsCalls.clear()
        now = now.plusSeconds(600)

        val second = optimizer.run()

        assertEquals(first.copy(optimizedAt = now), second)
        assertTrue(gw.hiddenCalls.isEmpty())
        assertTrue(gw.dnsCalls.isEmpty())
    }

    @Test fun aRefusedAppIsTriedAgainNextTime() {
        gw.refuseHiding = setOf("com.xiaomi.glgm")
        optimizer.run()
        gw.refuseHiding = emptySet()
        val report = optimizer.run()
        assertEquals(listOf("com.miui.msa.global", "com.xiaomi.glgm", "com.facebook.system"), report.hidden)
        assertEquals(emptyList<String>(), report.failed)
    }

    @Test fun withTheInternetBlockedTheAppsAreHiddenAndTheDnsWaits() {
        blocked = true
        val report = optimizer.run()
        assertEquals(3, report.hidden.size)
        assertEquals(PhoneDnsStates.OFF_INTERNET_BLOCKED, report.dns)
        assertTrue(gw.dnsCalls.isEmpty())
    }

    @Test fun notTheSchoolsPhoneHidesNothing() {
        gw.owner = false
        val report = optimizer.run()
        assertEquals(now, report.optimizedAt)
        assertEquals(emptyList<String>(), report.hidden)
        assertEquals(listOf("com.miui.msa.global", "com.xiaomi.glgm", "com.facebook.system"), report.failed)
        assertEquals(PhoneDnsStates.OFF_ERROR, report.dns)
        assertTrue(gw.hiddenCalls.isEmpty())
        assertTrue(network.calls.isEmpty())
    }

    // The first command of a trusted teacher.

    @Test fun theFirstTrustedCommandOptimizesInTheBackground() {
        optimizer.teacherSeen()
        assertNull(store.current.report.optimizedAt)
        assertEquals(1, queued.size)

        runQueued()

        assertEquals(now, store.current.report.optimizedAt)
        assertEquals(3, gw.hidden.size)
        assertEquals(PhoneDnsStates.ON, store.current.report.dns)
    }

    @Test fun theFirstOptimizationStartsOnce() {
        optimizer.teacherSeen()
        optimizer.teacherSeen()
        assertEquals(1, queued.size)
        runQueued()
        optimizer.teacherSeen()
        assertTrue(queued.isEmpty())
    }

    @Test fun onceOptimizedACommandStartsNothing() {
        optimizer.run()
        optimizer.teacherSeen()
        assertTrue(queued.isEmpty())
    }

    // The DNS tried again.

    @Test fun aDnsLeftOffForNoInternetIsTriedAgainAfter30Minutes() {
        network.internet = false
        optimizer.run()
        assertEquals(PhoneDnsStates.OFF_NO_INTERNET, store.current.report.dns)
        network.internet = true
        gw.hiddenCalls.clear()

        now = now.plus(Duration.ofMinutes(29))
        optimizer.teacherSeen()
        assertTrue(queued.isEmpty())

        now = now.plus(Duration.ofMinutes(1))
        optimizer.teacherSeen()
        runQueued()

        assertEquals(PhoneDnsStates.ON, store.current.report.dns)
        assertEquals(now, store.current.lastDnsAttempt)
        // Only the DNS: the apps and the time of the optimization stay.
        assertTrue(gw.hiddenCalls.isEmpty())
        assertEquals(now.minus(Duration.ofMinutes(30)), store.current.report.optimizedAt)
    }

    @Test fun aDnsTheNetworkBlockedIsTriedAgainAfter30Minutes() {
        gw.setHostResult = PrivateDns.SET_ERROR_HOST_NOT_SERVING
        optimizer.run()
        assertEquals(PhoneDnsStates.OFF_NETWORK_BLOCKS, store.current.report.dns)

        now = now.plus(Duration.ofMinutes(30))
        optimizer.teacherSeen()
        runQueued()
        assertEquals(listOf("host ${AdblockDns.HOST}", "host ${AdblockDns.HOST}"), gw.dnsCalls)
        assertEquals(now, store.current.lastDnsAttempt)
    }

    @Test fun aDnsOnOrInErrorIsNotTriedAgainByItself() {
        optimizer.run()
        now = now.plus(Duration.ofHours(2))
        optimizer.teacherSeen()
        assertTrue(queued.isEmpty())

        gw.privateDns = PrivateDns(PrivateDns.MODE_OPPORTUNISTIC, null)
        gw.refuseDns = true
        optimizer.run()
        assertEquals(PhoneDnsStates.OFF_ERROR, store.current.report.dns)
        now = now.plus(Duration.ofHours(2))
        optimizer.teacherSeen()
        assertTrue(queued.isEmpty())
    }

    @Test fun freeingTheInternetTriesTheDnsLeftOffForIt() {
        blocked = true
        optimizer.run()
        assertEquals(PhoneDnsStates.OFF_INTERNET_BLOCKED, store.current.report.dns)

        blocked = false
        optimizer.internetFreed()
        runQueued()

        assertEquals(PhoneDnsStates.ON, store.current.report.dns)
        assertEquals(ours, gw.privateDns)
    }

    @Test fun freeingTheInternetWithTheDnsOnChangesNothing() {
        optimizer.run()
        gw.dnsCalls.clear()
        optimizer.internetFreed()
        assertTrue(queued.isEmpty())
    }

    @Test fun whileTheInternetIsBlockedACommandDoesNotTryTheDns() {
        blocked = true
        optimizer.run()
        now = now.plus(Duration.ofHours(1))
        optimizer.teacherSeen()
        assertTrue(queued.isEmpty())
    }

    // The agent ended before it could try (the internet freed meanwhile).
    @Test fun aCommandAfterTheInternetWasFreedTriesTheDnsLeftOff() {
        blocked = true
        optimizer.run()
        blocked = false
        optimizer.teacherSeen()
        runQueued()
        assertEquals(PhoneDnsStates.ON, store.current.report.dns)
    }

    @Test fun aRetryRunningIsNotStartedTwice() {
        network.internet = false
        optimizer.run()
        now = now.plus(Duration.ofMinutes(31))
        optimizer.teacherSeen()
        optimizer.teacherSeen()
        assertEquals(1, queued.size)
    }

    // "Otimizar" while the first optimization still runs (in the
    // background) waits for it and answers its report, without a second run.
    @Test fun optimizeWhileOneRunsAnswersThatOnesReport() {
        val inside = CountDownLatch(1)
        val release = CountDownLatch(1)
        val slow = object : DnsNetwork {
            var checks = 0
            override fun internetOk(): Boolean {
                checks++
                inside.countDown()
                release.await(5, TimeUnit.SECONDS)
                return true
            }
            override fun hostServes(host: String) = true
            override fun awaitPrivateDns(host: String, timeoutMillis: Long) = true
        }
        val optimizer = Optimizer(
            catalog, gw, AdblockDns(gw, slow, internetBlocked = { false }), store,
            internetBlocked = { false }, clock = { now }, background = { Thread(it).start() },
        )

        optimizer.teacherSeen()
        assertTrue(inside.await(5, TimeUnit.SECONDS))
        var answer: Any? = null
        val asking = Thread { answer = optimizer.run() }.apply { start() }
        // Waiting for the run in the background to end.
        val until = System.nanoTime() + TimeUnit.SECONDS.toNanos(5)
        while (asking.state != Thread.State.BLOCKED && System.nanoTime() < until) Thread.sleep(5)
        release.countDown()
        asking.join(5_000)

        assertSame(store.current.report, answer)
        assertEquals(1, slow.checks)
    }
}
