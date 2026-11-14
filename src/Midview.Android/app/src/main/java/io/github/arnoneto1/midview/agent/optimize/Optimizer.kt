package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.policy.DevicePolicyGateway
import io.github.arnoneto1.midview.agent.protocol.PhoneDnsStates
import io.github.arnoneto1.midview.agent.protocol.PhoneOptimizeReport
import java.time.Duration
import java.time.Instant
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.withLock

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

    // A system app or an update of one, which a Device Owner cannot
    // uninstall (only hide); true when it cannot be told.
    fun cameWithPhone(pkg: String): Boolean
}

// The Device Owner's silent uninstall (SilentUninstaller), for UnusedApps.
fun interface AppRemover {
    // Waits at most waitMillis for Android: null once uninstalled, NO_ANSWER
    // when Android said nothing in time, otherwise why not.
    fun uninstall(pkg: String, waitMillis: Long): String?

    companion object {
        const val NO_ANSWER = "o Android não respondeu sobre a desinstalação"
    }
}

// "Otimizar" on the school phone: hides the ad and bloat apps it has
// (BloatList; hidden apps are gone from the home screen and do not run, and
// stay so after a restart), turns the ad-free DNS on (AdblockDns) and
// uninstalls the apps the school does not use (UnusedApps: those of the
// system, which a Device Owner cannot uninstall, are hidden instead, and so
// is one Android refuses to uninstall), keeping the report for the teacher
// (OptimizeStore), which counts every app it ever uninstalled that is still
// gone. Uninstalling is slow (a second or more each), so the teacher waits
// for it at most until UNINSTALL_WITHIN after asking (the Master waits 90 s):
// the apps left then are uninstalled right after, in the background, one at
// a time (each waits at most APP_WAIT for Android, so a teacher's
// "Otimizar" never waits long behind it), and the next report counts them.
// Meanwhile the report says how many are still to go (pending), so the
// teacher reads that they are still leaving the phone.
// A failure on one app never stops the others. It runs by itself at the first
// command of a trusted teacher (after pairing) and again whenever the teacher
// asks, changing nothing the second time. A DNS left off for no internet or a
// network that blocks it is tried again at a trusted teacher's command, at
// most once every 30 minutes; one left off while the teacher had the internet
// blocked, as soon as it is freed. One run at a time: the DnsWatcher's
// fallback waits for a run, so the two never undo each other; and a
// "Otimizar" that waited for a DNS try reuses it, so the teacher waits for
// one DNS step at most (up to ~36 s), never two.
//
// Whether the ad-free DNS is wanted at all is the lab panel's "Bloqueador de
// anúncios" (LabConfigStore, on unless the teacher turned it off there): its
// one source of truth. Off, every DNS step puts the automatic DNS back (if
// ours is set) and reports offByTeacher, "Otimizar" included, which then
// only hides apps; turned on again, the DNS is tried at once (adblockChanged).
class Optimizer(
    private val catalog: BloatCatalog,
    private val gw: DevicePolicyGateway,
    private val dns: AdblockDns,
    private val store: OptimizeStore,
    private val internetBlocked: () -> Boolean,
    private val adblockWanted: () -> Boolean = { true },
    private val clock: () -> Instant = Instant::now,
    private val background: (Runnable) -> Unit = { Thread(it, "Midview optimize").apply { isDaemon = true }.start() },
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
    private val remover: AppRemover = AppRemover { _, _ -> "não há como desinstalar" },
    private val ticker: () -> Long = System::nanoTime,
    // The cable's "Midview otimizado" (CableSetup.optimizes): with "Só o
    // Midview" the first trusted command does not optimize; the teacher's
    // "Otimizar" (run) still does.
    private val tidiesByItself: () -> Boolean = { true },
) : PhoneOptimizer {
    private val lock = ReentrantLock()
    private val busy = AtomicBoolean(false)
    private val removing = AtomicBoolean(false)
    private val runs = AtomicLong()
    private val dnsSteps = AtomicLong()

    override fun run(): PhoneOptimizeReport {
        val asked = ticker()
        val askedRuns = runs.get()
        val askedDns = dnsSteps.get()
        lock.withLock {
            // One ended while this waited (the first one, in the background):
            // its report is as new as another run's would be.
            if (runs.get() != askedRuns) return store.current.report
            // A DNS try ended while this waited: the apps are seen to again,
            // the DNS's answer is reused.
            return optimize(reuseDns = dnsSteps.get() != askedDns, asked).also { runs.incrementAndGet() }
        }
    }

    override fun teacherSeen() {
        try {
            val held = store.current
            val dnsState = held.report.dns
            when {
                held.report.optimizedAt == null -> if (tidiesByItself()) inBackground { run() }
                dnsState in RETRIED && OptimizeStore.retryDue(clock(), held.lastDnsAttempt) -> inBackground { retryDns() }
                // Freed while the agent was not running to hear it.
                dnsState == PhoneDnsStates.OFF_INTERNET_BLOCKED && !internetBlocked() -> inBackground { retryDns() }
                // Turned on in the panel while a run was busy.
                dnsState == PhoneDnsStates.OFF_BY_TEACHER && adblockWanted() -> inBackground { retryDns() }
            }
        } catch (e: Exception) {
            log("The optimization could not be checked", e)
        }
    }

    // The teacher freed the internet (InternetBlock): the DNS left off for it.
    fun internetFreed() {
        if (store.current.report.dns == PhoneDnsStates.OFF_INTERNET_BLOCKED) inBackground { retryDns() }
    }

    // The lab panel's "Bloqueador de anúncios" changed (LabConfigStore). On:
    // the DNS tried in the background, unless it is on already. Off: the
    // automatic DNS back at once, and the report says the teacher turned it
    // off; while a run is under way (up to ~36 s) that waits for it in the
    // background, so the teacher's panel is answered without waiting.
    fun adblockChanged() {
        if (adblockWanted()) {
            val on = try {
                store.current.report.dns == PhoneDnsStates.ON && dns.isSet()
            } catch (e: Exception) {
                false
            }
            if (!on) inBackground { retryDns() }
            return
        }
        if (lock.tryLock()) {
            try {
                adblockOff()
            } finally {
                lock.unlock()
            }
            return
        }
        try {
            background(Runnable { lock.withLock { adblockOff() } })
        } catch (e: Exception) {
            log("The ad-free DNS could not be turned off", e)
        }
    }

    private fun adblockOff() {
        // Turned on again meanwhile: nothing to turn off.
        if (adblockWanted()) return
        try {
            val held = store.current
            val state = dnsStep()
            if (held.report.dns != state) store.save(held.copy(report = held.report.copy(dns = state)))
        } catch (e: Exception) {
            log("The ad-free DNS could not be turned off", e)
        }
    }

    // DnsWatcher: with the ad-free DNS on and the network unvalidated for
    // 30 s, the DNS goes back to automatic and the teacher reads that the
    // network blocks it. unvalidatedFor is read once any run has ended (null:
    // validated, or no network at all). True when it went back.
    fun fallBack(unvalidatedFor: () -> Duration?): Boolean = lock.withLock {
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

    // Whether the ad-free DNS is set now (false when it cannot be read).
    fun ourDnsSet(): Boolean =
        try {
            dns.isSet()
        } catch (e: Exception) {
            false
        }

    // Only the DNS again: the apps and the time of the optimization stay.
    private fun retryDns() {
        lock.withLock {
            val held = store.current
            val state = dnsStep()
            store.save(held.copy(report = held.report.copy(dns = state), lastDnsAttempt = clock()))
        }
    }

    // The DNS tried (AdblockDns), counted for a "Otimizar" waiting behind it;
    // with the panel's "Bloqueador de anúncios" off, ours taken away instead.
    private fun dnsStep(): String = dnsWanted().also { dnsSteps.incrementAndGet() }

    private fun dnsWanted(): String {
        if (adblockWanted()) return dns.apply()
        try {
            if (dns.isSet()) dns.backToAutomatic()
        } catch (e: RuntimeException) {
            log("The private DNS could not be read", e)
        }
        log("Ad-free DNS: off in the lab panel.", null)
        return PhoneDnsStates.OFF_BY_TEACHER
    }

    private fun optimize(reuseDns: Boolean, asked: Long): PhoneOptimizeReport {
        val now = clock()
        val held = store.current
        var left = emptyList<String>()
        val next = if (gw.isDeviceOwner()) {
            showAgain(held.report.hidden)
            val plan = plan()
            val (hidden, failed) = hideApps(plan.hide)
            val (dnsState, attempt) = if (reuseDns) held.report.dns to held.lastDnsAttempt else dnsStep() to clock()
            val removed = removeApps(plan.uninstall, until = asked + UNINSTALL_WITHIN_NANOS)
            left = removed.left
            val uninstalled = (held.report.uninstalled.filter(::isGone) + removed.uninstalled).distinct()
            OptimizeState(
                PhoneOptimizeReport(
                    now, (hidden + removed.hidden).distinct(), failed + removed.failed, dnsState, AdblockDns.HOST,
                    uninstalled, pending = left.size,
                ),
                lastDnsAttempt = attempt,
            )
        } else {
            // Not the school's phone: nothing can be hidden, uninstalled nor set.
            val failed = runCatching { toPlan().let { it.hide + it.uninstall } }.getOrDefault(emptyList())
            OptimizeState(PhoneOptimizeReport(now, emptyList(), failed, PhoneDnsStates.OFF_ERROR, AdblockDns.HOST), held.lastDnsAttempt)
        }
        store.save(next)
        val report = next.report
        log(
            "Optimized: ${report.uninstalled.size} app(s) uninstalled ${report.uninstalled}, ${report.hidden.size} hidden " +
                "${report.hidden}, refused ${report.failed}, ${left.size} left for later $left, DNS ${report.dns}.",
            null,
        )
        if (left.isNotEmpty()) removeLater()
        // As kept: pending is 0 again if the background could not start.
        // (The background waits for this run's lock, so it changed nothing
        // else yet.)
        return store.current.report
    }

    // What "Otimizar" does to the phone's apps: those to hide (BloatList's,
    // and UnusedApps' of the system) and those to uninstall.
    private class Plan(val hide: List<String>, val uninstall: List<String>)

    private fun plan(): Plan =
        try {
            toPlan()
        } catch (e: Exception) {
            log("The phone's apps could not be read", e)
            Plan(emptyList(), emptyList())
        }

    private fun toPlan(): Plan {
        val never = catalog.neverHide()
        val bloat = BloatList.toHide(BloatList.PACKAGES.filter(catalog::isPresent).toSet(), never).toList()
        val (system, other) = UnusedApps.toRemove(UnusedApps.PACKAGES.filter(catalog::isPresent).toSet(), never)
            .partition(::cameWithPhone)
        return Plan(bloat + system, other)
    }

    private fun cameWithPhone(pkg: String): Boolean =
        try {
            catalog.cameWithPhone(pkg)
        } catch (e: Exception) {
            true
        }

    // No longer on the phone (false when it cannot be told).
    private fun isGone(pkg: String): Boolean =
        try {
            !catalog.isPresent(pkg)
        } catch (e: Exception) {
            false
        }

    // The apps hidden (or already hidden), and those Android would not hide.
    // Android answers false for an app already hidden, so that is asked first.
    private fun hideApps(target: List<String>): Pair<List<String>, List<String>> {
        val hidden = mutableListOf<String>()
        val failed = mutableListOf<String>()
        for (pkg in target) {
            if (hide(pkg)) hidden += pkg else failed += pkg
        }
        return hidden to failed
    }

    // An app an earlier run hid that the phone now never hides (its only
    // gallery, BloatList.keptGallery) is shown again; nothing it did not hide.
    private fun showAgain(hiddenBefore: List<String>) {
        if (hiddenBefore.isEmpty()) return
        val never = try {
            catalog.neverHide()
        } catch (e: Exception) {
            return
        }
        for (pkg in hiddenBefore.filter { it in never }) {
            try {
                if (gw.isApplicationHidden(pkg) && gw.setApplicationHidden(pkg, false)) log("Shown again: $pkg.", null)
            } catch (e: RuntimeException) {
                log("Android would not show $pkg again", e)
            }
        }
    }

    private fun hide(pkg: String): Boolean =
        try {
            gw.isApplicationHidden(pkg) || gw.setApplicationHidden(pkg, true)
        } catch (e: RuntimeException) {
            log("Android would not hide $pkg", e)
            false
        }

    private class Removal {
        val uninstalled = mutableListOf<String>()
        val hidden = mutableListOf<String>()
        val failed = mutableListOf<String>()
        val left = mutableListOf<String>()
    }

    // Uninstalled one by one, each waiting at most APP_WAIT for Android; one
    // Android refuses is hidden instead (or failed, if that is refused too).
    // until (ticker time): the apps not started by then, or Android did not
    // answer for in time, are left for later; null, none is.
    private fun removeApps(target: List<String>, until: Long?): Removal {
        val removal = Removal()
        for (pkg in target) {
            val wait = if (until == null) {
                APP_WAIT_MILLIS
            } else {
                val remaining = TimeUnit.NANOSECONDS.toMillis(until - ticker())
                if (remaining < MIN_WAIT_MILLIS) {
                    removal.left += pkg
                    continue
                }
                minOf(APP_WAIT_MILLIS, remaining)
            }
            val why = try {
                remover.uninstall(pkg, wait)
            } catch (e: Exception) {
                log("Android did not start the uninstall of $pkg", e)
                e.message ?: e.javaClass.simpleName
            }
            when {
                why == null || isGone(pkg) -> removal.uninstalled += pkg
                why == AppRemover.NO_ANSWER && until != null -> removal.left += pkg
                hide(pkg) -> {
                    log("Android would not uninstall $pkg ($why): hidden instead", null)
                    removal.hidden += pkg
                }
                else -> {
                    log("Android would neither uninstall ($why) nor hide $pkg", null)
                    removal.failed += pkg
                }
            }
        }
        return removal
    }

    // The apps a "Otimizar" left uninstalled in the background, one at a
    // time, each added to the report kept (the next "Otimizar" sends it).
    // The lock is held for one app at a time, so a teacher's "Otimizar"
    // waits for one uninstall at most (APP_WAIT).
    private fun removeLater() {
        if (!removing.compareAndSet(false, true)) return
        try {
            background(
                Runnable {
                    try {
                        val target = lock.withLock { if (gw.isDeviceOwner()) plan().uninstall else emptyList() }
                        for ((done, pkg) in target.withIndex()) {
                            lock.withLock {
                                val removal = if (isGone(pkg)) Removal() else removeApps(listOf(pkg), until = null)
                                keep(removal, pending = target.size - done - 1)
                            }
                        }
                    } catch (e: Exception) {
                        log("The apps left could not be uninstalled", e)
                    } finally {
                        noneLeft()
                        removing.set(false)
                    }
                },
            )
        } catch (e: Exception) {
            noneLeft()
            removing.set(false)
            log("The apps left could not be uninstalled", e)
        }
    }

    // Nothing is being uninstalled in the background any more.
    private fun noneLeft() {
        try {
            lock.withLock {
                val held = store.current
                if (held.report.pending != 0) store.save(held.copy(report = held.report.copy(pending = 0)))
            }
        } catch (e: Exception) {
            log("The report could not be updated", e)
        }
    }

    // One app done in the background (or found gone already); pending: how
    // many are still to go.
    private fun keep(removal: Removal, pending: Int) {
        val held = store.current
        val report = held.report
        val done = (removal.uninstalled + removal.hidden).toSet()
        if (done.isEmpty() && removal.failed.isEmpty()) {
            if (report.pending != pending) store.save(held.copy(report = report.copy(pending = pending)))
            return
        }
        store.save(
            held.copy(
                report = report.copy(
                    uninstalled = (report.uninstalled + removal.uninstalled).distinct(),
                    hidden = (report.hidden + removal.hidden).distinct(),
                    failed = (report.failed.filter { it !in done } + removal.failed).distinct(),
                    pending = pending,
                ),
            ),
        )
        log("Uninstalled later: ${removal.uninstalled}, hidden ${removal.hidden}, refused ${removal.failed}.", null)
    }

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

        // Uninstalls start at most until 60 s after the teacher asked, each
        // waiting at most 15 s for Android (and never past that), so the
        // answer is sent well within the Master's 90 s; an app with under
        // 2 s left is left for later.
        val UNINSTALL_WITHIN_NANOS = TimeUnit.SECONDS.toNanos(60)
        const val APP_WAIT_MILLIS = 15_000L
        const val MIN_WAIT_MILLIS = 2_000L
    }
}
