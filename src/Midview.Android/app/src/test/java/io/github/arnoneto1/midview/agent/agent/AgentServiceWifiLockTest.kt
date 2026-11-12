package io.github.arnoneto1.midview.agent.agent

import org.junit.Assert.assertEquals
import org.junit.Test

class AgentServiceWifiLockTest {
    // The phone keeps answering the teacher with the screen off: the
    // high-performance lock, and from Android 14 (which replaces that one
    // with it) the low-latency one (AgentWifi says what holds then).
    @Test fun wifiLockModeByAndroidVersion() {
        assertEquals(WIFI_MODE_FULL_HIGH_PERF, AgentWifi.lockMode(29))
        assertEquals(WIFI_MODE_FULL_HIGH_PERF, AgentWifi.lockMode(33))
        assertEquals(WIFI_MODE_FULL_LOW_LATENCY, AgentWifi.lockMode(34))
        assertEquals(WIFI_MODE_FULL_LOW_LATENCY, AgentWifi.lockMode(35))
    }

    private companion object {
        // WifiManager.WIFI_MODE_FULL_HIGH_PERF and WIFI_MODE_FULL_LOW_LATENCY.
        const val WIFI_MODE_FULL_HIGH_PERF = 3
        const val WIFI_MODE_FULL_LOW_LATENCY = 4
    }
}
