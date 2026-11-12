package io.github.arnoneto1.midview.agent.sites

import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.policy.DevicePolicyGateway
import io.github.arnoneto1.midview.agent.policy.Lockdown
import java.io.File
import java.io.IOException

// The phone's always-on VPN, as the Device Owner sets it: the app, and
// whether everything outside the VPN is blocked (lockdown).
data class AlwaysOnVpn(val pkg: String, val lockdown: Boolean)

// What the phone had before the internet was blocked (vpn null: none).
data class Previous(val vpn: AlwaysOnVpn?)

// "Bloquear a internet" for the whole phone, every app: the agent becomes
// the phone's always-on VPN in lockdown, which takes all traffic and lets
// none of it out (BlockVpnService), and lockdown keeps any app from going
// around it, even while that VPN is down. The agent itself stays outside, so
// the teacher keeps finding it, sending commands and seeing the screen over
// the school's network. Unblocking puts back the always-on VPN the phone had
// before (normally none), remembered in file so a restart in between does
// not lose it. The phone's private DNS is not touched, so whatever DNS it
// uses (the ad-blocking one of "Otimizar") is still there afterwards; freed
// is told when a blocked internet is freed, for the ad-free DNS "Otimizar"
// left off while it was blocked (Optimizer.internetFreed). It must return at
// once.
//
// Android does not let a Device Owner point the private DNS at a host that
// does not exist (setGlobalPrivateDnsModeSpecifiedHost first checks that the
// host serves DNS over TLS), which is why the block is a VPN.
class InternetBlock(
    private val gw: DevicePolicyGateway,
    private val file: File,
    private val startVpn: () -> Unit,
    private val stopVpn: () -> Unit,
    private val own: String = Lockdown.PACKAGE,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
    private val freed: () -> Unit = {},
) {
    // True when this phone's internet is blocked by the agent now.
    val isBlocked: Boolean get() = gw.alwaysOnVpn()?.pkg == own

    // True when the phone follows: blocked as asked, or free. Not being the
    // Device Owner, the phone cannot block, and has nothing blocked.
    @Synchronized
    fun apply(blocked: Boolean): Boolean {
        if (!gw.isDeviceOwner()) return !blocked
        return try {
            if (blocked) block() else free()
            true
        } catch (e: IOException) {
            log("The internet block could not be saved", e)
            false
        } catch (e: RuntimeException) {
            log("The internet block was refused", e)
            false
        }
    }

    private fun block() {
        val current = gw.alwaysOnVpn()
        // Remembered before anything changes.
        savePrevious(file, toSave(current, loadPrevious(file), own))
        val ours = AlwaysOnVpn(own, lockdown = true)
        if (current != ours) {
            gw.setAlwaysOnVpn(ours)
            log("Internet blocked: the agent's VPN is on, in lockdown.", null)
        }
        // Started again even when it was set: if the agent was ended, its VPN
        // is down (lockdown still blocks everything meanwhile).
        startVpn()
    }

    private fun free() {
        val wasBlocked = gw.alwaysOnVpn()?.pkg == own
        if (wasBlocked) {
            val previous = toRestore(loadPrevious(file), own)
            try {
                gw.setAlwaysOnVpn(previous)
            } catch (e: RuntimeException) {
                // The VPN app there before is gone: the internet is freed anyway.
                if (previous == null) throw e
                log("The VPN there before could not be put back", e)
                gw.setAlwaysOnVpn(null)
            }
            log("Internet freed: the agent's VPN is off.", null)
        }
        stopVpn()
        file.delete()
        // Freed is freed, whatever the listener does.
        if (wasBlocked) runCatching(freed).onFailure { log("Freeing the internet was not passed on", it) }
    }

    companion object {
        // What to remember before blocking: what is there now, unless it is
        // the block itself (the agent started again while blocked), when the
        // first record stays.
        fun toSave(current: AlwaysOnVpn?, saved: Previous?, own: String): Previous =
            if (current?.pkg == own) saved ?: Previous(null) else Previous(current)

        // What to put back when the internet is freed: what was there before,
        // never the block itself.
        fun toRestore(saved: Previous?, own: String): AlwaysOnVpn? = saved?.vpn?.takeIf { it.pkg != own }

        fun savePrevious(file: File, previous: Previous) {
            val text = previous.vpn?.let { "${it.pkg}\nlockdown=${if (it.lockdown) 1 else 0}\n" } ?: "none\n"
            file.absoluteFile.parentFile?.mkdirs()
            writeAtomically(file, text.toByteArray(Charsets.UTF_8))
        }

        // Null when there is no record (or one this agent did not write).
        fun loadPrevious(file: File): Previous? {
            val lines = try {
                if (!file.exists()) return null
                file.readText(Charsets.UTF_8).split('\n').dropLastWhile { it.isEmpty() }
            } catch (_: IOException) {
                return null
            }
            return when {
                lines == listOf("none") -> Previous(null)
                lines.size == 2 && lines[0].isNotEmpty() && lines[1] in setOf("lockdown=0", "lockdown=1") ->
                    Previous(AlwaysOnVpn(lines[0], lines[1] == "lockdown=1"))
                else -> null
            }
        }
    }
}
