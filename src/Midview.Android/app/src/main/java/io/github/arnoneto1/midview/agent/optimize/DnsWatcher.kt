package io.github.arnoneto1.midview.agent.optimize

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.os.SystemClock
import java.time.Duration
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.TimeUnit

// How long the phone's default network has gone without Android's
// validation (NetworkCapabilities.NET_CAPABILITY_VALIDATED), from its
// network callbacks. Times in milliseconds of one clock
// (SystemClock.elapsedRealtime on the phone).
class ValidationClock {
    private var network: Any? = null
    private var unvalidatedSince: Long? = null

    // The default network and whether it is validated now (another network
    // taking over starts the count again).
    @Synchronized
    fun changed(network: Any, validated: Boolean, now: Long) {
        val since = if (network == this.network) unvalidatedSince else null
        this.network = network
        unvalidatedSince = if (validated) null else since ?: now
    }

    // No default network any more. Android names a new one before letting
    // the old one go, so an old one lost changes nothing.
    @Synchronized
    fun lost(network: Any) {
        if (network == this.network) {
            this.network = null
            unvalidatedSince = null
        }
    }

    // Null while validated, or with no network at all: never a reason to
    // fall back, since without a network there is no internet to bring back.
    @Synchronized
    fun unvalidatedFor(now: Long): Duration? = unvalidatedSince?.let { Duration.ofMillis(now - it) }
}

// Watches the phone's network once the ad-free DNS is on: a default network
// Android cannot validate for 30 s (the phone moved to a Wi-Fi that blocks
// DNS over TLS, say) puts the DNS back to automatic (Optimizer.fallBack), so
// the internet comes back by itself and the teacher reads that the network
// blocks the DNS. The callbacks only keep the clock (on Android's own
// network thread); the check runs on a thread of its own, since it may wait
// for an optimization to end.
class DnsWatcher(
    context: Context,
    private val optimizer: Optimizer,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    private val cm = context.applicationContext.getSystemService(ConnectivityManager::class.java)
    private val clock = ValidationClock()
    private val checks = Executors.newSingleThreadScheduledExecutor { Thread(it, "Midview DNS watch").apply { isDaemon = true } }
    private var pending: ScheduledFuture<*>? = null

    // The wait before the next look while the network stays unvalidated.
    private var wait: Duration = FALL_BACK_AFTER

    private val callback = object : ConnectivityManager.NetworkCallback() {
        override fun onCapabilitiesChanged(network: Network, capabilities: NetworkCapabilities) {
            clock.changed(network, capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED), now())
            networkChanged()
        }

        override fun onLost(network: Network) {
            clock.lost(network)
            networkChanged()
        }
    }

    fun start() {
        cm.registerDefaultNetworkCallback(callback)
    }

    fun stop() {
        runCatching { cm.unregisterNetworkCallback(callback) }
        checks.shutdownNow()
    }

    // A check when the network will have gone 30 s unvalidated; none while
    // it is validated or gone. One already set stays: the capabilities
    // change often (the signal's strength), and never bring it closer.
    @Synchronized
    private fun networkChanged() {
        val unvalidatedFor = clock.unvalidatedFor(now())
        val set = pending
        if (unvalidatedFor == null) {
            set?.cancel(false)
            pending = null
            wait = FALL_BACK_AFTER
        } else if (set == null || set.isDone) {
            checkIn(FALL_BACK_AFTER - unvalidatedFor)
        }
    }

    @Synchronized
    private fun checkIn(wait: Duration) {
        pending = try {
            checks.schedule(::check, wait.toMillis().coerceAtLeast(0), TimeUnit.MILLISECONDS)
        } catch (_: Exception) {
            null
        }
    }

    // Still unvalidated afterwards: looked at again 30 s later while the
    // ad-free DNS is on (or turned on since); with it off there is nothing
    // to fall back from, so the looks space out (nextWait), and start over
    // when the network changes.
    private fun check() {
        try {
            optimizer.fallBack(::unvalidatedFor)
        } catch (e: Exception) {
            log("The network check failed", e)
        }
        val ours = optimizer.ourDnsSet()
        synchronized(this) {
            if (clock.unvalidatedFor(now()) != null) {
                wait = nextWait(ours, wait)
                checkIn(wait)
            } else {
                pending = null
                wait = FALL_BACK_AFTER
            }
        }
    }

    // As the callbacks saw it, unless Android says the network is validated
    // now (a callback may come late).
    private fun unvalidatedFor(): Duration? {
        val network = cm.activeNetwork ?: return null
        if (cm.getNetworkCapabilities(network)?.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED) == true) return null
        return clock.unvalidatedFor(now())
    }

    private fun now(): Long = SystemClock.elapsedRealtime()

    companion object {
        val FALL_BACK_AFTER: Duration = Duration.ofSeconds(30)

        // The longest wait between looks with the ad-free DNS off.
        val MAX_WAIT: Duration = Duration.ofMinutes(10)

        // 30 s with our DNS on; doubling up to MAX_WAIT with it off.
        fun nextWait(ourDnsOn: Boolean, last: Duration): Duration =
            if (ourDnsOn) FALL_BACK_AFTER else minOf(last.multipliedBy(2), MAX_WAIT)

        fun shouldFallBack(strictOnOurHost: Boolean, unvalidatedFor: Duration): Boolean =
            strictOnOurHost && unvalidatedFor >= FALL_BACK_AFTER
    }
}
