package io.github.arnoneto1.midview.agent.policy

import android.app.admin.DevicePolicyManager
import android.content.ComponentName
import android.content.Context
import android.content.pm.ApplicationInfo
import android.content.pm.PackageManager
import android.net.wifi.WifiConfiguration
import android.net.wifi.WifiManager
import android.os.Build
import android.os.Bundle
import android.util.Log
import io.github.arnoneto1.midview.agent.AdminReceiver
import io.github.arnoneto1.midview.agent.optimize.PrivateDns
import io.github.arnoneto1.midview.agent.sites.AlwaysOnVpn

// DevicePolicyManager for Lockdown, as the app's AdminReceiver.
class AndroidDevicePolicyGateway(context: Context) : DevicePolicyGateway {
    private val app = context.applicationContext
    private val dpm = app.getSystemService(DevicePolicyManager::class.java)
    private val admin = ComponentName(app, AdminReceiver::class.java)

    override val sdkInt: Int = Build.VERSION.SDK_INT

    override val debuggable: Boolean = (app.applicationInfo.flags and ApplicationInfo.FLAG_DEBUGGABLE) != 0

    // WifiManager.addNetwork is deprecated, and ignored for an ordinary app on
    // Android 10 and newer, but the Device Owner may still use it.
    @Suppress("DEPRECATION")
    override fun addWifi(ssid: String, password: String?) {
        val wifi = app.getSystemService(WifiManager::class.java)
        // Android takes what is between the first and the last quote as it is.
        val config = WifiConfiguration().apply {
            SSID = "\"" + ssid + "\""
            if (password.isNullOrEmpty()) {
                allowedKeyManagement.set(WifiConfiguration.KeyMgmt.NONE)
            } else {
                preSharedKey = "\"" + password + "\""
                allowedKeyManagement.set(WifiConfiguration.KeyMgmt.WPA_PSK)
            }
        }
        val id = wifi.addNetwork(config)
        if (id == -1) throw IllegalStateException("Android did not add the Wi-Fi network")
        wifi.enableNetwork(id, false)
    }

    override fun isDeviceOwner(): Boolean = dpm.isDeviceOwnerApp(app.packageName)

