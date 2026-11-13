package io.github.arnoneto1.midview.agent.leave

import io.github.arnoneto1.midview.agent.optimize.AdblockDns
import io.github.arnoneto1.midview.agent.optimize.PrivateDns
import io.github.arnoneto1.midview.agent.policy.Lockdown
import io.github.arnoneto1.midview.agent.sites.AlwaysOnVpn
import io.github.arnoneto1.midview.agent.sites.InternetBlock
import io.github.arnoneto1.midview.agent.sites.Previous

// An app the phone knows (PackageManager with MATCH_UNINSTALLED_PACKAGES),
// and whether it is installed for this user (ApplicationInfo.FLAG_INSTALLED):
// a system app uninstalled for the user ("pm uninstall --user 0") is not.
data class KnownPackage(val pkg: String, val installed: Boolean)

// What the Midview's leaving needs from Android, behind an interface so the
// plan is tested on the JVM. Everything but forgetTeachers, stopAgent and
// askUninstall needs the Device Owner.
interface LeaveGateway {
    fun isDeviceOwner(): Boolean

    // The user restrictions the app's admin holds now (Lockdown's and the
    // network's), and lifting one.
    fun restrictions(): Set<String>
    fun clearRestriction(r: String)

    // The app may be uninstalled, force-stopped and have its data cleared.
    fun releaseOwnApp()

    // The teacher's lock screen gone, and no app allowed to hold the phone
    // in lock task mode (Android's default features back).
    fun endLockTask()

    // Every accessibility service may be turned on again.
    fun allowEveryAccessibilityService()

    // Apps suspended now (the app block, a "Fechar programas" cut short),
    // and lifting them; the second returns those Android would not lift.
    fun suspendedApps(): List<String>
    fun unsuspend(pkgs: List<String>): List<String>

    // Apps the Device Owner hid ("Otimizar", other browsers, Xiaomi's
    // "Temas"), and showing one again (false when Android would not).
    fun hiddenApps(): List<String>
    fun unhide(pkg: String): Boolean

    // Chrome's managed configuration (the site and download blocks) cleared.
    fun clearChromePolicy()

    // The always-on VPN, what it was before the internet block
    // (InternetBlock's record), setting it and stopping the agent's own.
    fun alwaysOnVpn(): AlwaysOnVpn?
    fun vpnBeforeBlock(): Previous?
    fun setAlwaysOnVpn(vpn: AlwaysOnVpn?)
    fun stopBlockVpn()

    fun privateDns(): PrivateDns
    fun setPrivateDnsAutomatic(): Int

    // The networks the Device Owner added may be changed and forgotten again
    // (WIFI_DEVICE_OWNER_CONFIGS_LOCKDOWN back to 0).
    fun unlockOwnersNetworks()

    // The teacher's PIN off the lock screen, Android's PIN rule and the reset
    // token cleared; false when the teacher's PIN is still on it.
    fun removeScreenPin(): Boolean

    // The phone's own wallpaper back on both screens.
    fun clearWallpaper()

    fun packages(): List<KnownPackage>

    // DevicePolicyManager.installExistingPackage: a system app uninstalled
    // for this user comes back; false when Android would not.
    fun installExisting(pkg: String): Boolean

    // The trusted teachers, the agent's identity and every setting it kept
    // (the admin password, the blocks, the cable setup) deleted, never the
    // student's files (Download/Midview, the class folder).
    fun forgetTeachers()
    fun clearDeviceOwner()

    // The agent's service and its watchdog stopped for good.
    fun stopAgent()

    // Android's own "uninstall this app?" shown (the student taps it), and a
    // notification that brings it back if the screen was off.
    fun askUninstall()
}

// Each part of the leaving's undo, in the order it runs, with how the
// teacher reads one that failed.
enum class LeaveStep(val said: String) {
    RESTRICTIONS("as travas do Android"),
    LOCK_TASK("a tela travada pelo professor"),
    OWN_APP("a proteção do app do Midview"),
    ACCESSIBILITY("a lista de acessibilidade"),
    SUSPENDED("os apps bloqueados"),
    HIDDEN("os apps escondidos"),
    CHROME("as regras do Chrome"),
    VPN("o bloqueio da internet"),
    DNS("o DNS sem anúncios"),
    WIFI("a rede Wi-Fi da escola"),
    SCREEN_PIN("o PIN da tela"),
    WALLPAPER("o papel de parede"),
    RESTORE_APPS("os apps do sistema tirados"),
}

