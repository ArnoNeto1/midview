package io.github.arnoneto1.midview.agent.apps

import io.github.arnoneto1.midview.agent.optimize.PrivateDns
import io.github.arnoneto1.midview.agent.policy.DevicePolicyGateway
import io.github.arnoneto1.midview.agent.protocol.AppBlockMode
import io.github.arnoneto1.midview.agent.protocol.PhoneAppPolicy
import io.github.arnoneto1.midview.agent.sites.AlwaysOnVpn
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class AppCloserTest {
    private val self = "io.github.arnoneto1.midview.agent"
    private val launchable = setOf(
        "com.android.chrome", "com.miui.calculator", "com.android.camera", "com.whatsapp",
        "com.android.settings", "com.miui.home", "com.android.dialer", self,
    )
    private val protected = setOf("com.android.settings", "com.miui.home", "com.android.dialer", self)

    // Which apps: every one the student can open, but the protected ones,
    // the agent and what is suspended already.
    @Test fun closesEveryOpenableAppButTheProtectedOnes() {
        assertEquals(
            listOf("com.android.camera", "com.android.chrome", "com.miui.calculator", "com.whatsapp"),
            AppCloser.toClose(launchable, protected, emptySet(), self),
        )
    }

    // The teacher's block (or another app's suspension) is never touched:
    // closing would otherwise lift it.
    @Test fun neverTouchesAnAppSuspendedAlready() {
        assertEquals(
            listOf("com.android.camera", "com.android.chrome", "com.whatsapp"),
            AppCloser.toClose(launchable, protected, setOf("com.miui.calculator"), self),
        )
    }

    // Even if it ever showed up as not protected.
    @Test fun neverClosesTheAgentItself() {
        assertFalse(self in AppCloser.toClose(setOf(self, "com.whatsapp"), emptySet(), emptySet(), self))
    }

    @Test fun nothingOpenableLeavesNothingToClose() {
        assertEquals(emptyList<String>(), AppCloser.toClose(protected, protected, emptySet(), self))
    }

    @Test fun theTeacherReadsWhatHappened() {
        assertEquals("apps fechados; tela inicial na frente", AppCloser.detail(0))
        assertEquals("apps fechados; tela inicial na frente (1 app não fechou)", AppCloser.detail(1))
        assertEquals("apps fechados; tela inicial na frente (3 apps não fecharam)", AppCloser.detail(3))
    }

    // The phone as the closer sees it: one ordered log of what happened.
    private val events = mutableListOf<String>()
    private val suspended = mutableSetOf<String>()

    private inner class Catalog(val installed: Set<String> = launchable) : AppCatalog {
        override fun launchable() = launchable
        override fun protected() = protected
        override fun suspended() = suspended.toSet()
        override fun isInstalled(pkg: String) = pkg in installed
    }

    private inner class Gateway(var owner: Boolean = true, val refused: Set<String> = emptySet()) : DevicePolicyGateway {
        var throwOnSuspend = false
        override fun setPackagesSuspended(pkgs: List<String>, suspended: Boolean): List<String> {
            if (!owner || throwOnSuspend) throw SecurityException("not the device owner")
            events += (if (suspended) "suspend " else "release ") + pkgs.joinToString(",")
            val done = pkgs.filter { it !in refused }
            if (suspended) this@AppCloserTest.suspended += done else this@AppCloserTest.suspended -= done.toSet()
            return pkgs.filter { it in refused }
        }
        override fun isDeviceOwner() = owner
        override fun addWifi(ssid: String, password: String?) {}
        override fun wipe() {}
        override fun addUserRestriction(r: String) = Unit
        override fun setUninstallBlocked(pkg: String, blocked: Boolean) = Unit
        override fun setUserControlDisabled(pkgs: List<String>) = Unit
        override fun grantPermission(pkg: String, permission: String) = Unit
        override fun setLockTaskPackages(pkgs: List<String>) = Unit
        override fun setLockTaskFeatures(features: Int) = Unit
        override fun setPermittedAccessibilityServices(pkgs: List<String>?) = Unit
        override fun setApplicationRestrictions(pkg: String, restrictions: Map<String, Any>) = Unit
        override fun setApplicationHidden(pkg: String, hidden: Boolean): Boolean = true
        override fun isApplicationHidden(pkg: String): Boolean = false
        override fun privateDns(): PrivateDns = PrivateDns(PrivateDns.MODE_OPPORTUNISTIC, null)
        override fun setPrivateDnsHost(host: String): Int = PrivateDns.SET_NO_ERROR
        override fun writePrivateDnsHost(host: String) = Unit
        override fun setPrivateDnsAutomatic(): Int = PrivateDns.SET_NO_ERROR
        override fun alwaysOnVpn(): AlwaysOnVpn? = null
        override fun setAlwaysOnVpn(vpn: AlwaysOnVpn?) = Unit
        override val sdkInt = 33
        override val debuggable = true
    }

    private var locked = false
    private var homeFails = false
    private val later = mutableListOf<Pair<Long, () -> Unit>>()

    private fun closer(gw: Gateway, catalog: AppCatalog = Catalog(), suspender: AppSuspender = AppSuspender(catalog, gw)) = AppCloser(
        suspender, gw, self,
        reapplyBlock = { events += "reapply" },
        isScreenLocked = { locked },
        goHome = { if (homeFails) throw IllegalStateException("refused") else events += "home" },
        killBackground = { events += "kill $it" },
        pause = { events += "pause" },
        runLater = { delay, task -> later += delay to task },
    )

    private fun runLater() {
        val tasks = later.toList()
        later.clear()
        tasks.forEach { it.second() }
    }

    @Test fun homeThenSuspendHoldEndAndReleaseThenTheBlockAgain() {
        val result = closer(Gateway()).close()

        assertEquals(true to AppCloser.CLOSED, result)
        val apps = "com.android.camera,com.android.chrome,com.miui.calculator,com.whatsapp"
        assertEquals(
            listOf(
                "home",
                "suspend $apps",
                "pause",
                "kill com.android.camera", "kill com.android.chrome", "kill com.miui.calculator", "kill com.whatsapp",
                "release $apps",
                "reapply",
            ),
            events,
        )
        assertTrue("nothing is left suspended by the closing", suspended.isEmpty())
    }

    // "Bloquear só estes" [Calculadora]: the calculator stays suspended
    // through the closing, and is never released.
    @Test fun theTeachersBlockStays() {
        val gw = Gateway()
        val suspender = AppSuspender(Catalog(), gw)
        suspender.apply(PhoneAppPolicy(100, AppBlockMode.BLOCK_LIST, listOf("com.miui.calculator")))
        events.clear()

        closer(gw, suspender = suspender).close()

        assertEquals(setOf("com.miui.calculator"), suspended)
        assertFalse(events.toString(), events.any { it.startsWith("release") && "com.miui.calculator" in it })
        assertFalse(events.toString(), events.any { it.startsWith("suspend") && "com.miui.calculator" in it })
    }

    // Under the teacher's lock screen (lock task mode) Android does not close
    // a suspended app: the closing waits for the lock to go, and the lock
    // stays on top meanwhile (no home screen over it, nothing suspended).
    @Test fun underTheTeachersLockTheClosingWaitsForTheLockToGo() {
        locked = true
        val c = closer(Gateway())

        assertEquals(true to AppCloser.AFTER_LOCK, c.close())
        assertTrue(events.isEmpty())
        assertTrue(c.isPending)

        locked = false
        c.unlocked()
        assertEquals(listOf(AppCloser.UNLOCK_SETTLE_MILLIS), later.map { it.first })
        assertTrue("nothing before the lock task ended", events.isEmpty())
        runLater()

        assertEquals("home", events.first())
        assertTrue(events.any { it.startsWith("suspend") })
        assertEquals("reapply", events.last())
        assertFalse(c.isPending)
        assertTrue(suspended.isEmpty())

        // Done once: the next unlock has nothing to do.
        events.clear()
        c.unlocked()
        runLater()
        assertTrue(events.isEmpty())
    }

    // Locked again before the closing ran: it waits for that lock too.
    @Test fun lockedAgainBeforeTheClosingItKeepsWaiting() {
        locked = true
        val c = closer(Gateway())
        c.close()
        locked = false
        c.unlocked()
        locked = true
        runLater()
        assertTrue(events.isEmpty())
        assertTrue(c.isPending)

        locked = false
        c.unlocked()
        runLater()
        assertTrue(events.any { it.startsWith("suspend") })
        assertFalse(c.isPending)
    }

    // An unlock with nothing asked for does nothing.
    @Test fun anUnlockWithNoClosingAskedForDoesNothing() {
        closer(Gateway()).unlocked()
        assertTrue(later.isEmpty())
    }

    // Lifted between the two looks: closed at once, nothing left waiting.
    @Test fun aLockLiftedWhileAskingClosesAtOnce() {
        var looks = 0
        val gw = Gateway()
        val c = AppCloser(
            AppSuspender(Catalog(), gw), gw, self,
            reapplyBlock = {}, isScreenLocked = { looks++ == 0 }, goHome = { events += "home" },
            killBackground = {}, pause = {}, runLater = { _, _ -> error("not expected") },
        )
        assertEquals(true to AppCloser.CLOSED, c.close())
        assertFalse(c.isPending)
        assertEquals("home", events.first())
    }

    // While locked, a phone that isn't the Device Owner still says so.
    @Test fun notTheDeviceOwnerSaysSoEvenUnderTheLock() {
        locked = true
        val c = closer(Gateway(owner = false))
        assertEquals(false to AppCloser.NOT_DEVICE_OWNER, c.close())
        assertFalse(c.isPending)
    }

    // Android would not suspend one: it is not released (it was never ours) and the teacher is told.
    @Test fun anAppAndroidWouldNotSuspendIsCountedAndNotReleased() {
        val result = closer(Gateway(refused = setOf("com.whatsapp"))).close()

        assertEquals(true to "apps fechados; tela inicial na frente (1 app não fechou)", result)
        assertEquals("release com.android.camera,com.android.chrome,com.miui.calculator", events.single { it.startsWith("release") })
        assertFalse("kill com.whatsapp" in events)
    }

    @Test fun notTheDeviceOwnerClosesNothing() {
        val result = closer(Gateway(owner = false)).close()
        assertEquals(false to AppCloser.NOT_DEVICE_OWNER, result)
        assertTrue(events.isEmpty())
    }

    @Test fun aRefusalIsAnsweredAndTheBlockStillAppliedAgain() {
        val gw = Gateway().apply { throwOnSuspend = true }
        val result = closer(gw).close()
        assertEquals(false to AppCloser.NOT_CLOSED, result)
        assertEquals(listOf("home", "reapply"), events)
    }

    // The apps still close; the home screen is what is left.
    @Test fun aHomeScreenThatWillNotComeUpDoesNotStopTheClosing() {
        homeFails = true
        val result = closer(Gateway()).close()
        assertEquals(true to AppCloser.CLOSED, result)
        assertTrue(events.any { it.startsWith("release") })
    }

    // Whatever happens while held, what was suspended is let go.
    @Test fun whatWasSuspendedIsReleasedEvenIfTheHoldFails() {
        val gw = Gateway()
        val c = AppCloser(
            AppSuspender(Catalog(), gw), gw, self,
            reapplyBlock = { events += "reapply" }, isScreenLocked = { false }, goHome = {},
            killBackground = { throw SecurityException("no") }, pause = { throw IllegalStateException("woken") },
        )
        assertEquals(false to AppCloser.NOT_CLOSED, c.close())
        assertTrue(suspended.isEmpty())
        assertEquals("reapply", events.last())
    }

    @Test fun aKillThatFailsDoesNotStopTheOthers() {
        val gw = Gateway()
        val c = AppCloser(
            AppSuspender(Catalog(), gw), gw, self,
            reapplyBlock = {}, isScreenLocked = { false }, goHome = {},
            killBackground = { if (it == "com.android.camera") throw SecurityException("no") else events += "kill $it" },
            pause = {},
        )
        assertEquals(true to AppCloser.CLOSED, c.close())
        assertEquals(3, events.count { it.startsWith("kill") })
        assertTrue(suspended.isEmpty())
    }

    // An app removed between the reading and the call: not counted as refused.
    @Test fun aRefusedAppThatIsGoneIsNotCounted() {
        val result = closer(Gateway(refused = setOf("com.whatsapp")), Catalog(installed = launchable - "com.whatsapp")).close()
        assertEquals(true to AppCloser.CLOSED, result)
    }
}
