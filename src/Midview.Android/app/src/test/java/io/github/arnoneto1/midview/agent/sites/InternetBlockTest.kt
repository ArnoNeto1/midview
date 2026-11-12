package io.github.arnoneto1.midview.agent.sites

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

class InternetBlockTest {
    @get:Rule val folder = TemporaryFolder()

    private val own = "io.github.arnoneto1.midview.agent"
    private val ours = AlwaysOnVpn(own, lockdown = true)
    private val gw = FakeSiteGateway()
    private val file by lazy { File(folder.root, "internet-block.txt") }
    private val vpn = mutableListOf<String>()

    private fun block() = InternetBlock(gw, file, startVpn = { vpn += "start" }, stopVpn = { vpn += "stop" }, own = own)

    @Test fun blockingTurnsOnTheAgentsVpnWithNothingElseLetThrough() {
        assertTrue(block().apply(true))
        assertEquals(ours, gw.alwaysOn)
        assertEquals(listOf("start"), vpn)
        assertTrue(block().isBlocked)
    }

    @Test fun blockingAgainChangesNothing() {
        val block = block()
        block.apply(true)
        assertTrue(block.apply(true))
        assertEquals(listOf<AlwaysOnVpn?>(ours), gw.alwaysOnCalls)
        // The VPN is started again: after the agent was ended, it is gone.
        assertEquals(listOf("start", "start"), vpn)
    }

    @Test fun freeingTurnsTheVpnOffWhenNothingWasThereBefore() {
        val block = block()
        block.apply(true)
        assertTrue(block.apply(false))
        assertNull(gw.alwaysOn)
        assertEquals(listOf("start", "stop"), vpn)
        assertFalse(block.isBlocked)
        assertFalse(file.exists())
    }

    @Test fun freeingPutsBackTheVpnThatWasThere() {
        val theirs = AlwaysOnVpn("com.example.vpn", lockdown = false)
        gw.alwaysOn = theirs
        block().apply(true)
        // The agent ended and started again in between.
        assertTrue(block().apply(false))
        assertEquals(theirs, gw.alwaysOn)
    }

    @Test fun freeingWhenNothingWasBlockedTouchesNothing() {
        val theirs = AlwaysOnVpn("com.example.vpn", lockdown = true)
        gw.alwaysOn = theirs
        assertTrue(block().apply(false))
        assertEquals(theirs, gw.alwaysOn)
        assertTrue(gw.alwaysOnCalls.isEmpty())
        assertEquals(listOf("stop"), vpn)
    }

    @Test fun notTheDeviceOwnerCannotBlockButIsFree() {
        gw.owner = false
        assertFalse(block().apply(true))
        assertTrue(block().apply(false))
        assertTrue(vpn.isEmpty())
    }

    @Test fun aRefusedVpnIsAFailure() {
        gw.refuseAlwaysOn = true
        assertFalse(block().apply(true))
    }

    @Test fun whatWasThereIsRememberedOnlyOnce() {
        assertEquals(Previous(null), InternetBlock.toSave(current = null, saved = null, own = own))
        val theirs = AlwaysOnVpn("com.example.vpn", lockdown = false)
        assertEquals(Previous(theirs), InternetBlock.toSave(current = theirs, saved = null, own = own))
        // Blocked already (the agent started again): the first record stays.
        assertEquals(Previous(theirs), InternetBlock.toSave(current = ours, saved = Previous(theirs), own = own))
        // Blocked with no record (lost): nothing was there, as far as is known.
        assertEquals(Previous(null), InternetBlock.toSave(current = ours, saved = null, own = own))
    }

    @Test fun whatIsPutBack() {
        val theirs = AlwaysOnVpn("com.example.vpn", lockdown = true)
        assertEquals(theirs, InternetBlock.toRestore(Previous(theirs), own))
        assertNull(InternetBlock.toRestore(Previous(null), own))
        assertNull(InternetBlock.toRestore(null, own))
        // Never our own block again.
        assertNull(InternetBlock.toRestore(Previous(ours), own))
    }

    @Test fun theRecordIsReadBack() {
        val theirs = AlwaysOnVpn("com.example.vpn", lockdown = true)
        InternetBlock.savePrevious(file, Previous(theirs))
        assertEquals(Previous(theirs), InternetBlock.loadPrevious(file))
        InternetBlock.savePrevious(file, Previous(null))
        assertEquals(Previous(null), InternetBlock.loadPrevious(file))
        file.writeText("garbage")
        assertNull(InternetBlock.loadPrevious(file))
    }
}
