package io.github.arnoneto1.midview.agent.leave

import io.github.arnoneto1.midview.agent.optimize.AdblockDns
import io.github.arnoneto1.midview.agent.optimize.PrivateDns
import io.github.arnoneto1.midview.agent.policy.Lockdown
import io.github.arnoneto1.midview.agent.sites.AlwaysOnVpn
import io.github.arnoneto1.midview.agent.sites.Previous

// A school phone as the agent left it, in memory: every call is recorded in
// order (calls), and each changes the phone's state the way Android would.
// dieAt makes the call of that name end the "process" (an Error nothing
// catches), as a phone that restarts midway.
class FakeLeaveGateway : LeaveGateway {
    class Died : Error("the process died")

    val calls = mutableListOf<String>()
    var dieAt: String? = null
    var refuse: Set<String> = emptySet()

    var owner = true
    val restrictions = (Lockdown.RESTRICTIONS + Lockdown.networkRestrictions(33) + "no_from_somewhere_else").toMutableSet()
    var ownAppLocked = true
    var lockTask = true
    var accessibilityLimited = true
    val suspended = mutableSetOf("com.whatsapp", "com.instagram.android")
    val hidden = mutableSetOf("com.android.thememanager", "com.miui.msa.global", "org.mozilla.firefox")
    var chromePolicy = true
    var vpn: AlwaysOnVpn? = AlwaysOnVpn(Lockdown.PACKAGE, lockdown = true)
    var beforeBlock: Previous? = Previous(null)
    var vpnRunning = true
    var dns = PrivateDns(PrivateDns.MODE_PROVIDER_HOSTNAME, AdblockDns.HOST)
    var networksLocked = true
    var pin = true
    var wallpaper = true
    val packages = mutableMapOf(
        "com.android.chrome" to true,
        "com.google.android.setupwizard" to false,
        "com.longcheertel.cit" to false,
        "com.android.egg" to false,
        "com.gone.forever" to false,
        Lockdown.PACKAGE to true,
    )
    val neverComesBack = setOf("com.gone.forever")
    var teachers = true
    var agentRunning = true
    var uninstallAsked = 0

    private fun call(name: String) {
        calls += name
        if (name == dieAt) {
            dieAt = null
            throw Died()
        }
        if (name in refuse) throw SecurityException("Android refused $name")
    }

    override fun isDeviceOwner(): Boolean = owner

    override fun restrictions(): Set<String> = restrictions.toSet().also { call("restrictions") }

    override fun clearRestriction(r: String) {
        call("clearRestriction")
        restrictions -= r
    }

    override fun releaseOwnApp() {
        call("releaseOwnApp")
        ownAppLocked = false
    }

    override fun endLockTask() {
        call("endLockTask")
        lockTask = false
    }

    override fun allowEveryAccessibilityService() {
        call("allowEveryAccessibilityService")
        accessibilityLimited = false
    }

    override fun suspendedApps(): List<String> = suspended.toList().also { call("suspendedApps") }

    override fun unsuspend(pkgs: List<String>): List<String> {
        call("unsuspend")
        suspended -= pkgs.toSet()
        return emptyList()
    }

    override fun hiddenApps(): List<String> = hidden.toList().also { call("hiddenApps") }

    override fun unhide(pkg: String): Boolean {
        call("unhide")
        return hidden.remove(pkg)
    }

    override fun clearChromePolicy() {
        call("clearChromePolicy")
        chromePolicy = false
    }

    override fun alwaysOnVpn(): AlwaysOnVpn? = vpn

    override fun vpnBeforeBlock(): Previous? = beforeBlock

    override fun setAlwaysOnVpn(vpn: AlwaysOnVpn?) {
        call("setAlwaysOnVpn")
        this.vpn = vpn
    }

    override fun stopBlockVpn() {
        call("stopBlockVpn")
        vpnRunning = false
    }

    override fun privateDns(): PrivateDns = dns

    override fun setPrivateDnsAutomatic(): Int {
        call("setPrivateDnsAutomatic")
        dns = PrivateDns(PrivateDns.MODE_OPPORTUNISTIC, null)
        return PrivateDns.SET_NO_ERROR
    }

    override fun unlockOwnersNetworks() {
        call("unlockOwnersNetworks")
        networksLocked = false
    }

    override fun removeScreenPin(): Boolean {
        call("removeScreenPin")
        pin = false
        return true
    }

    override fun clearWallpaper() {
        call("clearWallpaper")
        wallpaper = false
    }

    override fun packages(): List<KnownPackage> = packages.map { (pkg, installed) -> KnownPackage(pkg, installed) }.also { call("packages") }

    override fun installExisting(pkg: String): Boolean {
        call("installExisting")
        if (pkg in neverComesBack) return false
        packages[pkg] = true
        return true
    }

    override fun forgetTeachers() {
        call("forgetTeachers")
        teachers = false
    }

    override fun clearDeviceOwner() {
        call("clearDeviceOwner")
        owner = false
    }

    override fun stopAgent() {
        call("stopAgent")
        agentRunning = false
    }

    override fun askUninstall() {
        call("askUninstall")
        uninstallAsked++
    }

    // Nothing of the school's left on the phone.
    fun undone(): Boolean =
        restrictions.isEmpty() && !ownAppLocked && !lockTask && !accessibilityLimited && suspended.isEmpty() && hidden.isEmpty() &&
            !chromePolicy && vpn == null && !vpnRunning && dns.mode == PrivateDns.MODE_OPPORTUNISTIC && !networksLocked && !pin &&
            !wallpaper
}
