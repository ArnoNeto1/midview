package io.github.arnoneto1.midview.agent.home

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

// Launcher3Home against a fake Launcher3 (the school's Multilaser M10), the
// same cases as the cable's PhoneLauncher3HomeTests: the home pages emptied,
// the class's apps in the dock, "Adicionar ícone à tela inicial" and
// "Mostrar Google App" off, and anything unexpected ending it.
class Launcher3HomeTest {
    private fun run(launcher: FakeLauncher3): Boolean = Launcher3Home(launcher).run()

    private fun drags(launcher: FakeLauncher3) = launcher.commands.filter { it.startsWith("drag ") }

    // The drags to the top of the screen, where "Remover" shows.
    private fun removals(launcher: FakeLauncher3) = drags(launcher).filter { it.endsWith(" 64") }

    @Test fun aMessyTabletEndsWithEmptyPagesTheClassDockAndBothSettingsOff() {
        val launcher = FakeLauncher3().apply {
            pages = mutableListOf(
                mutableListOf(FakeLauncher3.Item("Contatos"), FakeLauncher3.Item("Google", folder = true), FakeLauncher3.Item("Dinossauro do Chrome", widget = true)),
                mutableListOf(FakeLauncher3.Item("Planilhas")),
            )
            dock = mutableListOf("Play Store", "Chrome", "Fotos", "Relógio")
        }

        assertTrue(run(launcher))

        assertTrue(launcher.empty)
        assertEquals(FakeLauncher3.CLASS_APPS, launcher.dock)
        assertFalse(launcher.addIcon)
        assertFalse(launcher.googleApp)
        assertTrue(launcher.uninstallAsked.isEmpty())
        assertTrue(launcher.elapsed < Launcher3Home.LIMIT_MILLIS)
        // The settings first, before anything is dragged; the date is never dragged.
        assertTrue(launcher.commands.indexOf("settings") < launcher.commands.indexOfFirst { it.startsWith("drag ") })
        assertTrue(drags(launcher).none { it.startsWith("drag 400 211 ") })
        // Four on the pages, three in the dock: Chrome stays once it leads it.
        assertEquals(7, removals(launcher).size)
        assertEquals(4, drags(launcher).count { it.endsWith(" 680 1115") })
        assertEquals("home", launcher.commands.last())
    }

    @Test fun aDockThatStartsWithTheClassAppsKeepsThem() {
        val launcher = FakeLauncher3().apply { dock = mutableListOf("Chrome", "Câmera", "Relógio") }

        assertTrue(run(launcher))

        assertEquals(FakeLauncher3.CLASS_APPS, launcher.dock)
        // Only Relógio taken off (the third in the dock); Fotos, Files and Calculadora dragged in.
        assertEquals(listOf("drag 400 1115 400 64"), removals(launcher))
        assertEquals(3, drags(launcher).count { it.endsWith(" 680 1115") })
    }

    @Test fun anArrangedTabletIsOnlyRead() {
        val launcher = FakeLauncher3().apply {
            dock = FakeLauncher3.CLASS_APPS.toMutableList()
            addIcon = false
            googleApp = false
        }

        assertTrue(run(launcher))

        assertTrue(drags(launcher).isEmpty())
        assertTrue(launcher.commands.none { it.startsWith("tap ") })
    }

    @Test fun anAppTheStudentInstalledGoesToRemoverOnTheLeftNeverToDesinstalar() {
        val launcher = FakeLauncher3().apply {
            pages = mutableListOf(mutableListOf(FakeLauncher3.Item("Jogo", userApp = true)))
            dock = FakeLauncher3.CLASS_APPS.toMutableList()
        }

        assertTrue(run(launcher))

        assertTrue(launcher.empty)
        assertTrue(launcher.uninstallAsked.isEmpty())
        // Aimed at the middle first (where "Remover" shows alone), then at the left quarter.
        assertEquals(listOf("drag 144 375 400 64", "drag 144 375 200 64"), removals(launcher))
        assertTrue(drags(launcher).none { it.contains(" 600 64") })
    }

