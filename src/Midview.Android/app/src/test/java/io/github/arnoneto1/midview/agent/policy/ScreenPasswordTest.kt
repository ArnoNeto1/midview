package io.github.arnoneto1.midview.agent.policy

import io.github.arnoneto1.midview.agent.protocol.PhoneScreenPassword
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

class ScreenPasswordTest {
    @get:Rule val folder = TemporaryFolder()

    // Android's lock screen as the agent sees it: the lock (null: none), the
    // reset token (active at once without a lock, else after an unlock; a
    // waiting one forgotten at a restart) and the PIN rule.
    private class FakeAndroid : ScreenLockGateway {
        var owner = true
        var lock: String? = null
        var token: ByteArray? = null
        var tokenActive = false
        var tokenForgotten = false
        var boot = 1
        var refuseToken = false
        var refuseReset = false
        var refuseRule = false
        var pinRule: Boolean? = null
        val resets = mutableListOf<String>()
        var tokensGiven = 0
        var duringReset: () -> Unit = {}

        override fun isDeviceOwner() = owner
        override fun isDeviceSecure() = lock != null
        override fun isResetTokenActive() = token != null && tokenActive
        override fun setResetToken(token: ByteArray): Boolean {
            if (refuseToken) return false
            tokensGiven++
            this.token = token.copyOf()
            tokenActive = lock == null
            tokenForgotten = false
            return true
        }
        override fun resetPassword(pin: String, token: ByteArray): Boolean {
            if (refuseReset || !tokenActive || !token.contentEquals(this.token)) return false
            // Android refuses no lock while its PIN rule is on.
            if (pin.isEmpty() && pinRule == true) return false
            lock = pin.ifEmpty { null }
            resets += pin
            duringReset()
            return true
        }
        override fun requirePin(required: Boolean) {
            if (refuseRule) throw SecurityException("no limit-password")
            pinRule = required
        }
        override fun bootCount() = boot

        // The student (or anyone) unlocks the phone with its lock.
        fun unlock() {
            if (token != null && !tokenForgotten) tokenActive = true
        }

        fun restart() {
            boot++
            if (!tokenActive) tokenForgotten = true
        }
    }

    private val android = FakeAndroid()
    private var now = 0L
    private val logged = mutableListOf<String>()
    private val file by lazy { File(folder.root, "screen-password.txt") }
    private var made = 0

    private fun keeper(store: ScreenPasswordStore = ScreenPasswordStore(file)) =
        ScreenPassword(android, store, newToken = { ByteArray(32) { (it + made).toByte() }.also { made++ } }, clock = { now }) { t, _ -> logged += t }

    private fun pin(text: String) = PhoneScreenPassword.encode(text)

    @Test fun aPhoneWithNoLockGetsAnActiveTokenAtTheStart() {
        val keeper = keeper()
        assertEquals(ScreenPasswordAction.SET_TOKEN, keeper.check(starting = true))
        assertEquals(ResetToken.ACTIVE, keeper.tokenState())
        val kept = ScreenPasswordStore(file).current
        assertEquals(64, kept.token!!.length)
        assertEquals(1, kept.tokenBoot)
        assertNull(kept.teacherPin)
        assertTrue(logged.contains("Lock screen reset token: ativa."))
        // The next checks change nothing.
        assertEquals(ScreenPasswordAction.NOTHING, keeper.check())
        assertEquals(1, android.tokensGiven)
        assertEquals(emptyList<String>(), android.resets)
    }

    @Test fun theTeachersPinIsSetAndKept() {
        val keeper = keeper()
        keeper.check(starting = true)
        assertEquals(true to ScreenPassword.PIN_SET, keeper.setTeacherPin(pin("1234")))
        assertEquals("1234", android.lock)
        assertEquals(true, android.pinRule)
        assertEquals("1234", ScreenPasswordStore(file).current.teacherPin)
        assertEquals(ScreenPasswordAction.NOTHING, keeper.check())
        assertEquals(listOf("1234"), android.resets)
    }

    @Test fun theTokenIsGivenWhenTheTeacherSendsThePinBeforeAnyCheck() {
        assertEquals(true to ScreenPassword.PIN_SET, keeper().setTeacherPin(pin("1234")))
        assertEquals("1234", android.lock)
    }

