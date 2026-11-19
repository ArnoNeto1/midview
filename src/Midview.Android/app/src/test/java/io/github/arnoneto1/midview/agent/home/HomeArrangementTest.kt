package io.github.arnoneto1.midview.agent.home

import io.github.arnoneto1.midview.agent.protocol.PhoneHomeStates
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

// What is checked before the home screen is touched over the network, and
// how each outcome reads (PhoneHomeStates).
class HomeArrangementTest {
    private class FakeDevice(
        var home: String? = Launcher3Home.LAUNCHER,
        var accessibility: Boolean = true,
        var ready: Boolean = true,
        var opens: Boolean = true,
    ) : HomeDevice {
        val launcher = FakeLauncher3()
        var opened = 0
        var closed = 0

        override fun defaultHome() = home
        override fun accessibilityRunning() = accessibility
        override fun screenReady() = ready
        override fun open(): HomeSession? {
            if (!opens) return null
            opened++
            return object : HomeSession {
                override val driver: HomeDriver get() = launcher
                override fun close() {
                    closed++
                }
            }
        }
    }

    @Test fun aLauncher3HomeIsArranged() {
        val device = FakeDevice()
        assertEquals(PhoneHomeStates.ARRANGED, HomeArrangement.arrange(device))
        assertEquals(FakeLauncher3.CLASS_APPS, device.launcher.dock)
        assertEquals(1, device.closed)
    }

    @Test fun anythingUnexpectedLeavesItForTheTeachersHands() {
        val device = FakeDevice().apply { launcher.switchesStuck = true }
        assertEquals(PhoneHomeStates.BY_HAND, HomeArrangement.arrange(device))
        assertEquals(1, device.closed)
    }

    @Test fun anotherLauncherIsNeverTouched() {
        for (home in listOf("com.miui.home", "com.google.android.apps.nexuslauncher", "android", null)) {
            val device = FakeDevice(home = home)
            assertEquals(PhoneHomeStates.OTHER_LAUNCHER, HomeArrangement.arrange(device))
            assertEquals(0, device.opened)
            assertTrue(device.launcher.commands.isEmpty())
        }
    }

    @Test fun withoutTheAccessibilityServiceNothingIsTried() {
        val device = FakeDevice(accessibility = false)
        assertEquals(PhoneHomeStates.NO_ACCESSIBILITY, HomeArrangement.arrange(device))
        assertTrue(device.launcher.commands.isEmpty())

        val gone = FakeDevice(opens = false)
        assertEquals(PhoneHomeStates.NO_ACCESSIBILITY, HomeArrangement.arrange(gone))
    }

    @Test fun aScreenOffOrLockedIsLeftAlone() {
        val device = FakeDevice(ready = false)
        assertEquals(PhoneHomeStates.SCREEN_OFF, HomeArrangement.arrange(device))
        assertEquals(0, device.opened)
        assertTrue(device.launcher.commands.isEmpty())
    }

    @Test fun theSessionIsClosedEvenWhenTheDriverThrows() {
        val device = FakeDevice()
        val throwing = object : HomeDevice by device {
            override fun open(): HomeSession = object : HomeSession {
                override val driver: HomeDriver = object : HomeDriver by device.launcher {
                    override fun openLauncherSettings(): Boolean = throw IllegalStateException("gone")
                }

                override fun close() {
                    device.closed++
                }
            }
        }
        try {
            HomeArrangement.arrange(throwing)
        } catch (_: IllegalStateException) {
        }
        assertEquals(1, device.closed)
        assertFalse(device.launcher.commands.isEmpty())
    }
}
