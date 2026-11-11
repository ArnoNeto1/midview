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
        override val debuggable: Boolean = false,
    ) : DevicePolicyGateway {
        val restrictions = mutableSetOf<String>()
        val uninstallBlocked = mutableMapOf<String, Boolean>()
        var controlDisabledPackages: List<String>? = null
        val granted = mutableSetOf<Pair<String, String>>()
        var lockTaskAllowed: List<String>? = null
        var lockTaskFeatureFlags: Int? = null
        var accessibilityPermitted: List<String>? = listOf("never set")
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

        override fun setLockTaskPackages(pkgs: List<String>) {
            calls++
            lockTaskAllowed = pkgs
        }

        override fun setLockTaskFeatures(features: Int) {
            calls++
            lockTaskFeatureFlags = features
        }

        override fun setPermittedAccessibilityServices(pkgs: List<String>?) {
            calls++
            accessibilityPermitted = pkgs
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
        assertEquals(mapOf(pkg to true), android10.uninstallBlocked)

        val android11 = FakeGateway(sdkInt = 30)
        Lockdown(android11).apply()
        assertEquals(listOf(pkg), android11.controlDisabledPackages)
    }

    // Android 10 has no per-app lock: force stop and clear data are closed
    // for every app instead.
    @Test fun closesAppsControlOnlyOnAndroid10() {
        val android10 = FakeGateway(sdkInt = 29)
        Lockdown(android10).apply()
        assertEquals(all + "no_control_apps", android10.restrictions)

        val android11 = FakeGateway(sdkInt = 30)
        Lockdown(android11).apply()
        assertEquals(all, android11.restrictions)
        assertEquals(listOf(pkg), android11.controlDisabledPackages)
    }

    // A test phone or emulator made Device Owner with a debug build can
    // still be reached by adb and reset.
    @Test fun debugBuildKeepsAdbAndFactoryReset() {
        val gw = FakeGateway(sdkInt = 34, debuggable = true)

        assertTrue(Lockdown(gw).apply())

        assertEquals(all - "no_debugging_features" - "no_factory_reset", gw.restrictions)
        assertEquals(mapOf(pkg to true), gw.uninstallBlocked)
        assertEquals(listOf(pkg), gw.controlDisabledPackages)
        assertEquals(setOf(pkg to "android.permission.POST_NOTIFICATIONS"), gw.granted)
    }

    @Test fun debugBuildOnAndroid10StillClosesAppsControl() {
        val gw = FakeGateway(sdkInt = 29, debuggable = true)

        Lockdown(gw).apply()

        assertEquals(all - "no_debugging_features" - "no_factory_reset" + "no_control_apps", gw.restrictions)
    }

    @Test fun releaseBuildKeepsEveryLock() {
        val gw = FakeGateway(sdkInt = 34, debuggable = false)

        Lockdown(gw).apply()

        assertTrue(gw.restrictions.containsAll(setOf("no_debugging_features", "no_factory_reset")))
        assertEquals(all, gw.restrictions)
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

    // "Bloquear telas": only this app may hold the phone in lock task mode,
    // with nothing of the system's (home, recents, notifications, the power
    // menu) left to reach. On debug builds too.
    @Test fun lockdownAllowsLockTaskForOwnPackage() {
        for (debuggable in listOf(false, true)) {
            val gw = FakeGateway(sdkInt = 29, debuggable = debuggable)

            assertTrue(Lockdown(gw).apply())

            assertEquals(listOf(pkg), gw.lockTaskAllowed)
            assertEquals(0, gw.lockTaskFeatureFlags)
        }
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
        assertEquals(listOf(pkg), gw.lockTaskAllowed)
    }

    // The screen is seen through the app's accessibility service: the
    // Device Owner never leaves it out (null: every service allowed).
    @Test fun accessibilityServicesAreAllPermitted() {
        for (debuggable in listOf(false, true)) {
            val gw = FakeGateway(sdkInt = 33, debuggable = debuggable)

            assertTrue(Lockdown(gw).apply())

            assertEquals(null, gw.accessibilityPermitted)
        }
    }
}
