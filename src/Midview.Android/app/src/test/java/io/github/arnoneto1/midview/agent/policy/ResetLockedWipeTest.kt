package io.github.arnoneto1.midview.agent.policy

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test

class ResetLockedWipeTest {
    private val calls = mutableListOf<String>()

    private fun run(
        liftFails: Boolean = false,
        wipeFails: Boolean = false,
        logged: MutableList<String> = mutableListOf(),
        locked: () -> Boolean = { true },
    ) =
        ResetLockedWipe.run(
            isLocked = locked,
            lift = { calls += "lift"; if (liftFails) throw SecurityException("no lift") },
            lock = { calls += "lock" },
            wipe = { calls += "wipe"; if (wipeFails) throw SecurityException("refused") },
            log = { text, _ -> logged += text },
        )

    @Test fun liftsThenWipes() {
        run()
        assertEquals(listOf("lift", "wipe"), calls)
    }

    @Test fun failedWipePutsTheLockBackAndRethrows() {
        try {
            run(wipeFails = true)
            fail("expected the refusal")
        } catch (e: SecurityException) {
            assertEquals("refused", e.message)
        }
        assertEquals(listOf("lift", "wipe", "lock"), calls)
    }

    @Test fun lockThatCannotBeLiftedStillTriesTheWipeAndIsNotLockedAgain() {
        val logged = mutableListOf<String>()
        try {
            run(liftFails = true, wipeFails = true, logged = logged)
            fail("expected the refusal")
        } catch (e: SecurityException) {
        }
        assertEquals(listOf("lift", "wipe"), calls)
        assertTrue(logged.isNotEmpty())
    }

    // A debug build never had the lock on: it is neither lifted nor put on.
    @Test fun aPhoneWithoutTheLockIsNotLockedAfterAFailedWipe() {
        try {
            run(wipeFails = true, locked = { false })
            fail("expected the refusal")
        } catch (e: SecurityException) {
        }
        assertEquals(listOf("wipe"), calls)
    }

    @Test fun aPhoneWithoutTheLockJustWipes() {
        run(locked = { false })
        assertEquals(listOf("wipe"), calls)
    }

    @Test fun aLockThatCannotBeReadCountsAsOn() {
        val logged = mutableListOf<String>()
        try {
            run(wipeFails = true, logged = logged, locked = { throw SecurityException("no read") })
            fail("expected the refusal")
        } catch (e: SecurityException) {
            assertEquals("refused", e.message)
        }
        assertEquals(listOf("lift", "wipe", "lock"), calls)
        assertTrue(logged.isNotEmpty())
    }
}