// What an undo did, for the teacher's CommandAck and the phone's screen.
data class LeaveReport(
    val owner: Boolean,
    val failed: List<LeaveStep> = emptyList(),
    val unsuspended: Int = 0,
    val unhidden: Int = 0,
    val restored: Int = 0,
    val notRestored: Int = 0,
) {
    fun text(): String {
        if (!owner) return NOT_OWNER
        val done = mutableListOf("travas e ajustes da escola desfeitos")
        if (unsuspended > 0) done += count(unsuspended, "app desbloqueado", "apps desbloqueados")
        if (unhidden > 0) done += count(unhidden, "app mostrado de novo", "apps mostrados de novo")
        if (restored > 0) done += count(restored, "app do sistema de volta", "apps do sistema de volta")
        val lines = mutableListOf("Midview saindo: ${done.joinToString(", ")}.")
        if (notRestored > 0) lines += "${count(notRestored, "app do sistema não voltou", "apps do sistema não voltaram")}."
        if (failed.isNotEmpty()) lines += "Não deu para desfazer ${failed.joinToString(", ") { it.said }}."
        lines += "O celular pede para confirmar a desinstalação na tela."
        return lines.joinToString(" ")
    }

    companion object {
        const val NOT_OWNER =
            "Midview saindo: este aparelho não tinha travas da escola. O celular pede para confirmar a desinstalação na tela."

        private fun count(n: Int, one: String, many: String) = if (n == 1) "1 $one" else "$n $many"
    }
}

