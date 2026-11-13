package io.github.arnoneto1.midview.agent.policy

import io.github.arnoneto1.midview.agent.provisioning.SchoolWifiName

// What SchoolWifi needs from Android's WifiManager and DevicePolicyManager,
// behind an interface so the decisions are tested on the JVM.
interface WifiControl {
    fun isEnabled(): Boolean

    // Turns the Wi-Fi on (the Device Owner may, also under no_change_wifi_state).
    fun enable(): Boolean

    // The id of the saved network with this name and security, added or
    // updated without a password (an existing one keeps its own); -1 when
    // Android has none it can use.
    fun savedNetwork(ssid: String, open: Boolean): Int

    // Joins a saved network now, as if the user had picked it.
    fun join(networkId: Int): Boolean

    // Keeps the networks the Device Owner added out of the user's hands:
    // they can be neither changed nor forgotten
    // (Settings.Global.WIFI_DEVICE_OWNER_CONFIGS_LOCKDOWN).
    fun lockOwnersNetworks()
}

// The school's Wi-Fi kept within the teacher's reach. While the network is
// locked the Wi-Fi stays on (keepOn: before Android 13 the student can still
// turn it off, so the watchdog turns it on again); when a free time ends the
// phone goes back to the school network the cable setup added (rejoin), even
// if the student turned the Wi-Fi off or joined another network meanwhile.
// The school network itself cannot be forgotten (lockOwnersNetworks). A
// device without a cable setup (school) only gets its Wi-Fi turned on, and
// Android joins the saved networks by itself. A refusal is logged, never thrown.
class SchoolWifi(
    private val control: WifiControl,
    private val school: () -> SchoolWifiName?,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    fun lockOwnersNetworks() = quietly("lock the school's Wi-Fi settings") { control.lockOwnersNetworks() }

    // True when the Wi-Fi was off and was turned on now.
    fun keepOn(): Boolean = quietly("turn the Wi-Fi on") {
        if (control.isEnabled()) return@quietly false
        val on = control.enable()
        log(if (on) "Wi-Fi turned on again." else "Android did not turn the Wi-Fi on.", null)
        on
    } ?: false

    fun rejoin() {
        keepOn()
        val name = school() ?: return
        quietly("join the school's Wi-Fi") {
            val id = control.savedNetwork(name.ssid, name.open)
            if (id < 0) {
                log("The school's Wi-Fi is not saved on this phone.", null)
            } else if (!control.join(id)) {
                log("Android did not join the school's Wi-Fi.", null)
            }
        }
    }

    private fun <T> quietly(what: String, action: () -> T): T? =
        try {
            action()
        } catch (e: RuntimeException) {
            log("Couldn't $what", e)
            null
        }
}
