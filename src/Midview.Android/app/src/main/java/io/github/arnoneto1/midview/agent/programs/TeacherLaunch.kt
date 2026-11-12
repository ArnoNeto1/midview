package io.github.arnoneto1.midview.agent.programs

import io.github.arnoneto1.midview.agent.protocol.PhoneAppPolicy
import io.github.arnoneto1.midview.agent.protocol.TextMessage
import io.github.arnoneto1.midview.agent.sites.DomainNormalizer
import java.net.URI
import java.net.URISyntaxException

// Whether an app can be opened now, as the phone sees it.
enum class AppState {
    // Installed, enabled, with a screen to open.
    READY,

    // Not on the phone for the student: never installed, removed, hidden
    // ("Otimizar"), turned off, or with no screen to open.
    MISSING,

    // Suspended: the teacher's app block.
    BLOCKED,
}

// What opening needs of Android (AndroidLaunchScreen), behind an interface so
// TeacherLaunch is tested on the JVM. open and openSite throw when Android
// will not start the screen.
interface LaunchScreen {
    fun state(pkg: String): AppState

    // The app's own first screen, in front.
    fun open(pkg: String)

    // The address in Chrome, in a new tab.
    fun openSite(address: String)
}

// The teacher's "Abrir site" and "Abrir programa" on this phone (a PC's
// Session takes them). A site opens in Chrome, the phone's one browser (the
// others are hidden: OtherBrowsers); the teacher's site block and internet
// block stay as they are, so a blocked site shows Chrome's block page, and the
// teacher is told. An app opens by its package, from the list the phone sent
// (GetPhoneApps); one the teacher's app block suspends does not, and the
// teacher is told why. Under the teacher's lock screen nothing comes over the
// padlock (and nothing would show behind it): what was asked waits, in order,
// and opens once the teacher unlocks (unlocked). No Android classes (JVM
// tests); safe from any thread.
class TeacherLaunch(
    private val screen: LaunchScreen,
    private val selfPackage: String,
    private val isLocked: () -> Boolean,
    private val internetBlocked: () -> Boolean,
    private val blockedSites: () -> Set<String>,
    // Runs the task on another thread after the delay (the lock task has to
    // be over before anything comes up).
    private val later: (delayMillis: Long, task: () -> Unit) -> Unit,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    private val gate = Any()
    private val waiting = ArrayList<() -> Pair<Boolean, String>>()

    // How many wait under the lock screen now.
    val waitingCount: Int get() = synchronized(gate) { waiting.size }

    // OpenWebsite's payload in, the CommandAck's result out.
    fun openWebsite(payload: ByteArray): Pair<Boolean, String> {
        val address = WebsiteCheck.address(TextMessage.decode(payload)) ?: return false to NOT_A_SITE
        return whenUnlocked(SITE_AFTER_LOCK) { site(address) }
    }

    // OpenProgram's payload (an app's package) in, the CommandAck's result out.
    fun openApp(payload: ByteArray): Pair<Boolean, String> {
        val pkg = TextMessage.decode(payload)
        if (pkg == null || !PhoneAppPolicy.isValidPackageName(pkg)) return false to NOT_AN_APP
        if (pkg == selfPackage) return false to SELF
        // Said now, and not after the lock: the teacher is still there.
        when (screen.state(pkg)) {
            AppState.MISSING -> return false to MISSING
            AppState.BLOCKED -> return false to BLOCKED
            AppState.READY -> {}
        }
        return whenUnlocked(APP_AFTER_LOCK) { app(pkg) }
    }

    // The teacher's lock screen went: what waited opens, in the order asked,
    // a moment later. Locked again by then, it keeps waiting.
    fun unlocked() {
        later(AFTER_UNLOCK_MILLIS) {
            while (!isLocked()) {
                val next = synchronized(gate) { waiting.removeFirstOrNull() } ?: return@later
                val (opened, detail) = next()
                log("Opened after the lock screen: $opened, $detail", null)
            }
        }
    }

    private fun whenUnlocked(afterLock: String, open: () -> Pair<Boolean, String>): Pair<Boolean, String> {
        synchronized(gate) {
            if (isLocked()) {
                if (waiting.size >= MAX_WAITING) return false to TOO_MANY_WAITING
                waiting += open
                return true to afterLock
            }
        }
        return open()
    }

    private fun site(address: String): Pair<Boolean, String> {
        when (screen.state(CHROME)) {
            AppState.MISSING -> return false to NO_CHROME
            AppState.BLOCKED -> return false to CHROME_BLOCKED
            AppState.READY -> {}
        }
        try {
            screen.openSite(address)
        } catch (e: RuntimeException) {
            log("Opening a site for the student failed", e)
            return false to "o Chrome não abriu: ${describe(e)}"
        }
        return true to when {
            internetBlocked() -> OPENED_NO_INTERNET
            WebsiteCheck.isBlocked(address, blockedSites()) -> OPENED_BLOCKED
            else -> OPENED_SITE
        }
    }

    private fun app(pkg: String): Pair<Boolean, String> {
        // Again: the block may have changed while it waited.
        when (screen.state(pkg)) {
            AppState.MISSING -> return false to MISSING
            AppState.BLOCKED -> return false to BLOCKED
            AppState.READY -> {}
        }
        return try {
            screen.open(pkg)
            true to OPENED_APP
        } catch (e: RuntimeException) {
            log("Opening an app for the student failed", e)
            false to "não abriu: ${describe(e)}"
        }
    }

    companion object {
        const val CHROME = "com.android.chrome"

        const val OPENED_SITE = "aberto no Chrome"
        const val OPENED_NO_INTERNET = "aberto no Chrome, mas a internet deste celular está bloqueada"
        const val OPENED_BLOCKED = "aberto no Chrome, mas o site está bloqueado: aparece a página de bloqueio"
        const val SITE_AFTER_LOCK = "a tela está bloqueada: o site abre quando ela for liberada"
        const val NOT_A_SITE = "não é o endereço de um site"
        const val NO_CHROME = "o Chrome não está neste celular"
        const val CHROME_BLOCKED = "o Chrome está bloqueado em \"Bloquear programas\": libere-o para abrir sites"

        const val OPENED_APP = "aberto"
        const val APP_AFTER_LOCK = "a tela está bloqueada: o app abre quando ela for liberada"
        const val NOT_AN_APP = "não é um app deste celular"
        const val MISSING = "este app não está neste celular"
        const val BLOCKED = "o app está bloqueado em \"Bloquear programas\": libere-o para abrir"
        const val SELF = "o Midview não se abre por aqui"
        const val TOO_MANY_WAITING = "a tela está bloqueada e já há muita coisa esperando para abrir"

        // As TeacherTalk: the padlock's lock task is over by then.
        const val AFTER_UNLOCK_MILLIS = 1_500L

        // Sites and apps waiting under one lock screen, at most.
        const val MAX_WAITING = 20

        private fun describe(e: Exception): String = e.message?.takeIf { it.isNotBlank() } ?: e.javaClass.simpleName
    }
}

// The address OpenWebsite carries, checked again on the phone whatever the
// teacher's program checked (WebsiteAddress on the PC): only a website
// (http:// or https://, with a host), never a file, an app's link or anything
// else Chrome would open.
object WebsiteCheck {
    fun address(text: String?): String? {
        if (text == null || text.length > TextMessage.MAX_LENGTH || text.any { it.isWhitespace() || it.code < 32 }) return null
        val uri = try {
            URI(text)
        } catch (e: URISyntaxException) {
            return null
        }
        val scheme = uri.scheme?.lowercase() ?: return null
        if (scheme != "http" && scheme != "https") return null
        if (uri.rawAuthority.isNullOrEmpty() || uri.isOpaque) return null
        return text
    }

    // Whether the teacher's site block (its plain site names, as
    // DomainNormalizer makes them) covers the address: the site or one of its
    // subdomains, as Chrome's block list takes them.
    fun isBlocked(address: String, blocked: Set<String>): Boolean {
        if (blocked.isEmpty()) return false
        val host = DomainNormalizer.normalize(address) ?: return false
        return blocked.any { host == it || host.endsWith(".$it") }
    }
}
