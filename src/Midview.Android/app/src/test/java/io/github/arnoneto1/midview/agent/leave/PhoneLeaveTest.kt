package io.github.arnoneto1.midview.agent.leave

import io.github.arnoneto1.midview.agent.optimize.PrivateDns
import io.github.arnoneto1.midview.agent.policy.Lockdown
import io.github.arnoneto1.midview.agent.sites.AlwaysOnVpn
import io.github.arnoneto1.midview.agent.sites.Previous
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

// "Desfazer e tirar o Midview": which steps, in which order, run again
// safely, go on after the process died midway, and which apps come back.
class PhoneLeaveTest {
    @get:Rule val folder = TemporaryFolder()

    private val gw = FakeLeaveGateway()
    private val file by lazy { File(folder.root, "leaving.txt") }

    private fun store() = LeaveStore(file)

    private fun leave(store: LeaveStore = store()) = PhoneLeave(gw, store)

    private fun firstOf(name: String) = gw.calls.indexOf(name).also { assertTrue("$name was never called", it >= 0) }

    @Test fun startUndoesEveryLockAndKeepsTheTeachersForTheAnswer() {
        val report = leave().start()

        assertTrue(gw.undone())
        assertTrue(report.owner)
        assertEquals(emptyList<LeaveStep>(), report.failed)
        assertEquals(2, report.unsuspended)
        assertEquals(3, report.unhidden)
        assertEquals(3, report.restored)
        assertEquals(1, report.notRestored)
        // The connection the teacher waits on still trusts it; nothing is uninstalled yet.
        assertTrue(gw.teachers)
        assertTrue(gw.owner)
        // The teachers are kept: they can still erase the phone or try again.
        assertTrue(gw.teachers)
        assertFalse("forgetTeachers" in gw.calls)
        assertEquals(0, gw.uninstallAsked)
        assertEquals(LeaveStore.Phase.UNDONE, store().phase)
    }

    @Test fun theRestrictionsGoFirstAndTheSystemAppsComeBackLast() {
        val leave = leave()
        leave.start()
        leave.finish()

        assertEquals("clearRestriction", gw.calls.first { it != "restrictions" })
        assertTrue(firstOf("clearRestriction") < firstOf("endLockTask"))
        assertTrue(firstOf("unhide") < firstOf("installExisting"))
        assertTrue(firstOf("removeScreenPin") < firstOf("installExisting"))
        // Everything a Device Owner alone can do, before it stops being one;
        // the teachers only once it is no longer one.
        assertTrue(gw.calls.lastIndexOf("installExisting") < firstOf("clearDeviceOwner"))
        assertTrue(firstOf("clearDeviceOwner") < firstOf("forgetTeachers"))
        assertTrue(firstOf("forgetTeachers") < firstOf("stopAgent"))
        assertEquals("askUninstall", gw.calls.last())
    }

    @Test fun finishForgetsTheTeachersGivesUpTheOwnerAndAsksTheUninstall() {
        val leave = leave()
        leave.start()
        leave.finish()

        assertFalse(gw.teachers)
        assertFalse(gw.owner)
        assertFalse(gw.agentRunning)
        assertEquals(1, gw.uninstallAsked)
        assertEquals(LeaveStore.Phase.OWNER_CLEARED, store().phase)
    }

    // Every restriction the agent ever sets goes, and any other its admin
    // holds; a restriction from nobody is harmless to lift.
    @Test fun everyRestrictionOfTheAdminAndEveryKnownOneIsLifted() {
        leave().start()

        assertTrue(gw.restrictions.isEmpty())
        val lifted = gw.calls.count { it == "clearRestriction" }
        assertEquals((PhoneLeave.KNOWN_RESTRICTIONS + "no_from_somewhere_else").size, lifted)
        assertTrue("no_control_apps" in PhoneLeave.KNOWN_RESTRICTIONS)
        assertTrue("no_change_wifi_state" in PhoneLeave.KNOWN_RESTRICTIONS)
        assertTrue(Lockdown.RESTRICTIONS.all { it in PhoneLeave.KNOWN_RESTRICTIONS })
    }

