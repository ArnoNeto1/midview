package io.github.arnoneto1.midview.agent.policy

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

class NetworkFreeTest {
    @get:Rule val folder = TemporaryFolder()

    private val locks = listOf("no_config_wifi", "no_airplane_mode", "no_change_wifi_state")
    private val restrictions = locks.toMutableSet()
    private var clock = 1_000_000L
    private var owner = true
    private var refuseClear = false
    private val file by lazy { File(folder.root, "network-free.txt") }

    private fun network() = NetworkFree(
        file, locks,
        isDeviceOwner = { owner },
        addRestriction = { restrictions += it },
        clearRestriction = {
            if (refuseClear) throw SecurityException("refused $it")
            restrictions -= it
        },
        now = { clock },
    )

    private val minute = NetworkFree.MINUTE_MILLIS

    @Test fun freeingLiftsEveryNetworkLockForTheTime() {
        val network = network()

        assertEquals(true to "Wi-Fi e modo avião liberados por 10 minutos", network.free(10))

        assertTrue(restrictions.isEmpty())
        assertEquals(clock + 10 * minute, network.freeUntil())
    }

    @Test fun oneMinuteIsSingular() {
        assertEquals(true to "Wi-Fi e modo avião liberados por 1 minuto", network().free(1))
    }

    @Test fun theLocksComeBackWhenTheTimeIsUp() {
        val network = network()
        network.free(10)

        clock += 10 * minute - 1
        assertFalse(network.check())
        assertTrue(restrictions.isEmpty())

        clock += 1
        assertTrue(network.check())
        assertEquals(locks.toSet(), restrictions)
        assertNull(network.freeUntil())
        assertFalse(file.exists())
    }

    // A restart of the agent or of the phone: the end is read back, and
    // the locks still come back on time.
    @Test fun theEndSurvivesARestart() {
        network().free(30)

        clock += 29 * minute
        val again = network()
        assertEquals(1_000_000L + 30 * minute, again.freeUntil())
        assertFalse(again.check())

        clock += minute
        assertTrue(again.check())
        assertEquals(locks.toSet(), restrictions)
    }

    // The agent starts after the time is up (the phone was off): Lockdown's
    // pass locks the network at once.
    @Test fun aStartAfterTheEndLocksAgain() {
        network().free(10)
        clock += 2 * 60 * minute

        var locked = 0
        network().unlessFree { locked++ }

        assertEquals(1, locked)
        assertEquals(locks.toSet(), restrictions)
    }

    @Test fun whileFreeTheStartLeavesTheNetworkAlone() {
        val network = network()
        network.free(10)

        var locked = 0
        network.unlessFree { locked++ }

        assertEquals(0, locked)
        assertTrue(restrictions.isEmpty())
    }

    @Test fun lockedTheStartLocksTheNetwork() {
        var locked = 0
        network().unlessFree { locked++ }
        assertEquals(1, locked)
    }

    // A clock set back can never stretch the free time beyond an hour.
    @Test fun aClockSetBackLocksAgain() {
        val network = network()
        network.free(10)

        clock -= 2 * 60 * minute
        assertNull(network.freeUntil())
        assertTrue(network.check())
        assertEquals(locks.toSet(), restrictions)
    }

    @Test fun lockNowLocksAndForgetsTheEnd() {
        val network = network()
        network.free(60)

        assertEquals(true to NetworkFree.LOCKED, network.lockNow())

        assertEquals(locks.toSet(), restrictions)
        assertNull(network.freeUntil())
        assertNull(network().freeUntil())
    }

    @Test fun zeroMinutesLocksNow() {
        val network = network()
        network.free(10)
        assertEquals(true to NetworkFree.LOCKED, network.free(0))
        assertEquals(locks.toSet(), restrictions)
    }

    @Test fun freeingAgainCountsFromNow() {
        val network = network()
        network.free(10)
        clock += 5 * minute
        network.free(10)
        assertEquals(clock + 10 * minute, network.freeUntil())
    }

    @Test fun anythingOutOfRangeIsRefused() {
        val network = network()
        for (minutes in listOf(-1, 61, 255)) {
            assertEquals(false to NetworkFree.INVALID_REQUEST, network.free(minutes))
        }
        assertEquals(false to NetworkFree.INVALID_REQUEST, network.handle(byteArrayOf(61)))
        assertEquals(false to NetworkFree.INVALID_REQUEST, network.handle(ByteArray(0)))
        assertEquals(locks.toSet(), restrictions)
    }

    @Test fun thePayloadIsTheMinutes() {
        assertEquals(true to "Wi-Fi e modo avião liberados por 60 minutos", network().handle(byteArrayOf(60)))
    }

    @Test fun aPhoneThatIsNotTheSchoolsFreesNothing() {
        owner = false
        val network = network()

        assertEquals(false to NetworkFree.NOT_PREPARED, network.free(10))
        assertEquals(false to NetworkFree.NOT_PREPARED, network.lockNow())
        assertNull(network.freeUntil())
    }

    // Android refusing to lift a lock leaves the phone locked.
    @Test fun aRefusalLeavesItLocked() {
        refuseClear = true
        val network = network()

        assertEquals(false to NetworkFree.NOT_FREED, network.free(10))

        assertEquals(locks.toSet(), restrictions)
        assertNull(network.freeUntil())
        assertFalse(file.exists())
    }

    // A file nobody can read counts as locked.
    @Test fun anUnreadableFileIsLocked() {
        file.writeText("garbage")
        assertNull(network().freeUntil())
        var locked = 0
        network().unlessFree { locked++ }
        assertEquals(1, locked)
    }
}
