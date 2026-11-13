package io.github.arnoneto1.midview.agent.sites

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

class SitePolicyTest {
    @get:Rule val folder = TemporaryFolder()

    private val own = "io.github.arnoneto1.midview.agent"
    private val gw = FakeSiteGateway()
    private val vpn = mutableListOf<String>()
    private val policy by lazy {
        val internet = InternetBlock(gw, File(folder.root, "internet-block.txt"), { vpn += "start" }, { vpn += "stop" }, own)
        SitePolicy(gw, internet)
    }

    @Test fun sitesGoToChromeAndTheInternetStaysFree() {
        assertTrue(policy.apply(SiteBlockState(setOf("youtube.com"), false)))
        assertArrayEquals(arrayOf("www.youtube.com", "youtube.com"), gw.restrictions["com.android.chrome"]!!["URLBlocklist"] as Array<*>)
        assertEquals(null, gw.alwaysOn)
    }

    @Test fun theBlockedInternetBlocksChromeAndThePhone() {
        assertTrue(policy.apply(SiteBlockState(emptySet(), true)))
        assertArrayEquals(arrayOf("*"), gw.restrictions["com.android.chrome"]!!["URLBlocklist"] as Array<*>)
        assertEquals(AlwaysOnVpn(own, true), gw.alwaysOn)
        assertEquals(listOf("start"), vpn)
    }

    @Test fun nothingBlockedClearsChromesBlock() {
        policy.apply(SiteBlockState(setOf("youtube.com"), true))
        assertTrue(policy.apply(SiteBlockState.EMPTY))
        assertEquals(mapOf<String, Any>("TosDialogBehavior" to 2), gw.restrictions["com.android.chrome"])
        assertEquals(null, gw.alwaysOn)
    }

    @Test fun aPhoneThatIsNotTheDeviceOwnerCannotBlock() {
        gw.owner = false
        assertFalse(policy.apply(SiteBlockState(setOf("youtube.com"), false)))
    }

    @Test fun aRefusedInternetBlockIsAFailureButChromeIsStillSet() {
        gw.refuseAlwaysOn = true
        assertFalse(policy.apply(SiteBlockState(emptySet(), true)))
        assertArrayEquals(arrayOf("*"), gw.restrictions["com.android.chrome"]!!["URLBlocklist"] as Array<*>)
    }

    // The site block and the lab panel's download block share Chrome's
    // configuration: each change keeps the other.
    @Test fun theDownloadBlockAndTheSiteBlockKeepEachOther() {
        val chrome = ChromeConfig(gw, downloadsBlocked = true)
        val internet = InternetBlock(gw, File(folder.root, "internet-block.txt"), { vpn += "start" }, { vpn += "stop" }, own)
        assertTrue(SitePolicy(chrome, internet).apply(SiteBlockState(setOf("youtube.com"), false)))
        assertEquals(3, gw.restrictions["com.android.chrome"]!!["DownloadRestrictions"])
        assertArrayEquals(arrayOf("www.youtube.com", "youtube.com"), gw.restrictions["com.android.chrome"]!!["URLBlocklist"] as Array<*>)

        assertTrue(chrome.setDownloadsBlocked(false))
        assertEquals(setOf("TosDialogBehavior", "URLBlocklist"), gw.restrictions["com.android.chrome"]!!.keys)

        assertTrue(chrome.setSites(SiteBlockState.EMPTY))
        assertEquals(mapOf<String, Any>("TosDialogBehavior" to 2), gw.restrictions["com.android.chrome"])
    }

    @Test fun aRefusedChangeIsNotKept() {
        val chrome = ChromeConfig(gw)
        chrome.setSites(SiteBlockState.EMPTY)
        gw.owner = false
        assertFalse(chrome.setDownloadsBlocked(true))
        gw.owner = true
        assertTrue(chrome.setSites(SiteBlockState(setOf("youtube.com"), false)))
        assertEquals(setOf("TosDialogBehavior", "URLBlocklist"), gw.restrictions["com.android.chrome"]!!.keys)
    }

    // The agent starting: the site block's write was refused. A later
    // "Travar downloads" still writes the blocked sites with it.
    @Test fun aSiteBlockChromeRefusedAtStartStillGoesWithTheDownloadBlock() {
        val chrome = ChromeConfig(gw)
        gw.owner = false
        assertFalse(chrome.setSites(SiteBlockState(setOf("youtube.com"), false)))
        gw.owner = true
        assertTrue(chrome.setDownloadsBlocked(true))
        assertEquals(3, gw.restrictions["com.android.chrome"]!!["DownloadRestrictions"])
        assertArrayEquals(arrayOf("www.youtube.com", "youtube.com"), gw.restrictions["com.android.chrome"]!!["URLBlocklist"] as Array<*>)
    }

    // Before the site block is known, the download block writes nothing.
    @Test fun theDownloadBlockWaitsForTheSiteBlock() {
        val chrome = ChromeConfig(gw)
        assertFalse(chrome.setDownloadsBlocked(true))
        assertEquals(null, gw.restrictions["com.android.chrome"])
    }
}
