package io.github.arnoneto1.midview.agent.apps

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class PreinstalledTest {
    // The Redmi's system: built 2025-02-06.
    private val built = 1_738_842_605_000L

    @Test fun aSystemAppCameWithThePhone() {
        assertTrue(Preinstalled.cameWithPhone(system = true, firstInstallTime = 1_800_000_000_000L, buildTime = built))
    }

    // MIUI's Notes and Calculator: dated 1974, before the system was built.
    @Test fun aMakersDataAppCameWithThePhone() {
        assertTrue(Preinstalled.cameWithPhone(system = false, firstInstallTime = 137_000_000_000L, buildTime = built))
        assertTrue(Preinstalled.cameWithPhone(system = false, firstInstallTime = -1_000L, buildTime = built))
    }

    // Installed later (by the phone's setup, the store or the teacher): it can go.
    @Test fun anAppInstalledLaterDidNot() {
        assertFalse(Preinstalled.cameWithPhone(system = false, firstInstallTime = 1_794_400_000_000L, buildTime = built))
    }
}