    @Test fun aPinTheStudentChangesIsPutBack() {
        val keeper = keeper()
        keeper.setTeacherPin(pin("1234"))
        now += 60_000
        android.lock = "5678"
        assertFalse(keeper.isOwnChange())
        assertEquals(ScreenPasswordAction.RESTORE, keeper.lockChanged())
        assertEquals("1234", android.lock)
        assertFalse(ScreenPasswordStore(file).current.changedByOther)
        assertTrue(logged.contains("The teacher's lock screen PIN was put back (it had been changed)."))
    }

    @Test fun aPinTheStudentRemovesIsPutBackEvenWithoutAndroidSaying() {
        val keeper = keeper()
        keeper.setTeacherPin(pin("1234"))
        android.lock = null
        // The watchdog's check.
        assertEquals(ScreenPasswordAction.RESTORE, keeper.check())
        assertEquals("1234", android.lock)
        assertTrue(logged.contains("The teacher's lock screen PIN was put back (it had been removed)."))
    }

    @Test fun theAgentsOwnChangeIsNotTakenForTheStudents() {
        val keeper = keeper()
        var ownDuring: Boolean? = null
        android.duringReset = { ownDuring = keeper.isOwnChange() }
        keeper.setTeacherPin(pin("1234"))
        assertEquals(true, ownDuring)
        // Android's report comes a little later, maybe twice.
        now += 1_000
        assertTrue(keeper.isOwnChange())
        now += OwnChanges.WINDOW_MILLIS
        assertFalse(keeper.isOwnChange())
        assertEquals(listOf("1234"), android.resets)
    }

    @Test fun aChangeReportedBeforeTheAgentStopsIsUndoneAtItsNextStart() {
        val first = keeper()
        first.setTeacherPin(pin("1234"))
        android.lock = "5678"
        // Kept, as lockChanged does before its check.
        val store = ScreenPasswordStore(file)
        store.save(store.current.copy(changedByOther = true))
        assertEquals(ScreenPasswordAction.RESTORE, keeper().check(starting = true))
        assertEquals("1234", android.lock)
    }

    @Test fun theTeacherRemovesThePin() {
        val keeper = keeper()
        keeper.setTeacherPin(pin("1234"))
        assertEquals(true to ScreenPassword.PIN_REMOVED, keeper.setTeacherPin(ByteArray(0)))
        assertNull(android.lock)
        assertEquals(false, android.pinRule)
        assertNull(ScreenPasswordStore(file).current.teacherPin)
        assertEquals(ScreenPasswordAction.NOTHING, keeper.check())
    }

    @Test fun removingWhenThereIsNoLockSucceedsWithoutAChange() {
        val keeper = keeper()
        keeper.check(starting = true)
        assertEquals(true to ScreenPassword.PIN_REMOVED, keeper.setTeacherPin(ByteArray(0)))
        assertEquals(emptyList<String>(), android.resets)
    }

    @Test fun withoutTheTeachersPinAStudentsLockIsRemoved() {
        val keeper = keeper()
        keeper.check(starting = true)
        android.lock = "5678"
        assertEquals(ScreenPasswordAction.CLEAR, keeper.lockChanged())
        assertNull(android.lock)
        assertEquals(false, android.pinRule)
    }

    @Test fun aStudentsLockIsRemovedAlsoWithAnOldPinRuleLeftOn() {
        val keeper = keeper()
        keeper.check(starting = true)
        android.pinRule = true
        android.lock = "5678"
        assertEquals(ScreenPasswordAction.CLEAR, keeper.check(starting = true))
        assertNull(android.lock)
    }