    @Test fun runningItAgainChangesNothing() {
        val leave = leave()
        leave.start()
        gw.calls.clear()

        val again = leave.start()

        assertTrue(gw.undone())
        assertEquals(0, again.unsuspended)
        assertEquals(0, again.unhidden)
        assertEquals(0, again.restored)
        // Only what never comes back is asked again.
        assertEquals(1, gw.calls.count { it == "installExisting" })
        assertFalse("unsuspend" in gw.calls)
        assertFalse("unhide" in gw.calls)
        assertFalse("setAlwaysOnVpn" in gw.calls)
        assertFalse("setPrivateDnsAutomatic" in gw.calls)
    }

    // The record is kept before anything changes: a phone that restarts
    // midway finishes leaving at its next start, never locked again.
    @Test fun aProcessThatDiedMidwayFinishesAtTheNextStart() {
        gw.dieAt = "unhide"
        try {
            leave().start()
            fail("the process should have died")
        } catch (_: FakeLeaveGateway.Died) {
        }
        assertEquals(LeaveStore.Phase.STARTED, store().phase)
        assertTrue(gw.hidden.isNotEmpty())

        val next = leave()
        assertTrue(next.isActive)
        assertTrue(next.resume())

        assertTrue(gw.undone())
        assertFalse(gw.teachers)
        assertFalse(gw.owner)
        assertEquals(1, gw.uninstallAsked)
        assertEquals(LeaveStore.Phase.OWNER_CLEARED, store().phase)
    }

    @Test fun dyingRightAfterGivingUpTheOwnerOnlyAsksTheUninstallAgain() {
        gw.dieAt = "stopAgent"
        val leave = leave()
        leave.start()
        try {
            leave.finish()
            fail("the process should have died")
        } catch (_: FakeLeaveGateway.Died) {
        }
        gw.calls.clear()

        assertTrue(leave().resume())

        assertEquals(listOf("forgetTeachers", "stopAgent", "askUninstall"), gw.calls)
        assertEquals(1, gw.uninstallAsked)
    }

    // The owner given up, the process died before the teachers were
    // forgotten: they are at the next start.
    @Test fun dyingBeforeForgettingTheTeachersForgetsThemAtTheNextStart() {
        gw.dieAt = "forgetTeachers"
        val leave = leave()
        leave.start()
        try {
            leave.finish()
            fail("the process should have died")
        } catch (_: FakeLeaveGateway.Died) {
        }
        assertTrue(gw.teachers)
        assertEquals(LeaveStore.Phase.OWNER_CLEARED, store().phase)

        assertTrue(leave().resume())

        assertFalse(gw.teachers)
        assertEquals(1, gw.uninstallAsked)
    }

    @Test fun withNothingKeptThereIsNothingToResume() {
        val leave = leave()

        assertFalse(leave.isActive)
        assertFalse(leave.resume())
        assertTrue(gw.calls.isEmpty())
        assertNull(store().phase)
    }

    // The teacher's leaving in this process is finished by the command itself:
    // the watchdog's resume leaves it alone until then.
    @Test fun aResumeWaitsForTheLeavingThisProcessRuns() {
        val leave = leave()
        leave.start()
        gw.calls.clear()

        assertFalse(leave.resume())
        assertTrue(gw.calls.isEmpty())
        assertTrue(gw.teachers)

        leave.finish()
        gw.calls.clear()
        assertTrue(leave.resume())
        assertEquals(listOf("forgetTeachers", "stopAgent", "askUninstall"), gw.calls)
    }

    @Test fun aRefusedStepNeverStopsTheOthersAndIsSaid() {
        gw.refuse = setOf("removeScreenPin", "clearWallpaper")

        val report = leave().start()

        assertEquals(listOf(LeaveStep.SCREEN_PIN, LeaveStep.WALLPAPER), report.failed)
        assertFalse(report.complete)
        assertTrue(gw.restrictions.isEmpty())
        assertEquals(3, report.restored)
        assertEquals(
            "Não deu para desfazer o PIN da tela, o papel de parede. " +
                "O Midview continua no celular: tente de novo ou use Restaurar de fábrica.",
            report.text(),
        )
    }

