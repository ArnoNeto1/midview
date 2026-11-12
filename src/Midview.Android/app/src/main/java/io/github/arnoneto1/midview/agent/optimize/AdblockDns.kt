package io.github.arnoneto1.midview.agent.optimize

import android.content.Context
import android.net.ConnectivityManager
import android.net.DnsResolver
import android.net.Network
import android.net.NetworkCapabilities
import android.os.CancellationSignal
import android.os.SystemClock
import io.github.arnoneto1.midview.agent.policy.DevicePolicyGateway
import io.github.arnoneto1.midview.agent.protocol.PhoneDnsStates
import java.io.IOException
import java.net.HttpURLConnection
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket
import java.net.URL
import java.util.concurrent.Callable
import java.util.concurrent.CountDownLatch
import java.util.concurrent.ExecutionException
import java.util.concurrent.Executor
import java.util.concurrent.FutureTask
import java.util.concurrent.TimeUnit
import java.util.concurrent.TimeoutException
import javax.net.ssl.SSLSocket
import javax.net.ssl.SSLSocketFactory

// The phone's private DNS as Settings has it (DevicePolicyManager.
// getGlobalPrivateDnsMode and getGlobalPrivateDnsHost).
data class PrivateDns(val mode: Int, val host: String?) {
    fun isStrictOn(host: String): Boolean = mode == MODE_PROVIDER_HOSTNAME && this.host == host

    companion object {
        // DevicePolicyManager.PRIVATE_DNS_MODE_*.
        const val MODE_UNKNOWN = 0
        const val MODE_OFF = 1
        const val MODE_OPPORTUNISTIC = 2
        const val MODE_PROVIDER_HOSTNAME = 3

        // DevicePolicyManager.PRIVATE_DNS_SET_*.
        const val SET_NO_ERROR = 0
        const val SET_ERROR_HOST_NOT_SERVING = 1
        const val SET_ERROR_FAILURE_SETTING = 2
    }
}

// Where a step of turning the ad-free DNS on leads (AdblockDns.decide): the
// state for the teacher (PhoneDnsStates) and whether the DNS goes back to
// automatic first; or, nothing decided yet, the next step.
enum class DnsOutcome(val state: String?, val backToAutomatic: Boolean = false) {
    // The internet answers: Android is asked for the host next.
    SET_HOST(null),

    // Android took the host: the network has 20 s to validate it.
    AWAIT_VALIDATION(null),
    ON(PhoneDnsStates.ON),

    // Untouched: the teacher blocked the internet (tried when it is freed).
    INTERNET_BLOCKED(PhoneDnsStates.OFF_INTERNET_BLOCKED),

    // Untouched: no internet to check the DNS against.
    NO_INTERNET(PhoneDnsStates.OFF_NO_INTERNET),

    // Android did not take the host, since the network does not reach it
    // (DNS over TLS blocked): nothing was changed.
    HOST_REFUSED(PhoneDnsStates.OFF_NETWORK_BLOCKS),

    // The network did not validate it in 20 s: back to automatic.
    NOT_VALIDATED(PhoneDnsStates.OFF_NETWORK_BLOCKS, backToAutomatic = true),

    // Something failed: back to automatic, the side that keeps the internet.
    ERROR(PhoneDnsStates.OFF_ERROR, backToAutomatic = true),
}

// Whether the DNS host takes a TLS connection on port 853: at its first
// address (what Android's own check tries), and at any of its IPv4 addresses
// (with the host's name checked on its certificate).
data class HostCheck(val firstAddress: Boolean, val ipv4: Boolean)

// The phone's network as the ad-free DNS sees it (AndroidDnsNetwork).
interface DnsNetwork {
    // The agent's own HTTPS request to Android's check address answers 204
    // within 5 s.
    fun internetOk(): Boolean

    // Both checks of the host, together, bounded (10 s).
    fun checkHost(host: String): HostCheck

    // Waits up to timeoutMillis for the phone's network to be validated with
    // the private DNS on host in use; false if it never is.
    fun awaitPrivateDns(host: String, timeoutMillis: Long): Boolean
}

