package io.github.arnoneto1.midview.agent.programs

import io.github.arnoneto1.midview.agent.protocol.TextMessage
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class TeacherLaunchTest {
    private val opened = mutableListOf<String>()
    private val states = mutableMapOf(TeacherLaunch.CHROME to AppState.READY, CALCULATOR to AppState.READY)
    private var failOpening = false
    private var locked = false
    private var internetBlocked = false
    private var blockedSites = emptySet<String>()
    private val delays = mutableListOf<Long>()
    private val tasks = mutableListOf<() -> Unit>()

    private val screen = object : LaunchScreen {
        override fun state(pkg: String) = states[pkg] ?: AppState.MISSING

        override fun open(pkg: String) {
            if (failOpening) throw IllegalStateException("sem tela")
            opened += "app $pkg"
        }

        override fun openSite(address: String) {
            if (failOpening) throw IllegalStateException("recusado")
            opened += "site $address"
        }
    }

    private val launch = TeacherLaunch(
        screen, SELF, { locked }, { internetBlocked }, { blockedSites },
        later = { delay, task -> delays += delay; tasks += task },
    )

    private fun site(text: String) = launch.openWebsite(TextMessage.encode(text))

    private fun app(pkg: String) = launch.openApp(TextMessage.encode(pkg))

    // The lock screen goes: what waited opens, on the scheduled task.
    private fun unlock() {
        locked = false
        launch.unlocked()
        tasks.removeAt(0)()
    }

    @Test fun aSiteOpensInChrome() {
        assertEquals(true to TeacherLaunch.OPENED_SITE, site("https://www.google.com/"))
        assertEquals(listOf("site https://www.google.com/"), opened)
    }

    @Test fun severalSitesOpenEachInTurn() {
        site("https://www.google.com/")
        site("https://pt.wikipedia.org/wiki/Brasil")
        assertEquals(listOf("site https://www.google.com/", "site https://pt.wikipedia.org/wiki/Brasil"), opened)
    }

    @Test fun onlyWebsitesOpen() {
        for (text in listOf("file:///sdcard/a.apk", "intent://x#Intent;end", "javascript:alert(1)", "content://x/y",
            "www.google.com", "https://", "https:///x", "https://a b.com/", "", "mailto:a@b.com", "market://details?id=x")) {
            assertEquals(text, false to TeacherLaunch.NOT_A_SITE, site(text))
        }
        assertEquals(false to TeacherLaunch.NOT_A_SITE, launch.openWebsite(byteArrayOf(0xC3.toByte(), 0x28)))
        assertTrue(opened.isEmpty())
    }

    @Test fun theTeacherIsToldOfTheInternetBlockAndOfABlockedSite() {
        blockedSites = setOf("youtube.com")
        assertEquals(true to TeacherLaunch.OPENED_BLOCKED, site("https://www.youtube.com/watch?v=1"))
        assertEquals(true to TeacherLaunch.OPENED_BLOCKED, site("https://youtube.com/"))
        assertEquals(true to TeacherLaunch.OPENED_SITE, site("https://notyoutube.com/"))
        internetBlocked = true
        assertEquals(true to TeacherLaunch.OPENED_NO_INTERNET, site("https://www.google.com/"))
        assertEquals(4, opened.size)
    }

    @Test fun withoutChromeOrWithChromeBlockedNoSiteOpens() {
        states[TeacherLaunch.CHROME] = AppState.BLOCKED
        assertEquals(false to TeacherLaunch.CHROME_BLOCKED, site("https://www.google.com/"))
        states[TeacherLaunch.CHROME] = AppState.MISSING
        assertEquals(false to TeacherLaunch.NO_CHROME, site("https://www.google.com/"))
        assertTrue(opened.isEmpty())
    }

    @Test fun chromeRefusingIsSaid() {
        failOpening = true
        assertEquals(false to "o Chrome não abriu: recusado", site("https://www.google.com/"))
    }

    @Test fun underTheLockScreenSitesAndAppsWaitThenOpenInOrder() {
        locked = true
        assertEquals(true to TeacherLaunch.SITE_AFTER_LOCK, site("https://a.com/"))
        assertEquals(true to TeacherLaunch.APP_AFTER_LOCK, app(CALCULATOR))
        assertEquals(true to TeacherLaunch.SITE_AFTER_LOCK, site("https://b.com/"))
        assertTrue(opened.isEmpty())
        assertEquals(3, launch.waitingCount)

        unlock()
        assertEquals(listOf(TeacherLaunch.AFTER_UNLOCK_MILLIS), delays)
        assertEquals(listOf("site https://a.com/", "app $CALCULATOR", "site https://b.com/"), opened)
        assertEquals(0, launch.waitingCount)
    }

    @Test fun lockedAgainBeforeTheyOpenTheyKeepWaiting() {
        locked = true
        site("https://a.com/")
        launch.unlocked()
        // Locked again before the moment after the unlock.
        tasks.removeAt(0)()
        assertTrue(opened.isEmpty())
        unlock()
        assertEquals(listOf("site https://a.com/"), opened)
    }

    @Test fun nothingWaitsPastTheLimit() {
        locked = true
        repeat(TeacherLaunch.MAX_WAITING) { site("https://a$it.com/") }
        assertEquals(false to TeacherLaunch.TOO_MANY_WAITING, site("https://z.com/"))
    }

    @Test fun anAppOpensByItsPackage() {
        assertEquals(true to TeacherLaunch.OPENED_APP, app(CALCULATOR))
        assertEquals(listOf("app $CALCULATOR"), opened)
    }

    @Test fun aBlockedMissingOrInvalidAppIsSaidAndNotOpened() {
        states[CALCULATOR] = AppState.BLOCKED
        assertEquals(false to TeacherLaunch.BLOCKED, app(CALCULATOR))
        assertEquals(false to TeacherLaunch.MISSING, app("com.nao.existe"))
        assertEquals(false to TeacherLaunch.NOT_AN_APP, app("calc.exe /x"))
        assertEquals(false to TeacherLaunch.SELF, app(SELF))
        // Said at once even under the lock screen: nothing waits for it.
        locked = true
        assertEquals(false to TeacherLaunch.BLOCKED, app(CALCULATOR))
        assertEquals(0, launch.waitingCount)
        assertTrue(opened.isEmpty())
    }

    @Test fun anAppBlockedWhileItWaitedDoesNotOpen() {
        locked = true
        app(CALCULATOR)
        states[CALCULATOR] = AppState.BLOCKED
        unlock()
        assertTrue(opened.isEmpty())
    }

    @Test fun anAppThatDoesNotStartIsSaid() {
        failOpening = true
        assertEquals(false to "não abriu: sem tela", app(CALCULATOR))
    }

    @Test fun websiteCheck() {
        assertEquals("https://www.google.com/search?q=a%20b", WebsiteCheck.address("https://www.google.com/search?q=a%20b"))
        assertEquals("http://localhost:3000/", WebsiteCheck.address("http://localhost:3000/"))
        assertNull(WebsiteCheck.address(null))
        assertNull(WebsiteCheck.address("https://a.com/\nx"))
        assertNull(WebsiteCheck.address("https://a.com/" + "x".repeat(TextMessage.MAX_LENGTH)))
        assertFalse(WebsiteCheck.isBlocked("https://a.com/", emptySet()))
        assertTrue(WebsiteCheck.isBlocked("https://m.youtube.com/", setOf("youtube.com")))
        assertFalse(WebsiteCheck.isBlocked("https://youtube.com.br/", setOf("youtube.com")))
    }

    private companion object {
        const val SELF = "io.github.arnoneto1.midview.agent"
        const val CALCULATOR = "com.miui.calculator"
    }
}
