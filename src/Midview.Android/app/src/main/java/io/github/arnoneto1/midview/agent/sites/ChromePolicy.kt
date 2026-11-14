package io.github.arnoneto1.midview.agent.sites

// The sites the teacher blocked, as Chrome's managed configuration (the
// policies a Device Owner sets for it with setApplicationRestrictions): the
// URLBlocklist policy with each site and its "www." form, as the Windows
// agent writes both to the hosts file, or every address ("*") while the
// internet is blocked. Chrome shows its own "blocked" page for them. With
// "Travar downloads" of the lab panel on, also DownloadRestrictions 3 ("block
// all downloads"), which Chrome on Android follows from version 131 on (an
// older one keeps it and ignores it). Always TosDialogBehavior 2 and
// BrowserSignin 0; with nothing blocked those are the only policies Chrome
// gets. TosDialogBehavior 2 skips the Terms of Service only where Chromium's
// policy definition says it acts, the first run of Chrome's Custom Tabs (a
// link another app opens in Chrome): Chrome opened from its own icon still
// shows its first screen once. BrowserSignin 0 ("Disable browser sign-in",
// followed on Android since Chrome 70) keeps the Chrome browser itself out
// of any account, so it never takes the school's Google account the teacher
// puts on the device ("Contas do aparelho"); logging into a website
// (Classroom and the like) is only that site's cookies, and still works.
object ChromePolicy {
    const val PACKAGE = "com.android.chrome"
    const val URL_BLOCKLIST = "URLBlocklist"
    const val DOWNLOAD_RESTRICTIONS = "DownloadRestrictions"
    const val TOS_DIALOG_BEHAVIOR = "TosDialogBehavior"
    const val BROWSER_SIGNIN = "BrowserSignin"

    // TosDialogBehavior's "Skip the Terms of Service dialog on first run".
    const val SKIP_TOS_DIALOG = 2

    // BrowserSignin's "Disable browser sign-in".
    const val DISABLE_BROWSER_SIGNIN = 0

    // DownloadRestrictions' "Block all downloads".
    const val BLOCK_ALL_DOWNLOADS = 3

    // The first Chrome on Android that follows DownloadRestrictions.
    const val DOWNLOADS_FROM_VERSION = 131

    // Chrome reads at most 1000 entries of URLBlocklist: 500 sites, each with
    // its "www." form.
    const val MAX_DOMAINS = 500

    // A site saved with a final dot ("youtube.com.", which the Windows agent
    // accepts and keeps as typed) goes to Chrome without it: Chrome would block
    // nothing with the dot.
    fun urlBlocklist(state: SiteBlockState): List<String> =
        if (state.internetBlocked) {
            listOf("*")
        } else {
            state.domains
                .flatMap { listOfNotNull(it, wwwForm(it)) }
                .map { it.removeSuffix(".") }
                .distinct()
                .sorted()
        }

    // A list policy goes as an array of texts. Chrome's restrictions schema
    // declares URLBlocklist as a text (a JSON list), but Chrome's own reader
    // of the managed configuration also takes an array of texts as the list:
    // this was read in Chrome's APK, not yet seen in chrome://policy on a
    // phone (Chrome's first run there is still to be done).
    fun restrictions(state: SiteBlockState, blockDownloads: Boolean = false): Map<String, Any> {
        val blocklist = urlBlocklist(state)
        val policies = mutableMapOf<String, Any>(TOS_DIALOG_BEHAVIOR to SKIP_TOS_DIALOG, BROWSER_SIGNIN to DISABLE_BROWSER_SIGNIN)
        if (blocklist.isNotEmpty()) policies[URL_BLOCKLIST] = blocklist.toTypedArray()
        if (blockDownloads) policies[DOWNLOAD_RESTRICTIONS] = BLOCK_ALL_DOWNLOADS
        return policies
    }

    // HostsFileManager.ExpandBareAndWww: "www." added, or taken off (not
    // from "www." itself, which would leave nothing).
    private fun wwwForm(domain: String): String? = when {
        !domain.startsWith("www.") -> "www.$domain"
        domain.length > 4 -> domain.substring(4)
        else -> null
    }
}