// The ad-free DNS for the whole phone ("Otimizar"): the Device Owner sets the
// private DNS to HOST (DNS over TLS, strict), only after the agent found the
// internet working, and keeps it only if the network then validates with it.
// Otherwise the DNS goes back to automatic (opportunistic, Android's own), so
// the phone is never left without internet because of it. While the teacher
// has the internet blocked (the agent's VPN, InternetBlock) nothing is
// changed: Optimizer tries again when it is freed. The internet block never
// touches the private DNS, so there is nothing to put back after it either.
// A call checks the network for up to ~30 s: never on the main thread.
class AdblockDns(
    private val gw: DevicePolicyGateway,
    private val network: DnsNetwork,
    private val internetBlocked: () -> Boolean,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    // Turns the DNS on, or checks again the one on; the state for the teacher.
    @Synchronized
    fun apply(): String {
        val outcome = try {
            steps()
        } catch (e: Exception) {
            log("The ad-free DNS could not be set", e)
            DnsOutcome.ERROR
        }
        if (outcome.backToAutomatic) backToAutomatic()
        val state = checkNotNull(outcome.state)
        log("Ad-free DNS: $state.", null)
        return state
    }

    // The phone has the ad-free DNS set (strict, on HOST).
    fun isSet(): Boolean = gw.privateDns().isStrictOn(HOST)

    // The automatic DNS again; never fails its caller.
    @Synchronized
    fun backToAutomatic() {
        try {
            val result = gw.setPrivateDnsAutomatic()
            if (result == PrivateDns.SET_NO_ERROR) {
                log("Private DNS back to automatic.", null)
            } else {
                log("Android did not put the automatic DNS back ($result)", null)
            }
        } catch (e: Exception) {
            log("The automatic DNS could not be put back", e)
        }
    }

    private fun steps(): DnsOutcome {
        if (internetBlocked()) return decide(internetOk = false, internetBlockedByTeacher = true, setResult = null, validated = null)
        val dnsOn = isSet()
        val outcome = decide(network.internetOk(), internetBlockedByTeacher = false, setResult = null, validated = null, dnsOn = dnsOn)
        if (outcome != DnsOutcome.SET_HOST) return outcome
        // Already on: checked again, not set again (setting it makes the
        // network check it from the start).
        val result = if (dnsOn) PrivateDns.SET_NO_ERROR else setHost()
        val taken = decide(true, false, result, validated = null)
        if (taken != DnsOutcome.AWAIT_VALIDATION) return taken
        return decide(true, false, result, network.awaitPrivateDns(HOST, VALIDATION_MILLIS))
    }

    // Android checks a host before taking it, at its first address only and
    // with no time limit on the connection (minutes on a network that drops
    // the packets, past what the teacher waits for); the same check, bounded,
    // comes first. With the first address answering, Android's own way. With
    // only the IPv4 addresses answering (a network whose route to the host's
    // IPv6 is broken: Android's check would refuse a host the phone can use,
    // since the private DNS uses all of its addresses), the two settings are
    // written directly. The validation decides either way. With no address
    // answering, nothing is written: the answer Android would give.
    private fun setHost(): Int {
        val check = network.checkHost(HOST)
        return when {
            check.firstAddress -> gw.setPrivateDnsHost(HOST).also { log("Private DNS host asked for: Android answered $it.", null) }
            check.ipv4 -> writeHost()
            else -> PrivateDns.SET_ERROR_HOST_NOT_SERVING.also { log("No address of the DNS host answers.", null) }
        }
    }

    // A refused write, or one with no effect, is a network that keeps the
    // DNS out (HOST_NOT_SERVING), never an error. A mode left strict without
    // our host would leave the phone without names: back to automatic.
    private fun writeHost(): Int {
        try {
            gw.writePrivateDnsHost(HOST)
        } catch (e: RuntimeException) {
            log("Android would not take the private DNS written directly", e)
        }
        val now = gw.privateDns()
        if (now.isStrictOn(HOST)) {
            log("Private DNS written directly: only the host's IPv4 addresses answer.", null)
            return PrivateDns.SET_NO_ERROR
        }
        if (now.mode == PrivateDns.MODE_PROVIDER_HOSTNAME) backToAutomatic()
        log("The private DNS written directly did not take.", null)
        return PrivateDns.SET_ERROR_HOST_NOT_SERVING
    }

    companion object {
        // The ad-blocking DNS server (DNS over TLS): only ever a technical address.
        const val HOST = "dns.adguard-dns.com"

        // How long the network has to validate the DNS once Android took it.
        const val VALIDATION_MILLIS = 20_000L

        // Each step's answer, in order: the internet blocked by the teacher,
        // the agent's internet check, what Android said to the host (null:
        // not asked yet), whether the network validated it (null: not yet).
        // dnsOn: ours was on already, so the internet check went through it;
        // failing, the DNS may be what the network keeps out.
        fun decide(
            internetOk: Boolean,
            internetBlockedByTeacher: Boolean,
            setResult: Int?,
            validated: Boolean?,
            dnsOn: Boolean = false,
        ): DnsOutcome =
            when {
                internetBlockedByTeacher -> DnsOutcome.INTERNET_BLOCKED
                !internetOk -> if (dnsOn) DnsOutcome.NOT_VALIDATED else DnsOutcome.NO_INTERNET
                setResult == null -> DnsOutcome.SET_HOST
                setResult == PrivateDns.SET_ERROR_HOST_NOT_SERVING -> DnsOutcome.HOST_REFUSED
                setResult != PrivateDns.SET_NO_ERROR -> DnsOutcome.ERROR
                validated == null -> DnsOutcome.AWAIT_VALIDATION
                validated -> DnsOutcome.ON
                else -> DnsOutcome.NOT_VALIDATED
            }
    }
}