// "Desfazer e tirar o Midview": a school phone given back as a normal phone,
// with the student's photos and files kept. start lifts every lock and
// setting the agent applied (undo: RESTRICTIONS to RESTORE_APPS, the system
// apps uninstalled for the user brought back), and the teacher is told what
// it did; finish then forgets the teachers, stops being the Device Owner and
// asks Android to uninstall the app (the student taps once). Every step sets
// a state, so running it again changes nothing, and a step Android refuses
// never stops the others. The phase is kept (LeaveStore) before anything
// changes: from then on the agent never locks the phone again, and a process
// that died midway finishes at its next start (resume) instead.
class PhoneLeave(
    private val gw: LeaveGateway,
    private val store: LeaveStore,
    private val own: String = Lockdown.PACKAGE,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    // A leaving this process runs now (start until finish): resume leaves it alone.
    @Volatile private var running = false

    // True from the first ask on: nothing may lock the phone again.
    val isActive: Boolean get() = store.phase != null

    // The teacher's command or the phone's own screen: the leaving kept, the
    // locks undone. finish must follow.
    @Synchronized
    fun start(): LeaveReport {
        running = true
        if (store.phase == null) store.save(LeaveStore.Phase.STARTED)
        log("The Midview is leaving this phone.", null)
        return undo()
    }

    // The teachers forgotten, the Device Owner given up, the uninstall asked.
    @Synchronized
    fun finish() {
        try {
            quietly("forget the teachers") { gw.forgetTeachers() }
            if (gw.isDeviceOwner()) quietly("stop being the Device Owner") { gw.clearDeviceOwner() }
            if (gw.isDeviceOwner()) {
                // Android refused: the next start tries again; the phone is already free.
                log("Android kept the Midview as the Device Owner.", null)
                return
            }
            store.save(LeaveStore.Phase.OWNER_CLEARED)
            quietly("stop the agent") { gw.stopAgent() }
            quietly("ask for the uninstall") { gw.askUninstall() }
        } finally {
            running = false
        }
    }

    // At a start of the agent (or a watchdog tick) with the leaving kept
    // from before: finished, never locked again. False when there is
    // nothing to finish (or this process is finishing it already).
    @Synchronized
    fun resume(): Boolean {
        val phase = store.phase ?: return false
        if (running) return false
        log("The Midview's leaving goes on from ${phase.word}.", null)
        if (phase == LeaveStore.Phase.OWNER_CLEARED && !gw.isDeviceOwner()) {
            quietly("stop the agent") { gw.stopAgent() }
            quietly("ask for the uninstall") { gw.askUninstall() }
            return true
        }
        running = true
        undo()
        finish()
        return true
    }

    private fun undo(): LeaveReport {
        if (!gw.isDeviceOwner()) {
            store.save(LeaveStore.Phase.UNDONE)
            return LeaveReport(owner = false)
        }
        val failed = mutableListOf<LeaveStep>()
        var unsuspended = 0
        var unhidden = 0
        var restore = RestoreCount(0, 0)
        fun step(which: LeaveStep, action: () -> Unit) {
            try {
                action()
            } catch (e: RuntimeException) {
                log("Couldn't undo ${which.name}", e)
                failed += which
            }
        }

        step(LeaveStep.RESTRICTIONS) {
            val refused = (gw.restrictions() + KNOWN_RESTRICTIONS).sorted().filterNot { r -> tried { gw.clearRestriction(r) } }
            check(refused.isEmpty()) { "restrictions kept: $refused" }
        }
        step(LeaveStep.LOCK_TASK) { gw.endLockTask() }
        step(LeaveStep.OWN_APP) { gw.releaseOwnApp() }
        step(LeaveStep.ACCESSIBILITY) { gw.allowEveryAccessibilityService() }
        step(LeaveStep.SUSPENDED) {
            val suspended = gw.suspendedApps()
            val stuck = if (suspended.isEmpty()) emptyList() else gw.unsuspend(suspended.sorted())
            unsuspended = suspended.size - stuck.size
            check(stuck.isEmpty()) { "still suspended: $stuck" }
        }
        step(LeaveStep.HIDDEN) {
            val hidden = gw.hiddenApps().sorted()
            val kept = hidden.filterNot { pkg -> tried { check(gw.unhide(pkg)) } }
            unhidden = hidden.size - kept.size
            check(kept.isEmpty()) { "still hidden: $kept" }
        }
        step(LeaveStep.CHROME) { gw.clearChromePolicy() }
        step(LeaveStep.VPN) { freeVpn() }
        step(LeaveStep.DNS) {
            if (gw.privateDns().isStrictOn(AdblockDns.HOST)) {
                val result = gw.setPrivateDnsAutomatic()
                check(result == PrivateDns.SET_NO_ERROR) { "private DNS answered $result" }
            }
        }
        step(LeaveStep.WIFI) { gw.unlockOwnersNetworks() }
        step(LeaveStep.SCREEN_PIN) { check(gw.removeScreenPin()) { "the teacher's PIN is still on" } }
        step(LeaveStep.WALLPAPER) { gw.clearWallpaper() }
        // An app that does not come back is counted, not a failure of the step.
        step(LeaveStep.RESTORE_APPS) { restore = restoreApps() }
        store.save(maxOf(store.phase ?: LeaveStore.Phase.UNDONE, LeaveStore.Phase.UNDONE))
        val report = LeaveReport(true, failed, unsuspended, unhidden, restore.restored, restore.notRestored)
        log("The school's locks were undone: ${report.text()}", null)
        return report
    }

    // The always-on VPN the phone had before the internet block, or none.
    private fun freeVpn() {
        if (gw.alwaysOnVpn()?.pkg == own) {
            val previous = InternetBlock.toRestore(gw.vpnBeforeBlock(), own)
            try {
                gw.setAlwaysOnVpn(previous)
            } catch (e: RuntimeException) {
                if (previous == null) throw e
                log("The VPN there before could not be put back", e)
                gw.setAlwaysOnVpn(null)
            }
        }
        gw.stopBlockVpn()
    }

    private data class RestoreCount(val restored: Int, val notRestored: Int)

    private fun restoreApps(): RestoreCount {
        val wanted = toRestore(gw.packages(), own)
        val back = wanted.count { pkg -> tried { check(gw.installExisting(pkg)) } }
        log("System apps brought back: $back of ${wanted.size}.", null)
        return RestoreCount(back, wanted.size - back)
    }

    // True when Android did it; a refusal (RuntimeException) is false, and
    // only that: anything worse ends the process, which resume picks up.
    private fun tried(action: () -> Unit): Boolean =
        try {
            action()
            true
        } catch (e: RuntimeException) {
            false
        }

    private fun quietly(what: String, action: () -> Unit) {
        try {
            action()
        } catch (e: RuntimeException) {
            log("Couldn't $what", e)
        }
    }

    companion object {
        // Every restriction any version of the agent sets, lifted even if
        // Android does not list it as the admin's (Lockdown's, the network's
        // on any Android, and DISALLOW_APPS_CONTROL of Android 10).
        val KNOWN_RESTRICTIONS: Set<String> =
            (Lockdown.RESTRICTIONS + Lockdown.networkRestrictions(Int.MAX_VALUE) + Lockdown.DISALLOW_APPS_CONTROL).toSet()

        // The apps uninstalled for this user that come back: every one the
        // phone still knows (a system app removed by the cable setup or by
        // hand), never the agent itself.
        fun toRestore(packages: List<KnownPackage>, own: String): List<String> =
            packages.filter { !it.installed && it.pkg != own }.map { it.pkg }.distinct().sorted()
    }
}
