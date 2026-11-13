package io.github.arnoneto1.midview.agent.sites

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ChromePolicyTest {
    private fun state(vararg domains: String, internet: Boolean = false) = SiteBlockState(domains.toSet(), internet)

    // Nothing blocked: Chrome only skips its Terms of Service screen on first run.
    @Test fun nothingBlockedLeavesChromeOnlySkippingItsTerms() {
        assertEquals(mapOf<String, Any>("TosDialogBehavior" to 2), ChromePolicy.restrictions(SiteBlockState.EMPTY))
    }

    // The student's first Chrome opens without the Terms of Service dialog,
    // whatever else is blocked.
    @Test fun theTermsDialogIsAlwaysSkipped() {
        assertEquals(2, ChromePolicy.restrictions(SiteBlockState.EMPTY)["TosDialogBehavior"])
        assertEquals(2, ChromePolicy.restrictions(state("youtube.com", internet = true), blockDownloads = true)["TosDialogBehavior"])
    }

    @Test fun eachDomainIsBlockedWithItsWwwForm() {
        assertEquals(
            listOf("facebook.com", "www.facebook.com", "www.youtube.com", "youtube.com"),
            ChromePolicy.urlBlocklist(state("youtube.com", "facebook.com")),
        )
    }

    // As HostsFileManager.ExpandBareAndWww on Windows.
    @Test fun aWwwDomainAlsoBlocksTheBareOne() {
        assertEquals(listOf("g1.com", "www.g1.com"), ChromePolicy.urlBlocklist(state("www.g1.com")))
        assertEquals(listOf("g1.com", "www.g1.com"), ChromePolicy.urlBlocklist(state("www.g1.com", "g1.com")))
        assertEquals(listOf("www"), ChromePolicy.urlBlocklist(state("www.")))
    }

    // The teacher may type the site with a final dot ("youtube.com.", a valid
    // name the phone keeps as typed, as the Windows agent does), but Chrome
    // blocks nothing with it: Chrome gets the site without that dot.
    @Test fun aTrailingDotIsLeftOutForChrome() {
        assertEquals(listOf("www.youtube.com", "youtube.com"), ChromePolicy.urlBlocklist(state("youtube.com.")))
        assertEquals(listOf("g1.com", "www.g1.com"), ChromePolicy.urlBlocklist(state("www.g1.com.")))
        assertEquals(
            listOf("www.youtube.com", "youtube.com"),
            ChromePolicy.urlBlocklist(state("youtube.com.", "youtube.com")),
        )
        assertArrayEquals(
            arrayOf("www.youtube.com", "youtube.com"),
            ChromePolicy.restrictions(state("youtube.com."))["URLBlocklist"] as Array<*>,
        )
    }

    @Test fun theBlockedInternetBlocksEveryAddress() {
        assertEquals(listOf("*"), ChromePolicy.urlBlocklist(state(internet = true)))
        assertEquals(listOf("*"), ChromePolicy.urlBlocklist(state("youtube.com", internet = true)))
    }

    @Test fun theBlocklistGoesAsAListOfTexts() {
        val restrictions = ChromePolicy.restrictions(state("youtube.com"))
        assertEquals(setOf("TosDialogBehavior", "URLBlocklist"), restrictions.keys)
        assertArrayEquals(arrayOf("www.youtube.com", "youtube.com"), restrictions["URLBlocklist"] as Array<*>)
    }

    @Test fun chromeTakesAtMostAThousandEntries() {
        val most = SiteBlockState((1..ChromePolicy.MAX_DOMAINS).map { "s$it.com" }.toSet(), false)
        assertEquals(1000, ChromePolicy.urlBlocklist(most).size)
    }

    // "Travar downloads" of the lab panel: Chrome's "Block all downloads".
    @Test fun blockedDownloadsGoAsDownloadRestrictions3() {
        assertEquals(
            mapOf<String, Any>("TosDialogBehavior" to 2, "DownloadRestrictions" to 3),
            ChromePolicy.restrictions(SiteBlockState.EMPTY, blockDownloads = true),
        )
        val both = ChromePolicy.restrictions(state("youtube.com"), blockDownloads = true)
        assertEquals(setOf("TosDialogBehavior", "URLBlocklist", "DownloadRestrictions"), both.keys)
        assertEquals(3, both["DownloadRestrictions"])
    }
}