    @Test fun aPhoneThatAlreadyHasALockWaitsForTheUnlock() {
        android.lock = "5678"
        val keeper = keeper()
        assertEquals(ScreenPasswordAction.SET_TOKEN, keeper.check(starting = true))
        assertEquals(ResetToken.PENDING, keeper.tokenState())
        assertEquals(false to ScreenPassword.TOKEN_PENDING, keeper.setTeacherPin(pin("1234")))
        assertEquals("5678", android.lock)
        assertNull(ScreenPasswordStore(file).current.teacherPin)
        assertEquals(ScreenPasswordAction.WAIT_FOR_UNLOCK, keeper.check())
        assertEquals(ScreenPasswordAction.WAIT_FOR_UNLOCK, keeper.check())
        // Logged once, not at every tick.
        assertEquals(1, logged.count { it.startsWith("The lock screen's reset token is pending") })
        assertTrue(logged.contains("Lock screen reset token: pendente."))
        assertEquals(1, android.tokensGiven)

        android.unlock()
        // No teacher's PIN yet: the student's lock goes.
        assertEquals(ScreenPasswordAction.CLEAR, keeper.check())
        assertNull(android.lock)
        assertEquals(true to ScreenPassword.PIN_SET, keeper.setTeacherPin(pin("1234")))
    }

    @Test fun aWaitingTokenForgottenAtARestartIsGivenAgain() {
        android.lock = "5678"
        val keeper = keeper()
        keeper.check(starting = true)
        android.restart()
        assertEquals(ScreenPasswordAction.SET_TOKEN, keeper().check(starting = true))
        assertEquals(2, android.tokensGiven)
        assertEquals(2, ScreenPasswordStore(file).current.tokenBoot)
        android.unlock()
        assertEquals(ResetToken.ACTIVE, keeper().tokenState())
    }

    @Test fun anActiveTokenAndThePinSurviveARestart() {
        keeper().setTeacherPin(pin("1234"))
        android.restart()
        val keeper = keeper()
        assertEquals(ScreenPasswordAction.NOTHING, keeper.check(starting = true))
        assertEquals(1, android.tokensGiven)
        assertEquals(listOf("1234"), android.resets)
    }

    @Test fun anInvalidPinChangesNothing() {
        val keeper = keeper()
        keeper.check(starting = true)
        for (text in listOf("123", "12345", "12a4", "abcd")) {
            assertEquals(false to PhoneScreenPassword.INVALID, keeper.setTeacherPin(text.toByteArray()))
        }
        assertNull(android.lock)
        assertEquals(emptyList<String>(), android.resets)
    }

    @Test fun notTheDeviceOwnerDoesNothing() {
        android.owner = false
        val keeper = keeper()
        assertEquals(ScreenPasswordAction.NOTHING, keeper.check(starting = true))
        assertEquals(false to ScreenPassword.NOT_OWNER, keeper.setTeacherPin(pin("1234")))
        assertEquals(0, android.tokensGiven)
        assertFalse(file.exists())
    }

    @Test fun aRefusedTokenIsAskedAgainOnlyAtTheNextStart() {
        android.refuseToken = true
        val keeper = keeper()
        assertEquals(ScreenPasswordAction.SET_TOKEN, keeper.check(starting = true))
        keeper.check()
        keeper.check()
        assertEquals(1, logged.count { it == "Android refused the lock screen's reset token." })
        android.refuseToken = false
        keeper.check()
        assertEquals(0, android.tokensGiven)
        keeper.check(starting = true)
        assertEquals(1, android.tokensGiven)
    }

    @Test fun theTeacherReadsWhenTheTokenIsRefused() {
        android.refuseToken = true
        assertEquals(false to ScreenPassword.TOKEN_REFUSED, keeper().setTeacherPin(pin("1234")))
    }

    @Test fun aRefusedPinLeavesWhatWasKept() {
        val keeper = keeper()
        keeper.setTeacherPin(pin("1234"))
        android.refuseReset = true
        assertEquals(false to ScreenPassword.RESET_REFUSED, keeper.setTeacherPin(pin("4321")))
        assertEquals("1234", android.lock)
        assertEquals("1234", ScreenPasswordStore(file).current.teacherPin)
    }

    @Test fun aRefusedPinRuleKeepsThePinAnyway() {
        android.refuseRule = true
        val keeper = keeper()
        assertEquals(true to ScreenPassword.PIN_SET, keeper.setTeacherPin(pin("1234")))
        android.lock = null
        assertEquals(ScreenPasswordAction.RESTORE, keeper.check())
        assertEquals("1234", android.lock)
        assertTrue(logged.contains("Android refused the lock screen's PIN rule"))
    }
}
