package io.github.arnoneto1.midview.agent.apps

import io.github.arnoneto1.midview.agent.policy.DevicePolicyGateway
import io.github.arnoneto1.midview.agent.protocol.AppBlockMode
import io.github.arnoneto1.midview.agent.protocol.PhoneAppPolicy
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class AppSuspenderTest {
    private val launchable = setOf("com.android.chrome", "com.whatsapp", "com.android.camera", "com.android.settings", "com.miui.home")
    private val protected = setOf("com.android.settings", "com.miui.home", "io.github.arnoneto1.midview.agent")

    private fun policy(mode: AppBlockMode, vararg pkgs: String) = PhoneAppPolicy(100, mode, pkgs.toList())

    @Test fun nothingBlockedSuspendsNothing() {
        assertEquals(emptySet<String>(), AppSuspender.toSuspend(PhoneAppPolicy.lifted(5), launchable, protected))
    }

    @Test fun blockListSuspendsTheListedApps() {
        assertEquals(
            setOf("com.whatsapp"),
            AppSuspender.toSuspend(policy(AppBlockMode.BLOCK_LIST, "com.whatsapp"), launchable, protected),
        )
    }

    @Test fun blockListNeverSuspendsAProtectedApp() {
        assertEquals(
            setOf("com.android.chrome"),
            AppSuspender.toSuspend(
                policy(AppBlockMode.BLOCK_LIST, "com.android.chrome", "com.android.settings", "com.miui.home"),
                launchable, protected,
            ),
        )
    }

    // Not installed (or with no icon to open): nothing to suspend until it is.
    @Test fun blockListSkipsAppsTheStudentCannotOpen() {
        assertEquals(
            emptySet<String>(),
            AppSuspender.toSuspend(policy(AppBlockMode.BLOCK_LIST, "com.instagram.android"), launchable, protected),
        )
    }

    @Test fun allowOnlySuspendsEverythingElse() {
        assertEquals(
            setOf("com.whatsapp", "com.android.camera"),
            AppSuspender.toSuspend(policy(AppBlockMode.ALLOW_ONLY, "com.android.chrome"), launchable, protected),
        )
    }

    @Test fun allowOnlyEmptyLeavesOnlyTheEssentials() {
        assertEquals(
            setOf("com.android.chrome", "com.whatsapp", "com.android.camera"),
            AppSuspender.toSuspend(policy(AppBlockMode.ALLOW_ONLY), launchable, protected),
        )
    }

    @Test fun anAppInstalledLaterIsSuspendedInAllowOnly() {
        val allowOnly = policy(AppBlockMode.ALLOW_ONLY, "com.android.chrome")
        val before = AppSuspender.toSuspend(allowOnly, launchable, protected)
        val after = AppSuspender.toSuspend(allowOnly, launchable + "com.example.game", protected)
        assertFalse("com.example.game" in before)
        assertTrue("com.example.game" in after)
    }

    @Test fun aListedAppInstalledLaterIsSuspendedInBlockList() {
        val blockList = policy(AppBlockMode.BLOCK_LIST, "com.example.game")
        assertEquals(emptySet<String>(), AppSuspender.toSuspend(blockList, launchable, protected))
        assertEquals(setOf("com.example.game"), AppSuspender.toSuspend(blockList, launchable + "com.example.game", protected))
    }

    @Test fun suspendedAppsOutsideTheTargetAreLifted() {
        assertEquals(
            setOf("com.android.camera", "com.android.settings"),
            AppSuspender.toLift(setOf("com.whatsapp"), setOf("com.whatsapp", "com.android.camera", "com.android.settings")),
        )
    }

    @Test fun theEssentialsAreAlwaysProtected() {
        listOf(
            "io.github.arnoneto1.midview.agent",
            "com.android.systemui",
            "com.google.android.gms",
            "com.google.android.webview",
            "com.android.webview",
            "com.android.packageinstaller",
            "com.google.android.packageinstaller",
            "com.miui.packageinstaller",
            "com.android.settings",
            "com.android.phone",
            "com.android.server.telecom",
            "com.miui.home",
            "com.miui.securitycenter",
            "com.miui.powerkeeper",
            "com.xiaomi.joyose",
            "com.miui.notification",
        ).forEach { assertTrue(it, it in ProtectedApps.ALWAYS) }
    }

    @Test fun protectedAppsAddWhatThePhoneResolves() {
        val all = ProtectedApps.with(listOf("com.google.android.dialer", null, "com.sec.android.app.launcher"))
        assertTrue("com.google.android.dialer" in all)
        assertTrue("com.sec.android.app.launcher" in all)
        assertTrue(ProtectedApps.ALWAYS.all { it in all })
    }

    // Applying: what Android is told, lifting first.
    private class FakeCatalog(var launchable: Set<String>, var suspended: Set<String>, val protected: Set<String>) : AppCatalog {
        override fun launchable() = launchable
        override fun protected() = protected
        override fun suspended() = suspended
    }

    private class FakeGateway(var owner: Boolean = true, val refused: Set<String> = emptySet()) : DevicePolicyGateway {
        val calls = mutableListOf<Pair<List<String>, Boolean>>()
        override fun setPackagesSuspended(pkgs: List<String>, suspended: Boolean): List<String> {
            if (!owner) throw SecurityException("not the device owner")
            calls += pkgs to suspended
            return pkgs.filter { it in refused }
        }
        override fun isDeviceOwner() = owner
        override fun addUserRestriction(r: String) = Unit
        override fun setUninstallBlocked(pkg: String, blocked: Boolean) = Unit
        override fun setUserControlDisabled(pkgs: List<String>) = Unit
        override fun grantPermission(pkg: String, permission: String) = Unit
        override fun setLockTaskPackages(pkgs: List<String>) = Unit
        override fun setLockTaskFeatures(features: Int) = Unit
        override fun setPermittedAccessibilityServices(pkgs: List<String>?) = Unit
        override val sdkInt = 33
        override val debuggable = true
    }

    @Test fun applyLiftsThenSuspendsTheDifference() {
        val catalog = FakeCatalog(launchable, suspended = setOf("com.android.camera"), protected = protected)
        val gw = FakeGateway()
        val warning = AppSuspender(catalog, gw).apply(policy(AppBlockMode.BLOCK_LIST, "com.whatsapp", "com.android.chrome"))

        assertNull(warning)
        assertEquals(
            listOf(listOf("com.android.camera") to false, listOf("com.android.chrome", "com.whatsapp") to true),
            gw.calls,
        )
    }

    @Test fun applyNothingBlockedLiftsEverySuspension() {
        val catalog = FakeCatalog(launchable, suspended = setOf("com.whatsapp"), protected = protected)
        val gw = FakeGateway()
        AppSuspender(catalog, gw).apply(PhoneAppPolicy.lifted(7))
        assertEquals(listOf(listOf("com.whatsapp") to false), gw.calls)
    }

    @Test fun applyWithNothingToChangeCallsNothing() {
        val gw = FakeGateway()
        AppSuspender(FakeCatalog(launchable, emptySet(), protected), gw).apply(PhoneAppPolicy.lifted(7))
        assertTrue(gw.calls.isEmpty())
    }

    @Test fun applySaysHowManyAppsAndroidRefused() {
        val gw = FakeGateway(refused = setOf("com.whatsapp", "com.android.camera"))
        val warning = AppSuspender(FakeCatalog(launchable, emptySet(), protected), gw).apply(policy(AppBlockMode.ALLOW_ONLY))
        assertEquals("2 app(s) não puderam ser bloqueados", warning)
    }

    @Test fun aPhoneThatIsNotTheSchoolsBlocksNothingAndSaysSo() {
        val gw = FakeGateway(owner = false)
        val warning = AppSuspender(FakeCatalog(launchable, emptySet(), protected), gw).apply(policy(AppBlockMode.ALLOW_ONLY))
        assertEquals("este celular não bloqueia apps", warning)
        assertTrue(gw.calls.isEmpty())
    }

    @Test fun aRefusalFromAndroidIsAWarningNotACrash() {
        val gw = object : DevicePolicyGateway by FakeGateway() {
            override fun setPackagesSuspended(pkgs: List<String>, suspended: Boolean): List<String> =
                throw SecurityException("refused")
        }
        val warning = AppSuspender(FakeCatalog(launchable, emptySet(), protected), gw).apply(policy(AppBlockMode.ALLOW_ONLY))
        assertEquals("os apps não puderam ser bloqueados", warning)
    }
}
