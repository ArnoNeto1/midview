package io.github.arnoneto1.midview.agent.sites

import android.content.Context
import android.content.Intent
import android.net.VpnService
import android.os.ParcelFileDescriptor
import android.util.Log
import java.io.IOException

// The agent's VPN while the internet is blocked (InternetBlock): every
// address of every app goes into it and nothing comes out, with no DNS
// server, so names do not even resolve. IPv6 is blocked too, since only IPv4
// is configured. The agent itself is left out, so it keeps talking to the
// teacher over the school's network. Android starts it as the phone's
// always-on VPN (after a restart too); the agent starts it again whenever it
// starts. Nothing is read from it: what apps send is dropped by the system.
class BlockVpnService : VpnService() {
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            close()
            stopSelf()
            return START_NOT_STICKY
        }
        establish()
        return START_STICKY
    }

    // Android took the VPN away (another always-on VPN, or the internet freed).
    override fun onRevoke() {
        close()
        stopSelf()
    }

    override fun onDestroy() {
        close()
        super.onDestroy()
    }

    private fun establish() {
        synchronized(lock) {
            if (tunnel != null) return
            if (prepare(this) != null) {
                // Not this phone's always-on VPN (the internet was freed).
                Log.w(TAG, "The block VPN is not allowed now; not started.")
                stopSelf()
                return
            }
            tunnel = try {
                Builder()
                    .setSession("Midview: internet bloqueada")
                    .addAddress(ADDRESS, 32)
                    .addRoute("0.0.0.0", 0)
                    .addDisallowedApplication(packageName)
                    .establish()
            } catch (e: Exception) {
                Log.e(TAG, "The block VPN could not start", e)
                null
            }
            if (tunnel != null) Log.i(TAG, "Block VPN up: nothing goes out but the agent.")
        }
    }

    private fun close() {
        synchronized(lock) {
            try {
                tunnel?.close()
            } catch (e: IOException) {
                Log.w(TAG, "The block VPN did not close cleanly", e)
            }
            tunnel = null
        }
    }

    companion object {
        private const val TAG = "Midview"
        private const val ACTION_STOP = "io.github.arnoneto1.midview.agent.STOP_BLOCK_VPN"

        // An address of the VPN's own: any private one does, since every
        // other app's traffic goes into the VPN anyway.
        private const val ADDRESS = "10.233.233.233"

        private val lock = Any()
        @Volatile private var tunnel: ParcelFileDescriptor? = null

        fun start(context: Context) {
            context.startService(Intent(context, BlockVpnService::class.java))
        }

        fun stop(context: Context) {
            if (tunnel != null) {
                context.startService(Intent(context, BlockVpnService::class.java).setAction(ACTION_STOP))
            }
        }
    }
}
