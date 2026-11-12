package io.github.arnoneto1.midview.agent.sites

import io.github.arnoneto1.midview.agent.policy.DevicePolicyGateway

// Chrome's managed configuration as a whole (ChromePolicy): the site block
// (SitePolicy) and the lab panel's "Travar downloads" (LabConfigStore) go in
// the same bundle, which Android replaces at every write, so each change is
// written with the other's last value. A value is kept only once Chrome's
// configuration took it. A phone that is not the Device Owner cannot set it:
// false.
class ChromeConfig(
    private val gw: DevicePolicyGateway,
    downloadsBlocked: Boolean = false,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    private var sites = SiteBlockState.EMPTY
    private var downloadsBlocked = downloadsBlocked

    @Synchronized
    fun setSites(state: SiteBlockState): Boolean =
        write(state, downloadsBlocked, "Chrome's site block was refused").also { if (it) sites = state }

    @Synchronized
    fun setDownloadsBlocked(blocked: Boolean): Boolean =
        write(sites, blocked, "Chrome's download block was refused").also { if (it) downloadsBlocked = blocked }

    private fun write(state: SiteBlockState, blockDownloads: Boolean, refused: String): Boolean =
        try {
            gw.setApplicationRestrictions(ChromePolicy.PACKAGE, ChromePolicy.restrictions(state, blockDownloads))
            true
        } catch (e: RuntimeException) {
            log(refused, e)
            false
        }
}

// Makes the phone follow the site block (SiteBlockStore's apply): Chrome's
// policy (ChromeConfig) and the internet as a whole (InternetBlock). Both
// are always tried; true only when both hold. A phone that is not the Device
// Owner cannot set Chrome's policy, so it answers false.
class SitePolicy(
    private val chromeConfig: ChromeConfig,
    private val internet: InternetBlock,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    // Chrome's policy alone, as before the lab panel came to phones.
    constructor(gw: DevicePolicyGateway, internet: InternetBlock, log: (String, Throwable?) -> Unit = { _, _ -> }) :
        this(ChromeConfig(gw, log = log), internet, log)

    fun apply(state: SiteBlockState): Boolean {
        val chrome = chromeConfig.setSites(state)
        val whole = internet.apply(state.internetBlocked)
        if (chrome && whole) {
            log("Site block applied: ${state.domains.size} site(s), internet ${if (state.internetBlocked) "blocked" else "free"}.", null)
        }
        return chrome && whole
    }
}
