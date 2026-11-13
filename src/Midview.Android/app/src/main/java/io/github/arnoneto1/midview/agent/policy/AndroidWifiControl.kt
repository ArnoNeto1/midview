package io.github.arnoneto1.midview.agent.policy

import android.app.admin.DevicePolicyManager
import android.content.ComponentName
import android.content.Context
import android.net.wifi.WifiConfiguration
import android.net.wifi.WifiManager
import io.github.arnoneto1.midview.agent.AdminReceiver

// WifiControl on WifiManager, as the Device Owner. setWifiEnabled,
// addNetwork and enableNetwork are deprecated and fail for an ordinary app
// from Android 10, but not for the Device Owner. The agent holds no location
// permission, so it never reads which network the phone is on: it names the
// school's by its SSID (addNetwork without a password finds the saved one
// and leaves its password alone).
class AndroidWifiControl(context: Context) : WifiControl {
    private val app = context.applicationContext
    private val wifi = app.getSystemService(WifiManager::class.java)
    private val dpm = app.getSystemService(DevicePolicyManager::class.java)
    private val admin = ComponentName(app, AdminReceiver::class.java)

    override fun isEnabled(): Boolean = wifi.isWifiEnabled

    @Suppress("DEPRECATION")
    override fun enable(): Boolean = wifi.setWifiEnabled(true)

    @Suppress("DEPRECATION")
    override fun savedNetwork(ssid: String, open: Boolean): Int {
        val config = WifiConfiguration().apply {
            SSID = "\"" + ssid + "\""
            allowedKeyManagement.set(if (open) WifiConfiguration.KeyMgmt.NONE else WifiConfiguration.KeyMgmt.WPA_PSK)
        }
        return wifi.addNetwork(config)
    }

    @Suppress("DEPRECATION")
    override fun join(networkId: Int): Boolean = wifi.enableNetwork(networkId, true)

    override fun lockOwnersNetworks() = dpm.setGlobalSetting(admin, WIFI_DEVICE_OWNER_CONFIGS_LOCKDOWN, "1")

    private companion object {
        // Settings.Global.WIFI_DEVICE_OWNER_CONFIGS_LOCKDOWN.
        const val WIFI_DEVICE_OWNER_CONFIGS_LOCKDOWN = "wifi_device_owner_configs_lockdown"
    }
}
