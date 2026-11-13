package io.github.arnoneto1.midview.agent.policy

import io.github.arnoneto1.midview.agent.protocol.PhoneAdminPassword
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

class AdminUnlockTest {
    @get:Rule val folder = TemporaryFolder()

    private var clock = 5_000_000L
    private val freed = mutableListOf<Int>()
    private var freeAnswer = true to "Wi-Fi e modo avião liberados por 10 minutos"
    private val passwordFile by lazy { File(folder.root, "admin-password.bin") }
    private val attemptsFile by lazy { File(folder.root, "admin-attempts.txt") }
    private val payload = PhoneAdminPassword.encode("abc123", ByteArray(16) { it.toByte() }, 10_000)

    private fun admin() = AdminUnlock(
        passwordFile, attemptsFile,
        free = {
            freed += it
            freeAnswer
        },
        now = { clock },
    )

    @Test fun withoutAPasswordNothingIsChecked() {
        val admin = admin()
        assertEquals(AdminUnlock.State.NoPassword, admin.state())
        assertEquals(AdminUnlock.Attempt.NoPassword, admin.tryPassword("abc123"))
        assertTrue(freed.isEmpty())
    }

    @Test fun theRightPasswordFreesTheNetworkForTenMinutes() {
        val admin = admin()
        assertEquals(true to AdminUnlock.SET, admin.setPassword(payload))

        assertEquals(AdminUnlock.State.Ready, admin.state())
        assertEquals(AdminUnlock.Attempt.Freed, admin.tryPassword("abc123"))
        assertEquals(listOf(10), freed)
    }

    // Only the hash is kept: the payload as the teacher sent it.
    @Test fun thePhoneKeepsTheHashNotThePassword() {
        admin().setPassword(payload)
        assertArrayEquals(payload, passwordFile.readBytes())
        assertFalse(String(passwordFile.readBytes(), Charsets.ISO_8859_1).contains("abc123"))
    }

    @Test fun aWrongPasswordSaysHowManyTriesAreLeft() {
        val admin = admin()
        admin.setPassword(payload)

        assertEquals(AdminUnlock.Attempt.Wrong(4), admin.tryPassword("abc124"))
        assertEquals(AdminUnlock.Attempt.Wrong(3), admin.tryPassword("ABC123"))
        assertEquals(AdminUnlock.Attempt.Wrong(2), admin.tryPassword("x"))
        assertTrue(freed.isEmpty())
    }

    @Test fun fiveWrongOnesLockTheScreenOutForFiveMinutes() {
        val admin = admin()
        admin.setPassword(payload)
        repeat(4) { admin.tryPassword("wrong1") }

        val end = clock + AdminUnlock.LOCKOUT_MILLIS
        assertEquals(AdminUnlock.Attempt.LockedOut(end), admin.tryPassword("wrong1"))
        assertEquals(AdminUnlock.State.LockedOut(end), admin.state())
        // Not even the right one while it lasts.
        assertEquals(AdminUnlock.Attempt.LockedOut(end), admin.tryPassword("abc123"))
        assertTrue(freed.isEmpty())

        clock = end
        assertEquals(AdminUnlock.State.Ready, admin.state())
        assertEquals(AdminUnlock.Attempt.Freed, admin.tryPassword("abc123"))
    }

    // Closing the screen or killing the app does not start the count over.
    @Test fun theCountSurvivesARestart() {
        admin().setPassword(payload)
        repeat(3) { admin().tryPassword("wrong1") }
        assertEquals(AdminUnlock.Attempt.Wrong(1), admin().tryPassword("wrong1"))
        val end = clock + AdminUnlock.LOCKOUT_MILLIS
        assertEquals(AdminUnlock.Attempt.LockedOut(end), admin().tryPassword("wrong1"))
        assertEquals(AdminUnlock.State.LockedOut(end), admin().state())
    }

    @Test fun theRightPasswordStartsTheCountOver() {
        val admin = admin()
        admin.setPassword(payload)
        repeat(4) { admin.tryPassword("wrong1") }
        admin.tryPassword("abc123")
        assertEquals(AdminUnlock.Attempt.Wrong(4), admin.tryPassword("wrong1"))
    }

    @Test fun aNewPasswordStartsTheCountOverAndEndsALockout() {
        val admin = admin()
        admin.setPassword(payload)
        repeat(5) { admin.tryPassword("wrong1") }

        admin.setPassword(PhoneAdminPassword.encode("novaSenha9", ByteArray(16), 10_000))

        assertEquals(AdminUnlock.State.Ready, admin.state())
        assertEquals(AdminUnlock.Attempt.Wrong(4), admin.tryPassword("abc123"))
        assertEquals(AdminUnlock.Attempt.Freed, admin.tryPassword("novaSenha9"))
    }

    // A clock set back never makes the wait longer than one lockout.
    @Test fun aClockSetBackWaitsOneLockoutAtMost() {
        val admin = admin()
        admin.setPassword(payload)
        repeat(5) { admin.tryPassword("wrong1") }

        clock -= 60 * 60_000L
        assertEquals(AdminUnlock.State.LockedOut(clock + AdminUnlock.LOCKOUT_MILLIS), admin.state())
    }

    // Spoiling the count's file does not make guessing easier.
    @Test fun anUnreadableCountIsALockout() {
        admin().setPassword(payload)
        attemptsFile.writeText("garbage")
        assertEquals(AdminUnlock.State.LockedOut(clock + AdminUnlock.LOCKOUT_MILLIS), admin().state())
    }

    @Test fun anEmptyPayloadRemovesThePassword() {
        val admin = admin()
        admin.setPassword(payload)
        assertEquals(true to AdminUnlock.REMOVED, admin.setPassword(ByteArray(0)))
        assertEquals(AdminUnlock.State.NoPassword, admin.state())
        assertFalse(passwordFile.exists())
    }

    @Test fun anInvalidPayloadIsRefusedAndKeepsTheOldPassword() {
        val admin = admin()
        admin.setPassword(payload)
        assertEquals(false to PhoneAdminPassword.INVALID, admin.setPassword(byteArrayOf(1, 2, 3)))
        assertEquals(AdminUnlock.Attempt.Freed, admin.tryPassword("abc123"))
    }

    // The right password on a phone that cannot free the network: the reason.
    @Test fun aRefusedFreeingSaysWhy() {
        freeAnswer = false to NetworkFree.NOT_PREPARED
        val admin = admin()
        admin.setPassword(payload)
        assertEquals(AdminUnlock.Attempt.Refused(NetworkFree.NOT_PREPARED), admin.tryPassword("abc123"))
    }
}
