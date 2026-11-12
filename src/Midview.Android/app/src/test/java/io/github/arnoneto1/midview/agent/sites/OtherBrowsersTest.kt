package io.github.arnoneto1.midview.agent.sites

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

class OtherBrowsersTest {
    @get:Rule val folder = TemporaryFolder()

    private val own = "io.github.arnoneto1.midview.agent"
    private val chrome = "com.android.chrome"
    private val protected = setOf(own, "com.google.android.webview", "com.android.settings")

    private class FakeBrowsers(
        var browsers: Set<String>,
        val protected: Set<String>,
        var chromeInstalled: Boolean = true,
    ) : BrowserCatalog {
        override fun browsers() = browsers
        override fun protected() = protected
        override fun isChromeInstalled() = chromeInstalled
    }

    private val gw = FakeSiteGateway()
    private val file by lazy { File(folder.root, "hidden-browsers.txt") }

    @Test fun everyBrowserButChromeIsHidden() {
        assertEquals(
            setOf("org.mozilla.firefox", "com.mi.globalbrowser", "com.chrome.beta"),
            OtherBrowsers.toHide(
                setOf(chrome, "org.mozilla.firefox", "com.mi.globalbrowser", "com.chrome.beta"),
                chromeInstalled = true,
                protected = protected,
            ),
        )
    }

    @Test fun neverTheAgentTheWebViewNorAProtectedApp() {
        assertEquals(
            emptySet<String>(),
            OtherBrowsers.toHide(setOf(own, "com.google.android.webview", "com.android.settings", chrome), true, protected),
        )
    }

    // Without Chrome, hiding the others would leave the phone with no
    // browser at all.
    @Test fun withoutChromeNothingIsHidden() {
        assertEquals(emptySet<String>(), OtherBrowsers.toHide(setOf("org.mozilla.firefox"), chromeInstalled = false, protected))
    }

    @Test fun aHiddenAppThatBecameProtectedComesBack() {
        assertEquals(
            setOf("com.google.android.webview"),
            OtherBrowsers.toUnhide(setOf("org.mozilla.firefox", "com.google.android.webview"), protected),
        )
        assertEquals(setOf(chrome), OtherBrowsers.toUnhide(setOf(chrome), protected))
    }

    @Test fun applyHidesAndRecordsWhatItHid() {
        val catalog = FakeBrowsers(setOf(chrome, "org.mozilla.firefox", "com.opera.browser"), protected)
        OtherBrowsers(catalog, gw, file).apply()
        assertEquals(setOf("org.mozilla.firefox", "com.opera.browser"), gw.hidden)
        assertEquals(setOf("org.mozilla.firefox", "com.opera.browser"), OtherBrowsers.load(file))
    }

    @Test fun aHiddenBrowserIsNotSeenAgainAndStaysRecorded() {
        val catalog = FakeBrowsers(setOf(chrome, "org.mozilla.firefox"), protected)
        OtherBrowsers(catalog, gw, file).apply()
        // Android no longer lists a hidden app.
        catalog.browsers = setOf(chrome, "com.opera.browser")
        OtherBrowsers(catalog, gw, file).apply()
        assertEquals(setOf("org.mozilla.firefox", "com.opera.browser"), OtherBrowsers.load(file))
    }

    @Test fun oneAndroidRefusesIsNotRecorded() {
        gw.refuseHiding = setOf("com.opera.browser")
        OtherBrowsers(FakeBrowsers(setOf("org.mozilla.firefox", "com.opera.browser"), protected), gw, file).apply()
        assertEquals(setOf("org.mozilla.firefox"), OtherBrowsers.load(file))
    }

    @Test fun aRecordedAppNowProtectedIsShownAgain() {
        file.writeText("com.google.android.webview\norg.mozilla.firefox\n")
        OtherBrowsers(FakeBrowsers(setOf(chrome), protected), gw, file).apply()
        assertEquals(listOf("com.google.android.webview" to false), gw.hiddenCalls)
        assertEquals(setOf("org.mozilla.firefox"), OtherBrowsers.load(file))
    }

    @Test fun notTheDeviceOwnerDoesNothing() {
        gw.owner = false
        OtherBrowsers(FakeBrowsers(setOf("org.mozilla.firefox"), protected), gw, file).apply()
        assertTrue(gw.hiddenCalls.isEmpty())
    }
}
