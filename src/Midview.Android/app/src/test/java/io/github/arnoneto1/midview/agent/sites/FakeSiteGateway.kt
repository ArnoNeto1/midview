package io.github.arnoneto1.midview.agent.sites

import io.github.arnoneto1.midview.agent.policy.DevicePolicyGateway

// The DevicePolicyManager calls of the site block, recorded; the rest does
// nothing.
class FakeSiteGateway(var owner: Boolean = true) : DevicePolicyGateway {
    var alwaysOn: AlwaysOnVpn? = null
    val alwaysOnCalls = mutableListOf<AlwaysOnVpn?>()
    var refuseAlwaysOn = false
    val restrictions = mutableMapOf<String, Map<String, Any>>()
    val hidden = mutableSetOf<String>()
    val hiddenCalls = mutableListOf<Pair<String, Boolean>>()
    var refuseHiding = emptySet<String>()

    override fun alwaysOnVpn(): AlwaysOnVpn? = alwaysOn

    override fun setAlwaysOnVpn(vpn: AlwaysOnVpn?) {
        if (!owner) throw SecurityException("not the device owner")
        if (refuseAlwaysOn) throw UnsupportedOperationException("refused")
        alwaysOnCalls += vpn
        alwaysOn = vpn
    }

    override fun setApplicationRestrictions(pkg: String, restrictions: Map<String, Any>) {
        if (!owner) throw SecurityException("not the device owner")
        this.restrictions[pkg] = restrictions
    }

    override fun setApplicationHidden(pkg: String, hidden: Boolean): Boolean {
        hiddenCalls += pkg to hidden
        if (pkg in refuseHiding) return false
        if (hidden) this.hidden += pkg else this.hidden -= pkg
        return true
    }

    override fun isDeviceOwner() = owner
    override fun addUserRestriction(r: String) = Unit
    override fun setUninstallBlocked(pkg: String, blocked: Boolean) = Unit
    override fun setUserControlDisabled(pkgs: List<String>) = Unit
    override fun grantPermission(pkg: String, permission: String) = Unit
    override fun setLockTaskPackages(pkgs: List<String>) = Unit
    override fun setLockTaskFeatures(features: Int) = Unit
    override fun setPermittedAccessibilityServices(pkgs: List<String>?) = Unit
    override fun setPackagesSuspended(pkgs: List<String>, suspended: Boolean): List<String> = emptyList()
    override val sdkInt = 33
    override val debuggable = true
}
