package io.github.arnoneto1.midview.agent.policy

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

// "Contas do aparelho": the accounts lock lifted for the minutes the teacher
// asked, and back by itself when they end, also after a restart (the same
// TimedFree as the Wi-Fi's, NetworkFreeTest).
class AccountsFreeTest {
    @get:Rule val folder = TemporaryFolder()

    private val restrictions = Lockdown.ACCOUNT_RESTRICTIONS.toMutableSet()
    private var clock = 1_000_000L
    private var owner = true
    private var refuseClear = false
    private val file by lazy { File(folder.root, "accounts-free.txt") }
    private val minute = TimedFree.MINUTE_MILLIS

    private fun accounts() = AccountsFree(
        file, Lockdown.ACCOUNT_RESTRICTIONS,
        isDeviceOwner = { owner },
        addRestriction = { restrictions += it },
        clearRestriction = {
            if (refuseClear) throw SecurityException("refused $it")
            restrictions -= it
        },
        now = { clock },
    )

    @Test fun freeingLiftsTheAccountsLockForTheTime() {
        val accounts = accounts()

        assertEquals(true to "contas do aparelho liberadas por 10 minutos", accounts.free(10))

        assertTrue(restrictions.isEmpty())
        assertEquals(clock + 10 * minute, accounts.freeUntil())
        assertEquals(true to "contas do aparelho liberadas por 1 minuto", accounts.free(1))
    }

    @Test fun theLockComesBackWhenTheTimeIsUpEvenAfterARestart() {
        accounts().free(30)

        clock += 30 * minute - 1
        val again = accounts()
        assertFalse(again.check())
        assertTrue(restrictions.isEmpty())

        clock += 1
        assertTrue(again.check())
        assertEquals(setOf("no_modify_accounts"), restrictions)
        assertNull(again.freeUntil())
        assertFalse(file.exists())
    }

    @Test fun zeroLocksNow() {
        val accounts = accounts()
        accounts.free(10)

        assertEquals(true to AccountsFree.LOCKED, accounts.handle(byteArrayOf(0)))
        assertEquals(setOf("no_modify_accounts"), restrictions)
        assertNull(accounts.freeUntil())
    }

    // Lockdown at a start leaves the lock off while the time runs, and puts it back after.
    @Test fun aStartLocksOnlyWhenNotFree() {
        var locked = 0
        val accounts = accounts()
        accounts.free(10)
        accounts.unlessFree { locked++ }
        assertEquals(0, locked)

        clock += 10 * minute
        accounts.unlessFree { locked++ }
        assertEquals(1, locked)
    }

    @Test fun aDeviceNotPreparedOrAnInvalidTimeIsRefused() {
        assertEquals(false to TimedFree.INVALID_REQUEST, accounts().handle(byteArrayOf(61)))
        assertEquals(false to TimedFree.INVALID_REQUEST, accounts().handle(ByteArray(0)))
        owner = false
        assertEquals(false to TimedFree.NOT_PREPARED, accounts().free(10))
        assertEquals(setOf("no_modify_accounts"), restrictions)
    }

    // Android refusing to lift it: the lock stays and nothing counts as free.
    @Test fun aRefusalLeavesItLocked() {
        refuseClear = true
        val accounts = accounts()

        assertEquals(false to AccountsFree.NOT_FREED, accounts.free(10))
        assertEquals(setOf("no_modify_accounts"), restrictions)
        assertNull(accounts.freeUntil())
        assertFalse(file.exists())
    }

    // The Wi-Fi's and the accounts' free times are kept apart.
    @Test fun itsOwnFileAndKind() {
        accounts().free(10)
        val network = NetworkFree(
            file, listOf("no_config_wifi"), isDeviceOwner = { true }, addRestriction = {}, clearRestriction = {}, now = { clock },
        )
        assertNull(network.freeUntil())
    }
}
