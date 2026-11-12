package io.github.arnoneto1.midview.agent.sites

import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.policy.DevicePolicyGateway
import io.github.arnoneto1.midview.agent.policy.Lockdown
import java.io.File
import java.io.IOException

// What the phone knows about its browsers, read again at every check.
interface BrowserCatalog {
    // Every app that opens any http or https address (Chrome included).
    fun browsers(): Set<String>

    // Apps never hidden (the protected apps of the app block).
    fun protected(): Set<String>

    fun isChromeInstalled(): Boolean
}

// The school phone browses only in Chrome, which follows the teacher's site
// block: every other browser is hidden (gone from the home screen, not
// running, not uninstalled), checked again whenever an app is installed or
// updated. What was hidden is written to file, since Android no longer lists
// a hidden app, so it can be shown again; one that became protected (the
// WebView, say) is shown again at once. A phone without Chrome keeps its
// browsers. Only a Device Owner can hide apps.
class OtherBrowsers(
    private val catalog: BrowserCatalog,
    private val gw: DevicePolicyGateway,
    private val file: File,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    @Synchronized
    fun apply() {
        if (!gw.isDeviceOwner()) return
        try {
            val recorded = load(file)
            val protected = catalog.protected()
            val shown = toUnhide(recorded, protected).filter { gw.setApplicationHidden(it, false) }
            val hidden = toHide(catalog.browsers(), catalog.isChromeInstalled(), protected).filter { pkg ->
                gw.setApplicationHidden(pkg, true).also { if (!it) log("Android would not hide $pkg", null) }
            }
            val now = recorded - shown.toSet() + hidden
            if (now != recorded) {
                save(file, now)
                log("Other browsers hidden: ${now.sorted()}", null)
            }
        } catch (e: IOException) {
            log("The hidden browsers could not be recorded", e)
        } catch (e: RuntimeException) {
            log("Other browsers could not be hidden", e)
        }
    }

    companion object {
        // Never hidden: Chrome and the agent.
        private val NEVER = setOf(ChromePolicy.PACKAGE, Lockdown.PACKAGE)

        fun toHide(browsers: Set<String>, chromeInstalled: Boolean, protected: Set<String>): Set<String> =
            if (chromeInstalled) browsers - NEVER - protected else emptySet()

        fun toUnhide(recorded: Set<String>, protected: Set<String>): Set<String> = recorded.intersect(NEVER + protected)

        fun load(file: File): Set<String> =
            if (file.exists()) file.readLines(Charsets.UTF_8).filter { it.isNotBlank() }.toSet() else emptySet()

        private fun save(file: File, packages: Set<String>) {
            file.absoluteFile.parentFile?.mkdirs()
            writeAtomically(file, packages.sorted().joinToString("") { "$it\n" }.toByteArray(Charsets.UTF_8))
        }
    }
}
