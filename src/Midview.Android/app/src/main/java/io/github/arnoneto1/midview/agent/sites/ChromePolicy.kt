package io.github.arnoneto1.midview.agent.sites

// The sites the teacher blocked, as Chrome's managed configuration (the
// policies a Device Owner sets for it with setApplicationRestrictions): the
// URLBlocklist policy with each site and its "www." form, as the Windows
// agent writes both to the hosts file, or every address ("*") while the
// internet is blocked. Chrome shows its own "blocked" page for them. With
// nothing blocked Chrome is left with no policy at all.
object ChromePolicy {
    const val PACKAGE = "com.android.chrome"
    const val URL_BLOCKLIST = "URLBlocklist"

    // Chrome reads at most 1000 entries of URLBlocklist: 500 sites, each with
    // its "www." form.
    const val MAX_DOMAINS = 500

    fun urlBlocklist(state: SiteBlockState): List<String> =
        if (state.internetBlocked) {
            listOf("*")
        } else {
            state.domains.flatMap { listOfNotNull(it, wwwForm(it)) }.distinct().sorted()
        }

    // A list policy goes as an array of texts (checked on the Redmi in
    // chrome://policy).
    fun restrictions(state: SiteBlockState): Map<String, Any> {
        val blocklist = urlBlocklist(state)
        return if (blocklist.isEmpty()) emptyMap() else mapOf(URL_BLOCKLIST to blocklist.toTypedArray())
    }

    // HostsFileManager.ExpandBareAndWww: "www." added, or taken off (not
    // from "www." itself, which would leave nothing).
    private fun wwwForm(domain: String): String? = when {
        !domain.startsWith("www.") -> "www.$domain"
        domain.length > 4 -> domain.substring(4)
        else -> null
    }
}
