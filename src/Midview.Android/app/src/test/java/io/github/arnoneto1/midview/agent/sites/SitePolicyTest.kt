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

    @Test fun nothingBlockedClearsChromesPolicy() {
        policy.apply(SiteBlockState(setOf("youtube.com"), true))
        assertTrue(policy.apply(SiteBlockState.EMPTY))
        assertEquals(emptyMap<String, Any>(), gw.restrictions["com.android.chrome"])
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
        assertEquals(setOf("URLBlocklist"), gw.restrictions["com.android.chrome"]!!.keys)

        assertTrue(chrome.setSites(SiteBlockState.EMPTY))
        assertEquals(emptyMap<String, Any>(), gw.restrictions["com.android.chrome"])
    }

    @Test fun aRefusedChangeIsNotKept() {
        val chrome = ChromeConfig(gw)
        gw.owner = false
        assertFalse(chrome.setDownloadsBlocked(true))
        gw.owner = true
        assertTrue(chrome.setSites(SiteBlockState(setOf("youtube.com"), false)))
        assertEquals(setOf("URLBlocklist"), gw.restrictions["com.android.chrome"]!!.keys)
    }
}
