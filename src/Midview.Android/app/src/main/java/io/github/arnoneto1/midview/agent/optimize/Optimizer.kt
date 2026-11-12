package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.policy.DevicePolicyGateway
import io.github.arnoneto1.midview.agent.protocol.PhoneDnsStates
import io.github.arnoneto1.midview.agent.protocol.PhoneOptimizeReport
import java.time.Duration
import java.time.Instant
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong

// What the commands port asks of "Otimizar" (Optimizer).
interface PhoneOptimizer {
    // "Otimizar" (OptimizePhone): runs now, answered with the report.
    fun run(): PhoneOptimizeReport

    // A trusted teacher sent a command: the first optimization, or the DNS
    // tried again, in the background when due.
    fun teacherSeen()
}

// What the optimizer needs to know about the phone's apps (InstalledApps).
interface BloatCatalog {
    // On the phone for this user, hidden or not (an app removed for the
    // user is not).
    fun isPresent(pkg: String): Boolean

    // Never hidden on this phone (BloatList.neverHide).
    fun neverHide(): Set<String>
}

// "Otimizar" on the school phone: hides the ad and bloat apps it has
// (BloatList; hidden apps are gone from the home screen and do not run, and
// stay so after a restart) and turns the ad-free DNS on (AdblockDns), keeping
// the report for the teacher (OptimizeStore). It runs by itself at the first
// command of a trusted teacher (after pairing) and again whenever the teacher
// asks, changing nothing the second time. A DNS left off for no internet or a
// network that blocks it is tried again at a trusted teacher's command, at
// most once every 30 minutes; one left off while the teacher had the internet
// blocked, as soon as it is freed. One run at a time: the DnsWatcher's
// fallback waits for a run, so the two never undo each other; and a
// "Otimizar" that waited for a DNS try reuses it, so the teacher waits for
// one DNS step at most (up to ~36 s), never two.
class Optimizer(
    private val catalog: BloatCatalog,
    private val gw: DevicePolicyGateway,
    private val dns: AdblockDns,
    private val store: OptimizeStore,
    private val internetBlocked: () -> Boolean,
    private val clock: () -> Instant = Instant::now,
    private val background: (Runnable) -> Unit = { Thread(it, "Midview optimize").apply { isDaemon = true }.start() },
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) : PhoneOptimizer {
    private val lock = Any()
    private val busy = AtomicBoolean(false)
    private val runs = AtomicLong()
    private val dnsSteps = AtomicLong()

    override fun run(): PhoneOptimizeReport {
        val askedRuns = runs.get()
        val askedDns = dnsSteps.get()
        synchronized(lock) {
            // One ended while this waited (the first one, in the background):
            // its report is as new as another run's would be.
            if (runs.get() != askedRuns) return store.current.report
            // A DNS try ended while this waited: the apps are seen to again,
            // the DNS's answer is reused.
            return optimize(reuseDns = dnsSteps.get() != askedDns).also { runs.incrementAndGet() }
        }
    }

    override fun teacherSeen() {
        try {
            val held = store.current
            val dnsState = held.report.dns
            when {
                held.report.optimizedAt == null -> inBackground { run() }
                dnsState in RETRIED && OptimizeStore.retryDue(clock(), held.lastDnsAttempt) -> inBackground { retryDns() }
                // Freed while the agent was not running to hear it.
                dnsState == PhoneDnsStates.OFF_INTERNET_BLOCKED && !internetBlocked() -> inBackground { retryDns() }
            }
        } catch (e: Exception) {
            log("The optimization could not be checked", e)
        }
    }

    // The teacher freed the internet (InternetBlock): the DNS left off for it.
    fun internetFreed() {
        if (store.current.report.dns == PhoneDnsStates.OFF_INTERNET_BLOCKED) inBackground { retryDns() }
    }

    // DnsWatcher: with the ad-free DNS on and the network unvalidated for
    // 30 s, the DNS goes back to automatic and the teacher reads that the
    // network blocks it. unvalidatedFor is read once any run has ended (null:
    // validated, or no network at all). True when it went back.
    fun fallBack(unvalidatedFor: () -> Duration?): Boolean = synchronized(lock) {
        try {
            val since = unvalidatedFor() ?: return false
            if (!DnsWatcher.shouldFallBack(dns.isSet(), since)) return false
            dns.backToAutomatic()
            val held = store.current
            store.save(held.copy(report = held.report.copy(dns = PhoneDnsStates.OFF_NETWORK_BLOCKS), lastDnsAttempt = clock()))
            log("The network went ${since.seconds} s without validating the ad-free DNS: back to automatic.", null)
            true
        } catch (e: Exception) {
            log("The DNS fallback could not be checked", e)
            false
        }
    }

    // Only the DNS again: the apps and the time of the optimization stay.
    private fun retryDns() {
        synchronized(lock) {
            val held = store.current
            val state = dnsStep()
            store.save(held.copy(report = held.report.copy(dns = state), lastDnsAttempt = clock()))
        }
    }

    // The DNS tried (AdblockDns), counted for a "Otimizar" waiting behind it.
    private fun dnsStep(): String = dns.apply().also { dnsSteps.incrementAndGet() }

    private fun optimize(reuseDns: Boolean): PhoneOptimizeReport {
        val now = clock()
        val held = store.current
        val next = if (gw.isDeviceOwner()) {
            val (hidden, failed) = hideApps()
            val (dnsState, attempt) = if (reuseDns) held.report.dns to held.lastDnsAttempt else dnsStep() to clock()
            OptimizeState(PhoneOptimizeReport(now, hidden, failed, dnsState, AdblockDns.HOST), lastDnsAttempt = attempt)
        } else {
            // Not the school's phone: nothing can be hidden nor set.
            val failed = runCatching { toHide() }.getOrDefault(emptyList())
            OptimizeState(PhoneOptimizeReport(now, emptyList(), failed, PhoneDnsStates.OFF_ERROR, AdblockDns.HOST), held.lastDnsAttempt)
        }
        store.save(next)
        log("Optimized: ${next.report.hidden.size} app(s) hidden ${next.report.hidden}, refused ${next.report.failed}, DNS ${next.report.dns}.", null)
        return next.report
    }

    // The listed apps the phone has, hidden (or already hidden), and those
    // Android would not hide. Android answers false for an app already
    // hidden, so that is asked first.
    private fun hideApps(): Pair<List<String>, List<String>> {
        val target = try {
            toHide()
        } catch (e: Exception) {
            log("The phone's apps could not be read", e)
            return emptyList<String>() to emptyList()
        }
        val hidden = mutableListOf<String>()
        val failed = mutableListOf<String>()
        for (pkg in target) {
            val done = try {
                gw.isApplicationHidden(pkg) || gw.setApplicationHidden(pkg, true)
            } catch (e: RuntimeException) {
                log("Android would not hide $pkg", e)
                false
            }
            if (done) hidden += pkg else failed += pkg
        }
        return hidden to failed
    }

    private fun toHide(): List<String> =
        BloatList.toHide(BloatList.PACKAGES.filter(catalog::isPresent).toSet(), catalog.neverHide()).toList()

    // At most one run waiting or running in the background.
    private fun inBackground(work: () -> Unit) {
        if (!busy.compareAndSet(false, true)) return
        try {
            background(
                Runnable {
                    try {
                        work()
                    } catch (e: Exception) {
                        log("The optimization failed", e)
                    } finally {
                        busy.set(false)
                    }
                },
            )
        } catch (e: Exception) {
            busy.set(false)
            log("The optimization could not be started", e)
        }
    }

    private companion object {
        // The DNS states tried again at a teacher's command (every 30 min).
        val RETRIED = setOf(PhoneDnsStates.OFF_NO_INTERNET, PhoneDnsStates.OFF_NETWORK_BLOCKS)
    }
}
