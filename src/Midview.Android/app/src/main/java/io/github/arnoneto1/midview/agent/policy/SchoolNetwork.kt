package io.github.arnoneto1.midview.agent.policy

import android.content.Context
import android.os.Build
import android.util.Log
import io.github.arnoneto1.midview.agent.leave.PhoneLeaving
import io.github.arnoneto1.midview.agent.provisioning.CableSetup
import java.io.File

// The process's one NetworkFree, AccountsFree, DebuggingFree and
// AdminUnlock, on the app's storage and the Device Owner's
// DevicePolicyManager: the teacher's commands, the "Ajustes da escola"
// screen, Lockdown at each start and the watchdog all share them (and so
// their lock).
object SchoolNetwork {
    private const val TAG = "Midview"
    private const val NETWORK_FREE_FILE = "network-free.txt"
    private const val ACCOUNTS_FREE_FILE = "accounts-free.txt"
    private const val DEBUGGING_FREE_FILE = "debugging-free.txt"
    private const val ADMIN_PASSWORD_FILE = "admin-password.bin"
    private const val ADMIN_ATTEMPTS_FILE = "admin-attempts.txt"

    @Volatile private var network: NetworkFree? = null
    @Volatile private var accounts: AccountsFree? = null
    @Volatile private var debugging: DebuggingFree? = null
    @Volatile private var wifi: SchoolWifi? = null
    @Volatile private var admin: AdminUnlock? = null

    private val log: (String, Throwable?) -> Unit = { text, e -> if (e == null) Log.i(TAG, text) else Log.w(TAG, text, e) }

    fun network(context: Context): NetworkFree =
        network ?: synchronized(this) {
            network ?: run {
                val app = context.applicationContext
                val gw = AndroidDevicePolicyGateway(app)
                NetworkFree(
                    File(app.filesDir, NETWORK_FREE_FILE), Lockdown.networkRestrictions(Build.VERSION.SDK_INT),
                    isDeviceOwner = gw::isDeviceOwner,
                    addRestriction = gw::addUserRestriction,
                    clearRestriction = gw::clearUserRestriction,
                    log = log,
                    keepWifiOn = { wifi(app).keepOn() },
                    rejoinWifi = { wifi(app).rejoin() },
                )
            }.also { network = it }
        }

    // "Contas do aparelho": the device's accounts, locked unless freed.
    fun accounts(context: Context): AccountsFree =
        accounts ?: synchronized(this) {
            accounts ?: run {
                val app = context.applicationContext
                val gw = AndroidDevicePolicyGateway(app)
                AccountsFree(
                    File(app.filesDir, ACCOUNTS_FREE_FILE), Lockdown.ACCOUNT_RESTRICTIONS,
                    isDeviceOwner = gw::isDeviceOwner,
                    addRestriction = gw::addUserRestriction,
                    clearRestriction = gw::clearUserRestriction,
                    log = log,
                )
            }.also { accounts = it }
        }

    // "Depuração USB": USB debugging, locked unless freed (and on while freed).
    fun debugging(context: Context): DebuggingFree =
        debugging ?: synchronized(this) {
            debugging ?: run {
                val app = context.applicationContext
                val gw = AndroidDevicePolicyGateway(app)
                DebuggingFree(
                    File(app.filesDir, DEBUGGING_FREE_FILE), Lockdown.debuggingRestrictions(gw.debuggable),
                    isDeviceOwner = gw::isDeviceOwner,
                    addRestriction = gw::addUserRestriction,
                    clearRestriction = gw::clearUserRestriction,
                    usbDebugging = gw::setUsbDebugging,
                    keepsAdb = gw.debuggable,
                    log = log,
                )
            }.also { debugging = it }
        }

    // The school's Wi-Fi: the one the cable setup added.
    fun wifi(context: Context): SchoolWifi =
        wifi ?: synchronized(this) {
            wifi ?: run {
                val app = context.applicationContext
                SchoolWifi(AndroidWifiControl(app), { CableSetup(File(app.filesDir, CableSetup.FILE_NAME)).schoolWifi() }, log)
            }.also { wifi = it }
        }

    fun admin(context: Context): AdminUnlock =
        admin ?: synchronized(this) {
            admin ?: run {
                val app = context.applicationContext
                AdminUnlock(
                    File(app.filesDir, ADMIN_PASSWORD_FILE), File(app.filesDir, ADMIN_ATTEMPTS_FILE),
                    // The same timed locks as the teacher's commands.
                    freeLocks = { what, minutes ->
                        when (what) {
                            AdminUnlock.Freeable.NETWORK -> network(app).free(minutes)
                            AdminUnlock.Freeable.DEBUGGING -> debugging(app).free(minutes)
                            AdminUnlock.Freeable.ACCOUNTS -> accounts(app).free(minutes)
                        }
                    },
                    log = log,
                )
            }.also { admin = it }
        }

    // The watchdog's check: the Wi-Fi, the accounts and USB debugging locked
    // again once their free time is up. Never fails its caller.
    fun check(context: Context) {
        if (PhoneLeaving.isActive(context)) return
        try {
            network(context).check()
        } catch (e: RuntimeException) {
            Log.w(TAG, "Couldn't check the Wi-Fi's free time", e)
        }
        try {
            accounts(context).check()
        } catch (e: RuntimeException) {
            Log.w(TAG, "Couldn't check the accounts' free time", e)
        }
        try {
            debugging(context).check()
        } catch (e: RuntimeException) {
            Log.w(TAG, "Couldn't check USB debugging's free time", e)
        }
    }
}
