package io.github.arnoneto1.midview.agent.notifications

import io.github.arnoneto1.midview.agent.policy.Lockdown
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

// Android 13+: POST_NOTIFICATIONS denied to every other app, once each.
class NotificationDenialTest {
    private class FakePermissions(override var sdkInt: Int = 33) : NotificationPermissions {
        var owner = true
        var isLeaving = false
        val apps = mutableListOf(Lockdown.PACKAGE, "com.whatsapp", "com.android.chrome", "com.google.android.dialer", "android")
        val denied = mutableSetOf<String>()
        val refused = mutableSetOf<String>()
        val asked = mutableListOf<String>()

        override fun isDeviceOwner() = owner
        override fun leaving() = isLeaving
        override fun installed(): List<String> = apps.toList()
        override fun calls() = setOf("com.google.android.dialer")
        override fun isDenied(pkg: String) = pkg in denied

        override fun deny(pkg: String): Boolean {
            asked += pkg
            if (pkg in refused) return false
            denied += pkg
            return true
        }
    }

    private val gw = FakePermissions()
    private val logs = mutableListOf<String>()
    private val denial = NotificationDenial(gw) { text, _ -> logs += text }

    @Test fun everyOtherAppIsDenied() {
        denial.apply()

        assertEquals(setOf("com.android.chrome", "com.whatsapp"), gw.denied)
        assertEquals(listOf("Notifications denied to 2 apps."), logs)
    }

    // At every app installed or updated: only the new one is asked.
    @Test fun anAppInstalledLaterIsDeniedAsItArrives() {
        denial.apply()
        gw.asked.clear()
        gw.apps += "com.instagram.android"

        denial.apply()

        assertEquals(listOf("com.instagram.android"), gw.asked)
    }

    @Test fun nothingNewAsksNothingAndSaysNothing() {
        denial.apply()
        gw.asked.clear()
        logs.clear()

        denial.apply()

        assertTrue(gw.asked.isEmpty())
        assertTrue(logs.isEmpty())
    }

    // A system app's fixed permission: not asked again until the agent starts again.
    @Test fun anAppAndroidRefusedIsNotAskedAgain() {
        gw.refused += "com.android.chrome"
        denial.apply()
        assertEquals(listOf("Notifications denied to 1 apps; Android kept 1."), logs)
        gw.asked.clear()

        denial.apply()

        assertTrue(gw.asked.isEmpty())
    }

    @Test fun beforeAndroid13NothingIsDenied() {
        gw.sdkInt = 29

        denial.apply()

        assertTrue(gw.asked.isEmpty())
    }

    @Test fun notTheDeviceOwnerOrLeavingNothingIsDenied() {
        gw.owner = false
        denial.apply()
        gw.owner = true
        gw.isLeaving = true
        denial.apply()

        assertTrue(gw.asked.isEmpty())
    }
}