    // The "no factory reset" lock (Lockdown) is lifted for the wipe, so it
    // cannot stand in the way of the Device Owner's own wipe, and put back if
    // the wipe fails (ResetLockedWipe). A debug build, which never had it on,
    // is left without it.
    override fun wipe() = ResetLockedWipe.run(
        isLocked = { dpm.getUserRestrictions(admin).getBoolean(ResetLockedWipe.NO_FACTORY_RESET) },
        lift = { dpm.clearUserRestriction(admin, ResetLockedWipe.NO_FACTORY_RESET) },
        lock = { dpm.addUserRestriction(admin, ResetLockedWipe.NO_FACTORY_RESET) },
        wipe = {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
                dpm.wipeDevice(DevicePolicyManager.WIPE_RESET_PROTECTION_DATA)
            } else {
                @Suppress("DEPRECATION")
                dpm.wipeData(DevicePolicyManager.WIPE_RESET_PROTECTION_DATA)
            }
        },
        log = { text, e -> Log.w(TAG, text, e) },
    )

    override fun addUserRestriction(r: String) = dpm.addUserRestriction(admin, r)

    // Only NetworkFree lifts a lock, for "Liberar Wi-Fi e modo avião".
    fun clearUserRestriction(r: String) = dpm.clearUserRestriction(admin, r)

    override fun setUninstallBlocked(pkg: String, blocked: Boolean) = dpm.setUninstallBlocked(admin, pkg, blocked)

    override fun setUserControlDisabled(pkgs: List<String>) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            dpm.setUserControlDisabledPackages(admin, pkgs)
        }
    }

    override fun grantPermission(pkg: String, permission: String) {
        if (!dpm.setPermissionGrantState(admin, pkg, permission, DevicePolicyManager.PERMISSION_GRANT_STATE_GRANTED)) {
            throw SecurityException("Permission $permission was not granted to $pkg")
        }
    }

    override fun setLockTaskPackages(pkgs: List<String>) = dpm.setLockTaskPackages(admin, pkgs.toTypedArray())

    override fun setLockTaskFeatures(features: Int) = dpm.setLockTaskFeatures(admin, features)

    override fun setPermittedAccessibilityServices(pkgs: List<String>?) {
        if (!dpm.setPermittedAccessibilityServices(admin, pkgs)) {
            throw SecurityException("Accessibility services $pkgs were not permitted")
        }
    }

    override fun setPackagesSuspended(pkgs: List<String>, suspended: Boolean): List<String> =
        dpm.setPackagesSuspended(admin, pkgs.toTypedArray(), suspended).toList()

    override fun setApplicationRestrictions(pkg: String, restrictions: Map<String, Any>) {
        val bundle = Bundle()
        for ((key, value) in restrictions) {
            when (value) {
                is String -> bundle.putString(key, value)
                is Boolean -> bundle.putBoolean(key, value)
                is Int -> bundle.putInt(key, value)
                is Array<*> -> bundle.putStringArray(key, value.map { it as String }.toTypedArray())
                else -> throw IllegalArgumentException("Restriction $key of an unknown type")
            }
        }
        dpm.setApplicationRestrictions(admin, pkg, bundle)
    }

    override fun setApplicationHidden(pkg: String, hidden: Boolean): Boolean =
        dpm.setApplicationHidden(admin, pkg, hidden)

    override fun isApplicationHidden(pkg: String): Boolean = dpm.isApplicationHidden(admin, pkg)

    override fun privateDns(): PrivateDns =
        PrivateDns(dpm.getGlobalPrivateDnsMode(admin), dpm.getGlobalPrivateDnsHost(admin))

    // Android first connects to the host (DNS over TLS, port 853) from this
    // app's process and answers PRIVATE_DNS_SET_ERROR_HOST_NOT_SERVING,
    // changing nothing, if it cannot.
    override fun setPrivateDnsHost(host: String): Int = dpm.setGlobalPrivateDnsModeSpecifiedHost(admin, host)

    override fun setPrivateDnsAutomatic(): Int = dpm.setGlobalPrivateDnsModeOpportunistic(admin)

    // A Device Owner may write these two global settings (they are on
    // DevicePolicyManagerService's list for setGlobalSetting, Android 10 on;
    // their Settings names are hidden), as the system: the private DNS
    // restriction (Lockdown) does not stop it, and Android checks no host.
    override fun writePrivateDnsHost(host: String) {
        dpm.setGlobalSetting(admin, PRIVATE_DNS_SPECIFIER, host)
        dpm.setGlobalSetting(admin, PRIVATE_DNS_MODE, PRIVATE_DNS_MODE_HOSTNAME)
    }

    override fun alwaysOnVpn(): AlwaysOnVpn? {
        val pkg = dpm.getAlwaysOnVpnPackage(admin) ?: return null
        return AlwaysOnVpn(pkg, dpm.isAlwaysOnVpnLockdownEnabled(admin))
    }

    override fun setAlwaysOnVpn(vpn: AlwaysOnVpn?) {
        try {
            dpm.setAlwaysOnVpnPackage(admin, vpn?.pkg, vpn?.lockdown ?: false)
        } catch (e: PackageManager.NameNotFoundException) {
            throw IllegalStateException("The VPN app ${vpn?.pkg} is not installed", e)
        }
    }

    // Whether "Bloquear telas" can hold this phone: the Device Owner, with
    // lock task mode allowed for the app (Lockdown).
    fun canLockScreen(): Boolean = isDeviceOwner() && dpm.isLockTaskPermitted(app.packageName)

    companion object {
        private const val TAG = "Midview"

        // Settings.Global.PRIVATE_DNS_SPECIFIER and PRIVATE_DNS_MODE, and the
        // mode's value for a host (ConnectivityManager.PRIVATE_DNS_MODE_PROVIDER_HOSTNAME).
        private const val PRIVATE_DNS_SPECIFIER = "private_dns_specifier"
        private const val PRIVATE_DNS_MODE = "private_dns_mode"
        private const val PRIVATE_DNS_MODE_HOSTNAME = "hostname"

        // Applies the locks and never fails its caller: a refused lock is
        // logged, and the next start of the agent tries again. The Wi-Fi and
        // airplane mode stay free while their time runs (SchoolNetwork).
        fun lockDown(context: Context) {
            try {
                val network = SchoolNetwork.network(context)
                if (Lockdown(AndroidDevicePolicyGateway(context), network = network::unlessFree).apply()) {
                    Log.i(TAG, "School phone locks applied.")
                }
            } catch (e: RuntimeException) {
                Log.e(TAG, "A school phone lock was refused", e)
            }
        }
    }
}
