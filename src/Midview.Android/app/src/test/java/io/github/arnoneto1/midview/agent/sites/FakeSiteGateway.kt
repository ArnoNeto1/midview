package io.github.arnoneto1.midview.agent.sites

import io.github.arnoneto1.midview.agent.optimize.PrivateDns
import io.github.arnoneto1.midview.agent.policy.DevicePolicyGateway

// The DevicePolicyManager calls of the site block and of "Otimizar" (hidden
// apps, the private DNS), recorded; the rest does nothing.
class FakeSiteGateway(var owner: Boolean = true) : DevicePolicyGateway {
    override fun addWifi(ssid: String, password: String?) {}
    var alwaysOn: AlwaysOnVpn? = null
    val alwaysOnCalls = mutableListOf<AlwaysOnVpn?>()
    var refuseAlwaysOn = false
    val restrictions = mutableMapOf<String, Map<String, Any>>()
    val hidden = mutableSetOf<String>()
    val hiddenCalls = mutableListOf<Pair<String, Boolean>>()
    var refuseHiding = emptySet<String>()

    // The private DNS as Settings has it (Android's default: automatic), what
    // setting a host answers (a PRIVATE_DNS_SET_* value; only NO_ERROR
    // changes it, as on Android), and every set, in order.
    var privateDns = PrivateDns(PrivateDns.MODE_OPPORTUNISTIC, null)
    var setHostResult = PrivateDns.SET_NO_ERROR
    var refuseDns = false
    val dnsCalls = mutableListOf<String>()

    // Writing the two settings directly (setGlobalSetting): refused, with no
    // effect, or only the mode taking.
    var refuseWrite = false
    var writeHasNoEffect = false
    var writeOnlyTheMode = false

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

    override fun isApplicationHidden(pkg: String): Boolean {
        if (!owner) throw SecurityException("not the device owner")
        return pkg in hidden
    }

    override fun privateDns(): PrivateDns {
        if (!owner) throw SecurityException("not the device owner")
        return privateDns
    }

    override fun setPrivateDnsHost(host: String): Int {
        if (!owner) throw SecurityException("not the device owner")
        if (refuseDns) throw IllegalStateException("refused")
        dnsCalls += "host $host"
        if (setHostResult == PrivateDns.SET_NO_ERROR) privateDns = PrivateDns(PrivateDns.MODE_PROVIDER_HOSTNAME, host)
        return setHostResult
    }

    override fun writePrivateDnsHost(host: String) {
        if (!owner) throw SecurityException("not the device owner")
        dnsCalls += "write $host"
        if (refuseWrite) throw SecurityException("device owners cannot update private_dns_mode")
        when {
            writeHasNoEffect -> Unit
            writeOnlyTheMode -> privateDns = PrivateDns(PrivateDns.MODE_PROVIDER_HOSTNAME, privateDns.host)
            else -> privateDns = PrivateDns(PrivateDns.MODE_PROVIDER_HOSTNAME, host)
        }
    }

    override fun setPrivateDnsAutomatic(): Int {
        if (!owner) throw SecurityException("not the device owner")
        dnsCalls += "automatic"
        privateDns = PrivateDns(PrivateDns.MODE_OPPORTUNISTIC, null)
        return PrivateDns.SET_NO_ERROR
    }

    override fun isDeviceOwner() = owner

    // "Apagar e preparar de novo": how often the phone was erased, what runs
    // as it is, and what the test saw then.
    var wipes = 0
    var sentWhenWiped = -1
    var onWipe: () -> Unit = {}
    override fun wipe() {
        wipes++
        onWipe()
    }
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
