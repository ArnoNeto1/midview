package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.protocol.PhoneDnsStates
import io.github.arnoneto1.midview.agent.protocol.PhoneOptimizeReport
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

class OptimizeStoreTest {
    @get:Rule val folder = TemporaryFolder()

    private val file by lazy { File(folder.root, "optimize.txt") }
    private val at = Instant.parse("2026-11-12T13:00:00Z")
    private val state = OptimizeState(
        PhoneOptimizeReport(at, listOf("com.miui.msa.global", "com.xiaomi.glgm"), listOf("com.facebook.system"), PhoneDnsStates.ON, AdblockDns.HOST),
        lastDnsAttempt = at.plusSeconds(25),
    )

    @Test fun aPhoneNeverOptimizedHasNoReportYet() {
        val store = OptimizeStore(file)
        assertNull(store.current.report.optimizedAt)
        assertEquals(emptyList<String>(), store.current.report.hidden)
        assertEquals(emptyList<String>(), store.current.report.failed)
        assertEquals(AdblockDns.HOST, store.current.report.dnsHost)
        assertNull(store.current.lastDnsAttempt)
        assertFalse(file.exists())
    }

    @Test fun whatIsSavedIsThereAfterARestart() {
        assertTrue(OptimizeStore(file).save(state))
        assertEquals(state, OptimizeStore(file).current)
    }

    @Test fun aDnsNeverTriedIsSavedAsSuch() {
        val never = state.copy(lastDnsAttempt = null)
        OptimizeStore(file).save(never)
        assertEquals(never, OptimizeStore(file).current)
    }

    // The phone's own format, not the report's network form.
    @Test fun theSavedFileIsThePhonesOwnFormat() {
        OptimizeStore(file).save(state)
        assertEquals(
            "midview-optimize 2\nlastDnsAttempt=2026-11-12T13:00:25+00:00\noptimizedAt=2026-11-12T13:00:00+00:00\n" +
                "hidden=com.miui.msa.global\nhidden=com.xiaomi.glgm\nfailed=com.facebook.system\ndns=on\ndnsHost=${AdblockDns.HOST}\n",
            file.readText(Charsets.UTF_8),
        )
    }

    @Test fun theUninstalledAppsAreKept() {
        val withUninstalled = state.copy(report = state.report.copy(uninstalled = listOf("com.king.candycrushsaga", "com.booking")))
        OptimizeStore(file).save(withUninstalled)
        assertTrue(file.readText(Charsets.UTF_8).contains("failed=com.facebook.system\nuninstalled=com.king.candycrushsaga\nuninstalled=com.booking\n"))
        assertEquals(withUninstalled, OptimizeStore(file).current)
    }

    // What 0.0.70 and before wrote is read too.
    @Test fun theOldFormatIsReadToo() {
        file.writeBytes("lastDnsAttempt=2026-11-12T13:00:25+00:00\n".toByteArray() + state.report.encode())
        assertEquals(state, OptimizeStore(file).current)
    }

    @Test fun savingLeavesNoTemporaryFileBehind() {
        val store = OptimizeStore(file)
        store.save(state)
        store.save(state.copy(lastDnsAttempt = at.plusSeconds(60)))
        assertEquals(listOf("optimize.txt"), folder.root.list()!!.toList())
    }

    // Read again from scratch: the first optimization runs again, which
    // changes nothing already done.
    @Test fun aDamagedFileIsSetAsideAndTheStoreStartsOver() {
        file.writeText("lastDnsAttempt=\n{\"optimizedAt\":null}")
        val store = OptimizeStore(file)
        assertEquals(OptimizeStore.NEVER, store.current)
        assertTrue(File(file.path + ".corrupt").exists())
        assertFalse(file.exists())
    }

    @Test fun nonsenseIsNotAState() {
        assertNull(OptimizeStore.decode("nonsense".toByteArray()))
        assertNull(OptimizeStore.decode("lastDnsAttempt=yesterday\n{}".toByteArray()))
        assertNull(OptimizeStore.decode("when=\n${String(state.report.encode())}".toByteArray()))
        assertEquals(state, OptimizeStore.decode(OptimizeStore.encode(state)))
    }

    // What the phone did counts until the agent starts again.
    @Test fun aSaveTheDiskRefusesStillCountsNow() {
        val blocked = folder.newFolder("optimize-dir")
        File(blocked, "inside").writeText("x")
        val store = OptimizeStore(blocked)
        assertFalse(store.save(state))
        assertEquals(state, store.current)
    }

    @Test fun theDnsIsTriedAgainOnlyAfter30Minutes() {
        assertTrue(OptimizeStore.retryDue(at, lastDnsAttempt = null))
        assertFalse(OptimizeStore.retryDue(at, lastDnsAttempt = at))
        assertFalse(OptimizeStore.retryDue(at.plus(Duration.ofMinutes(30)).minusSeconds(1), at))
        assertTrue(OptimizeStore.retryDue(at.plus(Duration.ofMinutes(30)), at))
        assertTrue(OptimizeStore.retryDue(at.plus(Duration.ofHours(5)), at))
    }

    // A clock that was ahead and was set right must not hold the retry off
    // until it catches up.
    @Test fun aClockSetBackDoesNotHoldTheRetryOff() {
        assertTrue(OptimizeStore.retryDue(at.minus(Duration.ofDays(1)), at))
    }
}