    @Test fun theUninstallQuestionIsNeverAnsweredAndEndsIt() {
        val launcher = FakeLauncher3().apply {
            pages = mutableListOf(mutableListOf(FakeLauncher3.Item("Jogo", uninstallOnly = true)))
            dock = FakeLauncher3.CLASS_APPS.toMutableList()
        }

        assertFalse(run(launcher))

        // Closed with Back right after each drop, and no more than twice.
        assertTrue(launcher.uninstallAsked.size in 1..2)
        val commands = launcher.commands
        val dragged = commands.indices.filter { commands[it].startsWith("drag ") }
        for (i in dragged) {
            assertEquals("back", commands.drop(i + 1).first { it != "read" })
        }
        assertTrue(commands.drop(dragged.first()).none { it.startsWith("tap ") })
        assertEquals(1, launcher.pages[0].size)
    }

    @Test fun onlyTheClassAppsTheStudentCanOpenGoInTheDock() {
        val launcher = FakeLauncher3()
        launcher.launchable -= "com.google.android.apps.photos"
        launcher.launchable -= "com.google.android.apps.nbu.files"

        assertTrue(run(launcher))

        assertEquals(listOf("Chrome", "Câmera", "Calculadora"), launcher.dock)
    }

    @Test fun anAppFurtherDownTheDrawerIsScrolledTo() {
        val launcher = FakeLauncher3()
        launcher.drawerApps = (1..40).map { "App %02d".format(it) } + FakeLauncher3.CLASS_APPS

        assertTrue(run(launcher))

        assertEquals(FakeLauncher3.CLASS_APPS, launcher.dock)
        assertTrue(launcher.commands.contains("swipe 400 1040 400 268 500"))
    }

    @Test fun aLauncherWithoutTheGoogleSwitchNeedsOnlyTheOther() {
        val launcher = FakeLauncher3().apply { hasGoogleRow = false }

        assertTrue(run(launcher))

        assertFalse(launcher.addIcon)
    }

    @Test fun aSwitchThatStaysOnEndsItBeforeAnythingIsDragged() {
        val launcher = FakeLauncher3().apply {
            switchesStuck = true
            pages = mutableListOf(mutableListOf(FakeLauncher3.Item("Contatos")))
        }

        assertFalse(run(launcher))

        assertTrue(launcher.addIcon)
        assertTrue(drags(launcher).isEmpty())
        assertEquals("home", launcher.commands.last())
    }

    @Test fun settingsThatDoNotOpenStopItBeforeAnyTap() {
        val launcher = FakeLauncher3().apply { startFails = true }

        assertFalse(run(launcher))

        assertTrue(launcher.commands.none { it.startsWith("tap ") })
        assertTrue(launcher.commands.none { it == "read" })
    }

    @Test fun unreadableScreensEndItInTime() {
        val launcher = FakeLauncher3().apply { readsFail = true }

        assertFalse(run(launcher))

        assertTrue(launcher.elapsed <= Launcher3Home.LIMIT_MILLIS)
        assertTrue(drags(launcher).isEmpty())
    }

    // Every screen read slow (3 s, as measured over the cable): a tablet full
    // of items is cut off at the limit, never much past it.
    @Test fun aSlowTabletStopsAtTheLimit() {
        val launcher = FakeLauncher3().apply {
            readMillis = 60_000L
            pages = mutableListOf(MutableList(20) { FakeLauncher3.Item("App $it") })
        }

        assertFalse(run(launcher))

        assertTrue(launcher.elapsed < Launcher3Home.LIMIT_MILLIS + 2 * 60_000L)
        assertFalse(launcher.empty)
    }

    @Test fun theClassAppsAreTheTablets() {
        assertEquals(
            listOf("com.android.chrome", "com.android.camera2", "com.google.android.apps.photos", "com.google.android.apps.nbu.files", "com.google.android.calculator"),
            Launcher3Home.DOCK_APPS.map { it.pkg },
        )
    }

    @Test fun namesAreComparedWithoutCaseOrAccents() {
        assertEquals("camera", HomeNode.plain(" Câmera "))
        assertEquals("adicionar icone a tela inicial", HomeNode.plain("Adicionar ícone à tela inicial"))
    }
}