    // A lock that stays keeps the Midview: still the owner, still trusting
    // the teachers (who can erase the phone or try again); a start of the
    // agent tries once, never in a loop, and a teacher's new ask goes on.
    @Test fun aLockThatStaysKeepsTheMidviewUntilATeacherAsksAgain() {
        gw.refuse = setOf("removeScreenPin")
        leave().start()
        val next = leave()

        assertFalse(next.resume())
        gw.calls.clear()
        assertFalse(next.resume())
        assertTrue(gw.calls.isEmpty())
        assertTrue(gw.owner)
        assertTrue(gw.teachers)
        assertEquals(0, gw.uninstallAsked)
        assertTrue(next.isActive)
        assertFalse(next.ownerGivenUp)

        gw.refuse = emptySet()
        assertTrue(next.start().complete)
        next.finish()
        assertFalse(gw.owner)
        assertTrue(next.ownerGivenUp)
        assertEquals(1, gw.uninstallAsked)
    }

    // Android keeping the Midview as the Device Owner: nothing is uninstalled,
    // and the next start tries again.
    @Test fun anOwnerAndroidKeepsIsTriedAgain() {
        gw.refuse = setOf("clearDeviceOwner")
        val leave = leave()
        leave.start()
        leave.finish()

        assertTrue(gw.owner)
        assertEquals(0, gw.uninstallAsked)
        assertEquals(LeaveStore.Phase.UNDONE, store().phase)

        gw.refuse = emptySet()
        assertTrue(leave().resume())
        assertFalse(gw.owner)
        assertFalse(gw.teachers)
        assertEquals(1, gw.uninstallAsked)
    }

    // A phone paired by code was never locked: the teachers are forgotten
    // and the uninstall asked, nothing else.
    @Test fun aPhoneThatIsNotTheOwnerOnlyForgetsAndAsksTheUninstall() {
        gw.owner = false
        val leave = leave()
        val report = leave.start()
        leave.finish()

        assertFalse(report.owner)
        assertEquals(LeaveReport.NOT_OWNER, report.text())
        assertEquals(listOf("forgetTeachers", "stopAgent", "askUninstall"), gw.calls)
    }

    @Test fun theVpnFromBeforeTheBlockComesBack() {
        gw.beforeBlock = Previous(AlwaysOnVpn("com.work.vpn", lockdown = false))

        leave().start()

        assertEquals(AlwaysOnVpn("com.work.vpn", lockdown = false), gw.vpn)
        assertFalse(gw.vpnRunning)
    }

    @Test fun anotherVpnAndAnotherDnsAreLeftAlone() {
        gw.vpn = AlwaysOnVpn("com.work.vpn", lockdown = true)
        gw.dns = PrivateDns(PrivateDns.MODE_PROVIDER_HOSTNAME, "dns.example.org")

        leave().start()

        assertEquals(AlwaysOnVpn("com.work.vpn", lockdown = true), gw.vpn)
        assertEquals("dns.example.org", gw.dns.host)
        assertFalse("setAlwaysOnVpn" in gw.calls)
        assertFalse("setPrivateDnsAutomatic" in gw.calls)
    }

    @Test fun theAppsUninstalledForTheUserComeBackNeverTheAgent() {
        val packages = listOf(
            KnownPackage("com.android.chrome", installed = true),
            KnownPackage("com.longcheertel.cit", installed = false),
            KnownPackage(Lockdown.PACKAGE, installed = false),
            KnownPackage("com.android.egg", installed = false),
            KnownPackage("com.android.egg", installed = false),
        )

        assertEquals(listOf("com.android.egg", "com.longcheertel.cit"), PhoneLeave.toRestore(packages, Lockdown.PACKAGE))
    }

    @Test fun theReportSaysWhatWasUndone() {
        val text = LeaveReport(true, unsuspended = 1, unhidden = 22, restored = 70, notRestored = 2).text()

        assertEquals(
            "Midview saindo: travas e ajustes da escola desfeitos, 1 app desbloqueado, 22 apps mostrados de novo, " +
                "70 apps do sistema de volta. 2 apps do sistema não voltaram. O celular pede para confirmar a desinstalação na tela.",
            text,
        )
    }
}