// DnsNetwork on the phone. The agent's own connections go around the
// internet block's VPN, but the DNS is never tried while it is on.
class AndroidDnsNetwork(context: Context) : DnsNetwork {
    private val cm = context.applicationContext.getSystemService(ConnectivityManager::class.java)

    // Bounded as a whole: a name that does not resolve can take longer than
    // the connection's own timeouts.
    override fun internetOk(): Boolean = started { answers204() }.by(SystemClock.elapsedRealtime() + CHECK_MILLIS)

    override fun checkHost(host: String): HostCheck {
        val deadline = SystemClock.elapsedRealtime() + HOST_CHECK_MILLIS
        val first = started { firstAddressAnswers(host) }
        val ipv4 = started { ipv4Answers(host, deadline) }
        return HostCheck(firstAddress = first.by(deadline), ipv4 = ipv4.by(deadline))
    }

    // As DevicePolicyManager's own check (PrivateDnsConnectivityChecker): the
    // host's first address, as this process resolves it, and a TLS handshake
    // on port 853 with the system's certificates. Android's connects with no
    // time limit; this one gives up after a few seconds.
    private fun firstAddressAnswers(host: String): Boolean =
        try {
            (SSLSocketFactory.getDefault().createSocket() as SSLSocket).use { socket ->
                socket.soTimeout = HOST_HANDSHAKE_MILLIS
                socket.connect(InetSocketAddress(InetAddress.getByName(host), DOT_PORT), HOST_CONNECT_MILLIS)
                socket.startHandshake()
                true
            }
        } catch (_: IOException) {
            false
        }

    // Any of the host's IPv4 addresses, all tried at once, each with the
    // host's name checked on the certificate it shows.
    private fun ipv4Answers(host: String, deadline: Long): Boolean {
        val addresses = try {
            InetAddress.getAllByName(host).filterIsInstance<Inet4Address>()
        } catch (_: IOException) {
            return false
        }
        return addresses.map { address -> started { answersAs(address, host) } }.any { it.by(deadline) }
    }

