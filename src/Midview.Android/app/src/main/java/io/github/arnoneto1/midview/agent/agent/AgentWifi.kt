package io.github.arnoneto1.midview.agent.agent

import android.content.Context
import android.net.wifi.WifiManager
import android.os.Build

// The Wi-Fi as the agent holds it, so the teacher's search and commands are
// answered also with the screen off.
//
// Up to Android 13 a WIFI_MODE_FULL_HIGH_PERF lock keeps the Wi-Fi out of
// power save, screen off or not. From Android 14 that mode is gone: Android
// turns it into WIFI_MODE_FULL_LOW_LATENCY, which only acts with the screen
// on and the app in front, and no lock acts with the screen off. There the
// phone still stays on the network with the screen off (Android keeps the
// Wi-Fi connected while asleep; the router holds the packets for a phone in
// power save, and they wake it), and what lets the agent use the network in
// Doze is its foreground service (AgentService, type specialUse: Android
// keeps the network of a process with a foreground service, and gives that
// type no time limit). The low-latency lock is still held: it helps while
// the screen is on. A teacher's command may then take a little longer to be
// answered with the screen off (power save). Only Android 13 was tried (the
// Redmi 10C); an Android 14 phone with the screen off is still to be checked.
class AgentWifi(context: Context) {
    private val wifi = context.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
    private var lock: WifiManager.WifiLock? = null

    fun hold() {
        if (lock != null) return
        lock = wifi.createWifiLock(lockMode(Build.VERSION.SDK_INT), "Midview agent").apply {
            setReferenceCounted(false)
            acquire()
        }
    }

    fun release() {
        lock?.let { if (it.isHeld) it.release() }
        lock = null
    }

    companion object {
        // WifiManager.WIFI_MODE_FULL_HIGH_PERF up to Android 13, and
        // WIFI_MODE_FULL_LOW_LATENCY from Android 14, which replaces it.
        @Suppress("DEPRECATION")
        fun lockMode(sdkInt: Int): Int =
            if (sdkInt >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
                WifiManager.WIFI_MODE_FULL_LOW_LATENCY
            } else {
                WifiManager.WIFI_MODE_FULL_HIGH_PERF
            }
    }
}
