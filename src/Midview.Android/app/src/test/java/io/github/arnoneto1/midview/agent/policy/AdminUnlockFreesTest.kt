package io.github.arnoneto1.midview.agent.policy

import io.github.arnoneto1.midview.agent.protocol.PhoneAdminPassword
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

// "Ajustes da escola" with the admin password: USB debugging and the
// device's accounts freed through the same DebuggingFree and AccountsFree as
// the teacher's commands, so they lock again by themselves after 10 minutes,
// as SchoolNetwork wires them.
class AdminUnlockFreesTest {
    @get:Rule val folder = TemporaryFolder()

    private var clock = 1_000_000L
    private val restrictions = (Lockdown.DEBUGGING_RESTRICTIONS + Lockdown.ACCOUNT_RESTRICTIONS).toMutableSet()
    private var adb = false
    private val minute = TimedFree.MINUTE_MILLIS

    private val debugging by lazy {
        DebuggingFree(
            File(folder.root, "debugging-free.txt"), Lockdown.DEBUGGING_RESTRICTIONS,
            isDeviceOwner = { true },
            addRestriction = { restrictions += it },
            clearRestriction = { restrictions -= it },
            usbDebugging = { adb = it },
            now = { clock },
        )
    }
    private val accounts by lazy {
        AccountsFree(
            File(folder.root, "accounts-free.txt"), Lockdown.ACCOUNT_RESTRICTIONS,
            isDeviceOwner = { true },
            addRestriction = { restrictions += it },
            clearRestriction = { restrictions -= it },
            now = { clock },
        )
    }
    private val admin by lazy {
        AdminUnlock(
            File(folder.root, "admin-password.bin"), File(folder.root, "admin-attempts.txt"),
            freeLocks = { what, minutes ->
                when (what) {
                    AdminUnlock.Freeable.DEBUGGING -> debugging.free(minutes)
                    AdminUnlock.Freeable.ACCOUNTS -> accounts.free(minutes)
                    AdminUnlock.Freeable.NETWORK -> error("not in this test")
                }
            },
            now = { clock },
        ).also { it.setPassword(PhoneAdminPassword.encode("abc123", ByteArray(16), 10_000)) }
    }

    private fun grant() = (admin.tryPassword("abc123") as AdminUnlock.Attempt.Unlocked).grant

    @Test fun debuggingIsFreedAndOnForTenMinutesThenLockedAndOff() {
        assertEquals(true to DebuggingFree.freedText(10), admin.free(grant(), AdminUnlock.Freeable.DEBUGGING))
        assertTrue(adb)
        assertTrue(Lockdown.DEBUGGING_RESTRICTIONS.none(restrictions::contains))
        assertEquals(clock + 10 * minute, debugging.freeUntil())

        clock += 10 * minute
        assertTrue(debugging.check())
        assertFalse(adb)
        assertTrue(restrictions.containsAll(Lockdown.DEBUGGING_RESTRICTIONS))
        assertNull(debugging.freeUntil())
    }

    @Test fun accountsAreFreedForTenMinutesThenLocked() {
        assertEquals(true to AccountsFree.freedText(10), admin.free(grant(), AdminUnlock.Freeable.ACCOUNTS))
        assertTrue(Lockdown.ACCOUNT_RESTRICTIONS.none(restrictions::contains))
        // The other locks stay.
        assertTrue(restrictions.containsAll(Lockdown.DEBUGGING_RESTRICTIONS))

        clock += 10 * minute
        assertTrue(accounts.check())
        assertTrue(restrictions.containsAll(Lockdown.ACCOUNT_RESTRICTIONS))
    }
}
