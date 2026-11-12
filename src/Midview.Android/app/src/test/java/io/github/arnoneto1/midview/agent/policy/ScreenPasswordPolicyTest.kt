package io.github.arnoneto1.midview.agent.policy

import io.github.arnoneto1.midview.agent.policy.ScreenPasswordAction.CLEAR
import io.github.arnoneto1.midview.agent.policy.ScreenPasswordAction.NOTHING
import io.github.arnoneto1.midview.agent.policy.ScreenPasswordAction.RESTORE
import io.github.arnoneto1.midview.agent.policy.ScreenPasswordAction.SET_TOKEN
import io.github.arnoneto1.midview.agent.policy.ScreenPasswordAction.WAIT_FOR_UNLOCK
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ScreenPasswordPolicyTest {
    private fun decide(secure: Boolean, token: ResetToken, pin: String?, changed: Boolean = false, owner: Boolean = true) =
        ScreenPasswordPolicy.decide(owner, secure, token, pin, changed)

    @Test fun notTheDeviceOwnerDoesNothing() {
        for (secure in listOf(true, false)) {
            for (token in ResetToken.entries) {
                for (pin in listOf(null, "1234")) {
                    for (changed in listOf(true, false)) {
                        assertEquals(NOTHING, decide(secure, token, pin, changed, owner = false))
                    }
                }
            }
        }
    }

    @Test fun withoutATokenOneIsGiven() {
        for (secure in listOf(true, false)) {
            for (pin in listOf(null, "1234")) {
                assertEquals(SET_TOKEN, decide(secure, ResetToken.NONE, pin, changed = true))
            }
        }
    }

    @Test fun aPendingTokenWaitsForTheUnlock() {
        assertEquals(WAIT_FOR_UNLOCK, decide(true, ResetToken.PENDING, null))
        assertEquals(WAIT_FOR_UNLOCK, decide(true, ResetToken.PENDING, "1234", changed = true))
    }

    @Test fun withoutTheTeachersPinTheStudentsLockIsRemoved() {
        assertEquals(CLEAR, decide(true, ResetToken.ACTIVE, null))
        assertEquals(CLEAR, decide(true, ResetToken.ACTIVE, null, changed = true))
        assertEquals(NOTHING, decide(false, ResetToken.ACTIVE, null))
        assertEquals(NOTHING, decide(false, ResetToken.ACTIVE, null, changed = true))
    }

    @Test fun theTeachersPinRemovedIsPutBack() {
        assertEquals(RESTORE, decide(false, ResetToken.ACTIVE, "1234"))
        assertEquals(RESTORE, decide(false, ResetToken.ACTIVE, "1234", changed = true))
    }

    @Test fun theTeachersPinChangedIsPutBack() {
        assertEquals(RESTORE, decide(true, ResetToken.ACTIVE, "1234", changed = true))
    }

    @Test fun theTeachersPinInPlaceIsLeftAlone() {
        assertEquals(NOTHING, decide(true, ResetToken.ACTIVE, "1234"))
    }

    @Test fun theTokensState() {
        val t = ScreenPasswordPolicy::token
        assertEquals(ResetToken.NONE, t(false, false, false, false))
        assertEquals(ResetToken.NONE, t(false, true, true, true))
        assertEquals(ResetToken.ACTIVE, t(true, true, false, true))
        assertEquals(ResetToken.ACTIVE, t(true, true, true, false))
        // Given while the phone had a lock: waits for the unlock, this boot.
        assertEquals(ResetToken.PENDING, t(true, false, true, true))
        // A waiting token is forgotten at a restart: given again.
        assertEquals(ResetToken.NONE, t(true, false, false, true))
        // No lock and still not active: Android lost it, given again.
        assertEquals(ResetToken.NONE, t(true, false, true, false))
    }

    // The teacher's PIN unknown (a damaged file): the lock stays as it is,
    // never removed.
    @Test fun anUnknownTeachersPinLeavesTheLockAsItIs() {
        assertEquals(
            ScreenPasswordAction.NOTHING,
            ScreenPasswordPolicy.decide(true, secure = true, ResetToken.ACTIVE, teacherPin = null, changedByOther = true, pinUnknown = true),
        )
        assertEquals(
            ScreenPasswordAction.CLEAR,
            ScreenPasswordPolicy.decide(true, secure = true, ResetToken.ACTIVE, teacherPin = null, changedByOther = true),
        )
    }

    @Test fun aReportWhileTheAgentChangesTheLockIsItsOwn() {
        var now = 1_000L
        val own = OwnChanges { now }
        assertFalse(own.isOwn())
        own.starting()
        assertTrue(own.isOwn())
        now += 60_000
        assertTrue(own.isOwn())
        own.finished()
        // Its report came already; a repeat of it a moment later too.
        now += 1_000
        assertTrue(own.isOwn())
        now += OwnChanges.REPEAT_MILLIS
        assertFalse(own.isOwn())
    }

    @Test fun eachChangeOwesOneReport() {
        var now = 1_000L
        val own = OwnChanges { now }
        own.starting()
        own.finished()
        now += 2_000
        assertTrue(own.isOwn())
        // The same report again, at once: some phones send two.
        now += 100
        assertTrue(own.isOwn())
        // A change two seconds later is the student's, within the old window.
        now += 2_000
        assertFalse(own.isOwn())
    }

    @Test fun aReportOwedTooLongAgoIsSomeoneElses() {
        var now = 1_000L
        val own = OwnChanges { now }
        own.starting()
        own.finished()
        now += OwnChanges.WINDOW_MILLIS + 1
        assertFalse(own.isOwn())
    }

    @Test fun aChangeAndroidRefusedOwesNothing() {
        var now = 1_000L
        val own = OwnChanges { now }
        own.starting()
        own.finished(changed = false)
        now += 100
        assertFalse(own.isOwn())
    }

    @Test fun twoChangesOweTwoReports() {
        var now = 1_000L
        val own = OwnChanges { now }
        repeat(2) {
            own.starting()
            own.finished()
        }
        now += 2_000
        assertTrue(own.isOwn())
        now += 2_000
        assertTrue(own.isOwn())
        now += 2_000
        assertFalse(own.isOwn())
    }
}
