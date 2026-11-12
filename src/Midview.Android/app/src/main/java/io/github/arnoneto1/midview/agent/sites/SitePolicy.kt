package io.github.arnoneto1.midview.agent.sites

import io.github.arnoneto1.midview.agent.policy.DevicePolicyGateway

// Makes the phone follow the site block (SiteBlockStore's apply): Chrome's
// policy (ChromePolicy) and the internet as a whole (InternetBlock). Both
// are always tried; true only when both hold. A phone that is not the Device
// Owner cannot set Chrome's policy, so it answers false.
class SitePolicy(
    private val gw: DevicePolicyGateway,
    private val internet: InternetBlock,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    fun apply(state: SiteBlockState): Boolean {
        val chrome = try {
            gw.setApplicationRestrictions(ChromePolicy.PACKAGE, ChromePolicy.restrictions(state))
            true
        } catch (e: RuntimeException) {
            log("Chrome's site block was refused", e)
            false
        }
        val whole = internet.apply(state.internetBlocked)
        if (chrome && whole) {
            log("Site block applied: ${state.domains.size} site(s), internet ${if (state.internetBlocked) "blocked" else "free"}.", null)
        }
        return chrome && whole
    }
}
