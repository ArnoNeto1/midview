package io.github.arnoneto1.midview.agent.policy

import android.content.Context
import android.os.Build
import android.util.Log
import java.io.File

// The process's one NetworkFree and AdminUnlock, on the app's storage and
// the Device Owner's DevicePolicyManager: the teacher's commands, the
// "Ajustes da escola" screen, Lockdown at each start and the watchdog all
// share them (and so their lock).
object SchoolNetwork {
    private const val TAG = "Midview"
    private const val NETWORK_FREE_FILE = "network-free.txt"
    private const val ADMIN_PASSWORD_FILE = "admin-password.bin"
    private const val ADMIN_ATTEMPTS_FILE = "admin-attempts.txt"

    @Volatile private var network: NetworkFree? = null
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
                )
            }.also { network = it }
        }

    fun admin(context: Context): AdminUnlock =
        admin ?: synchronized(this) {
            admin ?: run {
                val app = context.applicationContext
                AdminUnlock(
                    File(app.filesDir, ADMIN_PASSWORD_FILE), File(app.filesDir, ADMIN_ATTEMPTS_FILE),
                    free = { network(app).free(it) },
                    log = log,
                )
            }.also { admin = it }
        }

    // The watchdog's check: the Wi-Fi locked again once its free time is up.
    // Never fails its caller.
    fun check(context: Context) {
        try {
            network(context).check()
        } catch (e: RuntimeException) {
            Log.w(TAG, "Couldn't check the Wi-Fi's free time", e)
        }
    }
}
