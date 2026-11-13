package io.github.arnoneto1.midview.agent.policy

import io.github.arnoneto1.midview.agent.provisioning.SchoolWifiName
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SchoolWifiTest {
    private class FakeControl(var enabled: Boolean = true, val savedId: Int = 4, val refuse: Boolean = false) : WifiControl {
        val calls = mutableListOf<String>()

        override fun isEnabled(): Boolean = enabled

        override fun enable(): Boolean {
            calls += "enable"
            if (refuse) throw SecurityException("refused")
            enabled = true
            return true
        }

        override fun savedNetwork(ssid: String, open: Boolean): Int {
            calls += "saved $ssid open=$open"
            return savedId
        }

        override fun join(networkId: Int): Boolean {
            calls += "join $networkId"
            return true
        }

        override fun lockOwnersNetworks() {
            calls += "lock"
        }
    }

    @Test fun keepOnTurnsAWifiThatIsOffOn() {
        val control = FakeControl(enabled = false)
        assertTrue(SchoolWifi(control, { null }).keepOn())
        assertEquals(listOf("enable"), control.calls)
    }

    @Test fun keepOnLeavesAWifiThatIsOnAlone() {
        val control = FakeControl(enabled = true)
        assertFalse(SchoolWifi(control, { null }).keepOn())
        assertTrue(control.calls.isEmpty())
    }

    // Off, or on another network: on again and on the school's, by its name.
    @Test fun rejoinTurnsTheWifiOnAndJoinsTheSchoolsNetwork() {
        val control = FakeControl(enabled = false)
        SchoolWifi(control, { SchoolWifiName("Lab", open = false) }).rejoin()
        assertEquals(listOf("enable", "saved Lab open=false", "join 4"), control.calls)
    }

    @Test fun anOpenSchoolNetworkIsAskedForAsOpen() {
        val control = FakeControl()
        SchoolWifi(control, { SchoolWifiName("Aberta", open = true) }).rejoin()
        assertEquals(listOf("saved Aberta open=true", "join 4"), control.calls)
    }

    @Test fun aNetworkAndroidDoesNotHaveIsNotJoined() {
        val control = FakeControl(savedId = -1)
        SchoolWifi(control, { SchoolWifiName("Lab", open = false) }).rejoin()
        assertEquals(listOf("saved Lab open=false"), control.calls)
    }

    // Without a cable setup only the Wi-Fi comes on: Android joins the saved networks.
    @Test fun withoutASetupOnlyTheWifiComesOn() {
        val control = FakeControl(enabled = false)
        SchoolWifi(control, { null }).rejoin()
        assertEquals(listOf("enable"), control.calls)
    }

    // A refusal is logged, never thrown at the lock it goes with.
    @Test fun aRefusalIsLoggedNotThrown() {
        val control = FakeControl(enabled = false, refuse = true)
        val logged = mutableListOf<String>()
        val wifi = SchoolWifi(control, { SchoolWifiName("Lab", open = false) }) { text, _ -> logged += text }

        assertFalse(wifi.keepOn())
        wifi.rejoin()

        assertTrue(logged.first().startsWith("Couldn't turn the Wi-Fi on"))
        assertTrue("join 4" in control.calls)
    }

    @Test fun theOwnersNetworksAreLocked() {
        val control = FakeControl()
        SchoolWifi(control, { null }).lockOwnersNetworks()
        assertEquals(listOf("lock"), control.calls)
    }
}
