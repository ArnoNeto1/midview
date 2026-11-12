package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.protocol.PhoneDnsStates
import io.github.arnoneto1.midview.agent.protocol.PhoneOptimizeReport
import io.github.arnoneto1.midview.agent.sites.FakeSiteGateway
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.time.Duration
import java.time.Instant

class DnsWatcherTest {
    @get:Rule val folder = TemporaryFolder()

    @Test fun theStrictDnsUnvalidatedFor30SecondsFallsBack() {
        assertTrue(DnsWatcher.shouldFallBack(strictOnOurHost = true, unvalidatedFor = Duration.ofSeconds(30)))
        assertTrue(DnsWatcher.shouldFallBack(strictOnOurHost = true, unvalidatedFor = Duration.ofMinutes(10)))
    }

    @Test fun lessThan30SecondsWaits() {
        assertFalse(DnsWatcher.shouldFallBack(strictOnOurHost = true, unvalidatedFor = Duration.ofMillis(29_999)))
        assertFalse(DnsWatcher.shouldFallBack(strictOnOurHost = true, unvalidatedFor = Duration.ZERO))
    }

    @Test fun withTheDnsOffThereIsNothingToFallBackFrom() {
        assertFalse(DnsWatcher.shouldFallBack(strictOnOurHost = false, unvalidatedFor = Duration.ofHours(1)))
    }

    // How long the default network has gone unvalidated.

    // With the ad-free DNS off (the panel turned it off, or it fell back)
    // a network without internet is looked at less and less often.
    @Test fun withTheDnsOffTheLooksSpaceOut() {
        var wait = DnsWatcher.FALL_BACK_AFTER
        val waits = List(6) { DnsWatcher.nextWait(ourDnsOn = false, wait).also { wait = it } }
        assertEquals(
            listOf(60L, 120L, 240L, 480L, 600L, 600L).map { Duration.ofSeconds(it) },
            waits,
        )
        // Ours on again: back to every 30 s.
        assertEquals(DnsWatcher.FALL_BACK_AFTER, DnsWatcher.nextWait(ourDnsOn = true, Duration.ofMinutes(10)))
    }

    @Test fun anUnvalidatedNetworkCountsFromWhenItWasSeen() {
        val clock = ValidationClock()
        clock.changed("wifi", validated = false, now = 1_000)
        // Another change of the same network, still unvalidated.
        clock.changed("wifi", validated = false, now = 20_000)
        assertEquals(Duration.ofSeconds(30), clock.unvalidatedFor(31_000))
    }

    @Test fun aValidatedNetworkIsNeverAReason() {
        val clock = ValidationClock()
        clock.changed("wifi", validated = true, now = 0)
        assertNull(clock.unvalidatedFor(60_000))
        clock.changed("wifi", validated = false, now = 70_000)
        assertEquals(Duration.ofSeconds(5), clock.unvalidatedFor(75_000))
        clock.changed("wifi", validated = true, now = 80_000)
        assertNull(clock.unvalidatedFor(200_000))
    }

    @Test fun noNetworkAtAllNeverFallsBack() {
        val clock = ValidationClock()
        assertNull(clock.unvalidatedFor(60_000))
        clock.changed("wifi", validated = false, now = 0)
        clock.lost("wifi")
        assertNull(clock.unvalidatedFor(60_000))
    }

    @Test fun anotherNetworkIsCountedFromItsOwnStart() {
        val clock = ValidationClock()
        clock.changed("wifi", validated = false, now = 0)
        clock.changed("other wifi", validated = false, now = 20_000)
        assertEquals(Duration.ofSeconds(20), clock.unvalidatedFor(40_000))
    }

    // Android names the new default network before letting the old one go.
    @Test fun theOldNetworkLostLaterChangesNothing() {
        val clock = ValidationClock()
        clock.changed("wifi", validated = true, now = 0)
        clock.changed("other wifi", validated = false, now = 10_000)
        clock.lost("wifi")
        assertEquals(Duration.ofSeconds(30), clock.unvalidatedFor(40_000))
    }

    // What the watcher's check does (Optimizer.fallBack).

    private val gw = FakeSiteGateway()
    private var now = Instant.parse("2026-11-12T13:00:00Z")
    private val store by lazy { OptimizeStore(File(folder.root, "optimize.txt")) }
    private val optimizer by lazy {
        Optimizer(
            FakeBloatCatalog(emptySet()), gw, AdblockDns(gw, FakeDnsNetwork(), internetBlocked = { false }), store,
            internetBlocked = { false }, clock = { now }, background = { it.run() },
        )
    }
    private val ours = PrivateDns(PrivateDns.MODE_PROVIDER_HOSTNAME, AdblockDns.HOST)
    private val optimized = OptimizeState(
        PhoneOptimizeReport(Instant.parse("2026-11-12T12:00:00Z"), listOf("com.miui.msa.global"), emptyList(), PhoneDnsStates.ON, AdblockDns.HOST),
        lastDnsAttempt = Instant.parse("2026-11-12T12:00:00Z"),
    )

    @Test fun theDnsOnAndNoValidationFor30SecondsGoesBackToAutomatic() {
        gw.privateDns = ours
        store.save(optimized)

        assertTrue(optimizer.fallBack { Duration.ofSeconds(30) })

        assertEquals(listOf("automatic"), gw.dnsCalls)
        assertEquals(PrivateDns(PrivateDns.MODE_OPPORTUNISTIC, null), gw.privateDns)
        // Kept for the teacher and for the next try, 30 minutes from now.
        val saved = OptimizeStore(File(folder.root, "optimize.txt")).current
        assertEquals(PhoneDnsStates.OFF_NETWORK_BLOCKS, saved.report.dns)
        assertEquals(optimized.report.hidden, saved.report.hidden)
        assertEquals(optimized.report.optimizedAt, saved.report.optimizedAt)
        assertEquals(now, saved.lastDnsAttempt)
    }

    @Test fun lessThan30SecondsChangesNothing() {
        gw.privateDns = ours
        store.save(optimized)
        assertFalse(optimizer.fallBack { Duration.ofSeconds(29) })
        assertTrue(gw.dnsCalls.isEmpty())
        assertEquals(optimized, store.current)
    }

    @Test fun withoutANetworkNothingChanges() {
        gw.privateDns = ours
        store.save(optimized)
        assertFalse(optimizer.fallBack { null })
        assertTrue(gw.dnsCalls.isEmpty())
    }

    @Test fun withTheDnsAlreadyAutomaticNothingChanges() {
        store.save(optimized.copy(report = optimized.report.copy(dns = PhoneDnsStates.OFF_NO_INTERNET)))
        assertFalse(optimizer.fallBack { Duration.ofMinutes(5) })
        assertTrue(gw.dnsCalls.isEmpty())
        assertEquals(PhoneDnsStates.OFF_NO_INTERNET, store.current.report.dns)
    }

    @Test fun notTheDeviceOwnerNothingChanges() {
        gw.owner = false
        assertFalse(optimizer.fallBack { Duration.ofMinutes(5) })
    }
}