    private fun answersAs(address: Inet4Address, host: String): Boolean =
        try {
            Socket().use { plain ->
                plain.connect(InetSocketAddress(address, DOT_PORT), HOST_CONNECT_MILLIS)
                plain.soTimeout = HOST_HANDSHAKE_MILLIS
                ((SSLSocketFactory.getDefault() as SSLSocketFactory).createSocket(plain, host, DOT_PORT, true) as SSLSocket).use { socket ->
                    socket.sslParameters = socket.sslParameters.apply { endpointIdentificationAlgorithm = "HTTPS" }
                    socket.startHandshake()
                    true
                }
            }
        } catch (_: IOException) {
            false
        }

    // A check on a thread of its own; its answer by the deadline, false if
    // late or failed.
    private fun started(check: () -> Boolean): FutureTask<Boolean> =
        FutureTask(Callable { check() }).also { Thread(it, "Midview network check").apply { isDaemon = true }.start() }

    private fun FutureTask<Boolean>.by(deadline: Long): Boolean =
        try {
            get((deadline - SystemClock.elapsedRealtime()).coerceAtLeast(0), TimeUnit.MILLISECONDS)
        } catch (_: TimeoutException) {
            cancel(true)
            false
        } catch (_: ExecutionException) {
            false
        }

    private fun answers204(): Boolean {
        val connection = URL(CHECK_URL).openConnection() as HttpURLConnection
        return try {
            connection.connectTimeout = CHECK_MILLIS.toInt()
            connection.readTimeout = CHECK_MILLIS.toInt()
            connection.instanceFollowRedirects = false
            connection.useCaches = false
            connection.responseCode == HttpURLConnection.HTTP_NO_CONTENT
        } catch (_: IOException) {
            false
        } finally {
            connection.disconnect()
        }
    }

    // The network validated by Android with the private DNS on host in use,
    // and a name looked up through it now, not from the cache: right after
    // the DNS is set, Android still shows the network validated from before,
    // until its own check of the new DNS ends.
    override fun awaitPrivateDns(host: String, timeoutMillis: Long): Boolean {
        val deadline = SystemClock.elapsedRealtime() + timeoutMillis
        while (true) {
            val network = cm.activeNetwork
            if (network != null && usesPrivateDns(network, host) && looksUp(network, deadline)) return true
            val left = deadline - SystemClock.elapsedRealtime()
            if (left <= 0) return false
            Thread.sleep(minOf(POLL_MILLIS, left))
        }
    }

    private fun usesPrivateDns(network: Network, host: String): Boolean {
        val capabilities = cm.getNetworkCapabilities(network) ?: return false
        val link = cm.getLinkProperties(network) ?: return false
        return capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED) &&
            link.isPrivateDnsActive && link.privateDnsServerName == host
    }

    private fun looksUp(network: Network, deadline: Long): Boolean {
        val wait = minOf(LOOKUP_MILLIS, deadline - SystemClock.elapsedRealtime())
        if (wait <= 0) return false
        val done = CountDownLatch(1)
        var found = false
        val cancel = CancellationSignal()
        DnsResolver.getInstance().query(
            network, CHECK_HOST, DnsResolver.FLAG_NO_CACHE_LOOKUP, Executor { it.run() }, cancel,
            object : DnsResolver.Callback<List<InetAddress>> {
                override fun onAnswer(answer: List<InetAddress>, rcode: Int) {
                    found = rcode == RCODE_NO_ERROR && answer.isNotEmpty()
                    done.countDown()
                }

                override fun onError(error: DnsResolver.DnsException) {
                    done.countDown()
                }
            },
        )
        if (!done.await(wait, TimeUnit.MILLISECONDS)) {
            cancel.cancel()
            return false
        }
        return found
    }

    private companion object {
        const val CHECK_URL = "https://connectivitycheck.gstatic.com/generate_204"
        const val CHECK_HOST = "connectivitycheck.gstatic.com"
        const val CHECK_MILLIS = 5_000L
        const val DOT_PORT = 853
        const val HOST_CONNECT_MILLIS = 5_000
        const val HOST_HANDSHAKE_MILLIS = 5_000
        const val HOST_CHECK_MILLIS = 10_000L
        const val LOOKUP_MILLIS = 5_000L
        const val POLL_MILLIS = 1_000L
        const val RCODE_NO_ERROR = 0
    }
}
