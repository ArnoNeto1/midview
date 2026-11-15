package io.github.arnoneto1.midview.agent.policy

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

// "Depuração USB": the lock lifted and USB debugging turned on for the
// minutes the teacher asked, and turned off and locked again by itself when
// they end, also after a restart (the same TimedFree as the Wi-Fi's and the
// accounts').
class DebuggingFreeTest {
    @get:Rule val folder = TemporaryFolder()

    private val restrictions = Lockdown.DEBUGGING_RESTRICTIONS.toMutableSet()

    // What happened, in order: "clear", "add" (the lock), "on", "off" (adb).
    private val steps = mutableListOf<String>()
    private var adb = false
    private var clock = 1_000_000L
    private var owner = true
    private var refuseAdb = false
    private val file by lazy { File(folder.root, "debugging-free.txt") }
    private val minute = TimedFree.MINUTE_MILLIS

    private fun debugging(debuggable: Boolean = false) = DebuggingFree(
        file, Lockdown.debuggingRestrictions(debuggable),
        isDeviceOwner = { owner },
        addRestriction = {
            steps += "add"
            restrictions += it
        },
        clearRestriction = {
            steps += "clear"
            restrictions -= it
        },
        usbDebugging = { on ->
            if (refuseAdb) throw SecurityException("refused adb $on")
            // Android refuses turning adb on under the lock.
            check(!on || restrictions.isEmpty()) { "adb on under the lock" }
            steps += if (on) "on" else "off"
            adb = on
        },
        keepsAdb = debuggable,
        now = { clock },
    )

    @Test fun freeingLiftsTheLockAndThenTurnsAdbOn() {
        val debugging = debugging()

        assertEquals(true to "depuração USB liberada e ligada por 10 minutos", debugging.free(10))

        assertTrue(restrictions.isEmpty())
        assertTrue(adb)
        assertEquals(listOf("clear", "on"), steps)
        assertEquals(clock + 10 * minute, debugging.freeUntil())
        assertEquals(true to "depuração USB liberada e ligada por 1 minuto", debugging.free(1))
    }

    @Test fun atTheEndAdbGoesOffThenTheLockComesBackEvenAfterARestart() {
        debugging().free(30)
        steps.clear()

        clock += 30 * minute - 1
        val again = debugging()
        assertFalse(again.check())
        assertTrue(adb)
        assertTrue(restrictions.isEmpty())

        clock += 1
        assertTrue(again.check())
        assertFalse(adb)
        assertEquals(listOf("off", "add"), steps)
        assertEquals(setOf("no_debugging_features"), restrictions)
        assertNull(again.freeUntil())
        assertFalse(file.exists())
    }

    @Test fun zeroTurnsAdbOffAndLocksNow() {
        val debugging = debugging()
        debugging.free(10)
        steps.clear()

        assertEquals(true to DebuggingFree.LOCKED, debugging.handle(byteArrayOf(0)))
        assertEquals(listOf("off", "add"), steps)
        assertFalse(adb)
        assertEquals(setOf("no_debugging_features"), restrictions)
        assertNull(debugging.freeUntil())
    }

    // Lockdown at a start (and so the watchdog's restart of the agent)
    // leaves the lock off while the time runs, and puts it back after.
    @Test fun aStartLocksOnlyWhenNotFree() {
        var locked = 0
        val debugging = debugging()
        debugging.free(10)
        debugging().unlessFree { locked++ }
        assertEquals(0, locked)
        assertTrue(adb)

        clock += 10 * minute
        debugging().unlessFree { locked++ }
        assertEquals(1, locked)
        assertFalse(adb)
    }

    // Never in effect before the device is owned: nothing lifted, adb untouched.
    @Test fun aDeviceNotPreparedOrAnInvalidTimeIsRefused() {
        assertEquals(false to TimedFree.INVALID_REQUEST, debugging().handle(byteArrayOf(61)))
        assertEquals(false to TimedFree.INVALID_REQUEST, debugging().handle(ByteArray(0)))
        owner = false
        assertEquals(false to TimedFree.NOT_PREPARED, debugging().free(10))
        assertEquals(false to TimedFree.NOT_PREPARED, debugging().lockNow())
        assertEquals(setOf("no_debugging_features"), restrictions)
        assertTrue(steps.isEmpty())
        assertFalse(file.exists())
    }

    // Android refusing to turn adb on: it goes off, the lock stays and nothing counts as free.
    @Test fun aRefusalLeavesItLocked() {
        val debugging = debugging()
        refuseAdb = true

        assertEquals(false to DebuggingFree.NOT_FREED, debugging.free(10))
        assertEquals(setOf("no_debugging_features"), restrictions)
        assertNull(debugging.freeUntil())
        assertFalse(file.exists())
    }

    // Turning adb off refused at the end: the lock still comes back (and
    // Android turns adb off with it).
    @Test fun aRefusedTurningOffStillLocks() {
        val debugging = debugging()
        debugging.free(10)
        refuseAdb = true

        assertEquals(true to DebuggingFree.LOCKED, debugging.lockNow())
        assertEquals(setOf("no_debugging_features"), restrictions)
    }

    // A debug build never has the lock: freeing turns adb on, and the end
    // leaves it as it is, so the test device stays reachable.
    @Test fun aDebugBuildKeepsAdbAtTheEnd() {
        restrictions.clear()
        val debugging = debugging(debuggable = true)

        assertTrue(debugging.free(10).first)
        assertTrue(adb)
        clock += 10 * minute
        assertTrue(debugging.check())

        assertTrue(adb)
        assertTrue(restrictions.isEmpty())
        assertEquals(listOf("on"), steps)
    }

    // "Tirar o Midview": the free time dropped and adb off, the lock not put back.
    @Test fun leavingTurnsOffWhatTheFreeTurnedOnWithoutTheLock() {
        val debugging = debugging()
        debugging.free(10)
        steps.clear()

        debugging.leave()

        assertFalse(adb)
        assertEquals(listOf("off"), steps)
        assertTrue(restrictions.isEmpty())
        assertNull(debugging.freeUntil())
        assertFalse(file.exists())

        // Never freed: nothing to turn off.
        steps.clear()
        debugging().leave()
        assertTrue(steps.isEmpty())
    }

    // Its own file, apart from the Wi-Fi's and the accounts'.
    @Test fun itsOwnFileAndKind() {
        debugging().free(10)
        val accounts = AccountsFree(
            file, Lockdown.ACCOUNT_RESTRICTIONS, isDeviceOwner = { true }, addRestriction = {}, clearRestriction = {}, now = { clock },
        )
        assertNull(accounts.freeUntil())
    }
}
