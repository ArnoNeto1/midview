package io.github.arnoneto1.midview.agent.policy

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test

class LockdownTest {
    private class FakeGateway(
        override val sdkInt: Int = 34,
        private val owner: Boolean = true,
        private val failing: String? = null,
    ) : DevicePolicyGateway {
        val restrictions = mutableSetOf<String>()
        val uninstallBlocked = mutableMapOf<String, Boolean>()
        var controlDisabledPackages: List<String>? = null
        val granted = mutableSetOf<Pair<String, String>>()
        var calls = 0

        override fun addUserRestriction(r: String) {
            calls++
            if (r == failing) throw SecurityException("refused $r")
            restrictions += r
        }

        override fun setUninstallBlocked(pkg: String, blocked: Boolean) {
            calls++
            uninstallBlocked[pkg] = blocked
        }

        override fun setUserControlDisabled(pkgs: List<String>) {
            calls++
            controlDisabledPackages = pkgs
        }

        override fun grantPermission(pkg: String, permission: String) {
            calls++
            granted += pkg to permission
        }

        override fun isDeviceOwner(): Boolean = owner
    }

    private val pkg = "io.github.arnoneto1.midview.agent"
    private val all = setOf(
        "no_factory_reset",
        "no_debugging_features",
        "no_safe_boot",
        "no_add_user",
        "no_usb_file_transfer",
    )

    @Test fun appliesAllRestrictionsWhenDeviceOwner() {
        val gw = FakeGateway(sdkInt = 34)

        assertTrue(Lockdown(gw).apply())

        assertEquals(all, gw.restrictions)
        assertEquals(mapOf(pkg to true), gw.uninstallBlocked)
        assertEquals(listOf(pkg), gw.controlDisabledPackages)
    }

    @Test fun blocksForceStopOnlyFromAndroid11() {
        val android10 = FakeGateway(sdkInt = 29)
        Lockdown(android10).apply()
        assertEquals(null, android10.controlDisabledPackages)
        assertEquals(all, android10.restrictions)
        assertEquals(mapOf(pkg to true), android10.uninstallBlocked)

        val android11 = FakeGateway(sdkInt = 30)
        Lockdown(android11).apply()
        assertEquals(listOf(pkg), android11.controlDisabledPackages)
    }

    // The pairing code goes in a notification; from Android 13 the app
    // needs the permission, and on a school phone nobody is there to tap
    // "Permitir".
    @Test fun grantsNotificationsOnlyFromAndroid13() {
        val android12 = FakeGateway(sdkInt = 32)
        Lockdown(android12).apply()
        assertTrue(android12.granted.isEmpty())

        val android13 = FakeGateway(sdkInt = 33)
        Lockdown(android13).apply()
        assertEquals(setOf(pkg to "android.permission.POST_NOTIFICATIONS"), android13.granted)
    }

    @Test fun doesNothingWhenNotDeviceOwner() {
        val gw = FakeGateway(sdkInt = 34, owner = false)

        assertFalse(Lockdown(gw).apply())

        assertEquals(0, gw.calls)
    }

    @Test fun applyTwiceIsHarmless() {
        val gw = FakeGateway(sdkInt = 34)
        val lockdown = Lockdown(gw)

        assertTrue(lockdown.apply())
        assertTrue(lockdown.apply())

        assertEquals(all, gw.restrictions)
        assertEquals(mapOf(pkg to true), gw.uninstallBlocked)
        assertEquals(listOf(pkg), gw.controlDisabledPackages)
        assertEquals(setOf(pkg to "android.permission.POST_NOTIFICATIONS"), gw.granted)
    }

    // One lock the system refuses does not leave the others off.
    @Test fun aRefusedStepStillAppliesTheOthersAndIsReported() {
        val gw = FakeGateway(sdkInt = 34, failing = "no_safe_boot")

        try {
            Lockdown(gw).apply()
            fail("the refused step must be reported")
        } catch (e: SecurityException) {
            assertEquals("refused no_safe_boot", e.message)
        }

        assertEquals(all - "no_safe_boot", gw.restrictions)
        assertEquals(mapOf(pkg to true), gw.uninstallBlocked)
        assertEquals(listOf(pkg), gw.controlDisabledPackages)
        assertEquals(setOf(pkg to "android.permission.POST_NOTIFICATIONS"), gw.granted)
    }
}
