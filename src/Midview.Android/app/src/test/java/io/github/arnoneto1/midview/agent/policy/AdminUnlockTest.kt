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
    private val freed = mutableListOf<Pair<AdminUnlock.Freeable, Int>>()
    private var freeAnswer = true to "Wi-Fi e modo avião liberados por 10 minutos"
    private val passwordFile by lazy { File(folder.root, "admin-password.bin") }
    private val attemptsFile by lazy { File(folder.root, "admin-attempts.txt") }
    private val payload = PhoneAdminPassword.encode("abc123", ByteArray(16) { it.toByte() }, 10_000)

    private fun admin() = AdminUnlock(
        passwordFile, attemptsFile,
        freeLocks = { what, minutes ->
            freed += what to minutes
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

    private fun unlock(admin: AdminUnlock, typed: String = "abc123"): AdminUnlock.Grant =
        (admin.tryPassword(typed) as AdminUnlock.Attempt.Unlocked).grant

    // The right password frees nothing by itself: it opens the choices.
    @Test fun theRightPasswordUnlocksWithoutFreeing() {
        val admin = admin()
        assertEquals(true to AdminUnlock.SET, admin.setPassword(payload))

        assertEquals(AdminUnlock.State.Ready, admin.state())
        val grant = unlock(admin)
        assertTrue(admin.isValid(grant))
        assertEquals(clock + AdminUnlock.UNLOCKED_MILLIS, grant.untilMillis)
        assertTrue(freed.isEmpty())
    }

    @Test fun eachChoiceFreesItsLockForTenMinutes() {
        val admin = admin()
        admin.setPassword(payload)
        val grant = unlock(admin)

        assertEquals(freeAnswer, admin.free(grant, AdminUnlock.Freeable.NETWORK))
        admin.free(grant, AdminUnlock.Freeable.DEBUGGING)
        admin.free(grant, AdminUnlock.Freeable.ACCOUNTS)

        assertEquals(
            listOf(
                AdminUnlock.Freeable.NETWORK to 10,
                AdminUnlock.Freeable.DEBUGGING to 10,
                AdminUnlock.Freeable.ACCOUNTS to 10,
            ),
            freed,
        )
    }

    @Test fun withoutAnUnlockNothingIsFreed() {
        val admin = admin()
        admin.setPassword(payload)

        assertEquals(false to AdminUnlock.UNLOCK_ENDED, admin.free(null, AdminUnlock.Freeable.DEBUGGING))
        assertTrue(freed.isEmpty())
    }

    @Test fun anUnlockEndsAfterTwoMinutes() {
        val admin = admin()
        admin.setPassword(payload)
        val grant = unlock(admin)

        clock += AdminUnlock.UNLOCKED_MILLIS
        assertFalse(admin.isValid(grant))
        assertEquals(false to AdminUnlock.UNLOCK_ENDED, admin.free(grant, AdminUnlock.Freeable.ACCOUNTS))
        assertTrue(freed.isEmpty())
    }

    // The clock set back does not stretch an unlock.
    @Test fun aClockSetBackEndsAnUnlock() {
        val admin = admin()
        admin.setPassword(payload)
        val grant = unlock(admin)

        clock -= 60 * 60_000L
        assertFalse(admin.isValid(grant))
    }

    // The teacher sending another password (or taking it off) ends an
    // unlock of the old one.
    @Test fun aNewPasswordEndsAnUnlock() {
        val admin = admin()
        admin.setPassword(payload)
        val grant = unlock(admin)

        admin.setPassword(PhoneAdminPassword.encode("novaSenha9", ByteArray(16), 10_000))
        assertFalse(admin.isValid(grant))

        val again = unlock(admin, "novaSenha9")
        admin.setPassword(ByteArray(0))
        assertFalse(admin.isValid(again))
        assertTrue(freed.isEmpty())
    }

    // Only the AdminUnlock that gave it takes a grant.
    @Test fun anotherUnlocksGrantDoesNotFree() {
        val first = admin()
        first.setPassword(payload)
        val grant = unlock(first)

        val other = AdminUnlock(
            File(folder.newFolder(), "p.bin"), File(folder.newFolder(), "a.txt"),
            freeLocks = { what, minutes ->
                freed += what to minutes
                freeAnswer
            },
            now = { clock },
        )
        other.setPassword(payload)
        assertEquals(false to AdminUnlock.UNLOCK_ENDED, other.free(grant, AdminUnlock.Freeable.NETWORK))
        assertTrue(freed.isEmpty())
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
        assertTrue(admin.tryPassword("abc123") is AdminUnlock.Attempt.Unlocked)
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
        assertTrue(admin.tryPassword("novaSenha9") is AdminUnlock.Attempt.Unlocked)
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
        assertTrue(admin.tryPassword("abc123") is AdminUnlock.Attempt.Unlocked)
    }

    // A choice on a phone that cannot free it: the reason.
    @Test fun aRefusedFreeingSaysWhy() {
        freeAnswer = false to NetworkFree.NOT_PREPARED
        val admin = admin()
        admin.setPassword(payload)
        assertEquals(false to NetworkFree.NOT_PREPARED, admin.free(unlock(admin), AdminUnlock.Freeable.NETWORK))
    }

    // The fifth wrong try was counted, then the phone restarted before its
    // check ended: the wait starts at the next start, it is not forgotten.
    @Test fun aRestartDuringTheFifthTryStillLocksOut() {
        admin().setPassword(payload)
        attemptsFile.writeText("midview-admin-attempts 2\nfailures=5\nlockedUntil=\n")

        val end = clock + AdminUnlock.LOCKOUT_MILLIS
        assertEquals(AdminUnlock.State.LockedOut(end), admin().state())
        assertEquals(AdminUnlock.Attempt.LockedOut(end), admin().tryPassword("abc123"))
        assertTrue(freed.isEmpty())
    }

    // "Tirar o Midview deste celular": the password checked alone frees
    // nothing, and counts its wrong tries with the Wi-Fi's.
    @Test fun checkingAloneFreesNothing() {
        val admin = admin()
        admin.setPassword(payload)

        assertEquals(AdminUnlock.Attempt.Verified, admin.checkOnly("abc123"))
        assertTrue(freed.isEmpty())
    }

    @Test fun checkingAloneSharesTheWrongTries() {
        val admin = admin()
        admin.setPassword(payload)
        repeat(4) { admin.tryPassword("errada") }

        val locked = admin.checkOnly("errada")

        assertTrue(locked is AdminUnlock.Attempt.LockedOut)
        assertTrue(admin.checkOnly("abc123") is AdminUnlock.Attempt.LockedOut)
    }

    @Test fun withoutAPasswordCheckingSaysSo() =
        assertEquals(AdminUnlock.Attempt.NoPassword, admin().checkOnly("abc123"))
}
