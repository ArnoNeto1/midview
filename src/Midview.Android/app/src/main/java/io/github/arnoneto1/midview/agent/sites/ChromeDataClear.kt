package io.github.arnoneto1.midview.agent.sites

import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

// "Sair das contas do Chrome" (CLEAR_CHROME_DATA), at the end of a class:
// Chrome's data erased as Settings' "Limpar armazenamento" does (the logins
// to websites, the cookies, the history and the tabs), so the next student
// opens a clean Chrome; it is stopped first, and shows its first screen once
// again. Only the Device Owner may (DevicePolicyManager's
// clearApplicationUserData, Android 9 on: the agent's minSdk is 10, so the
// version check only guards a build for older ones). Android answers on
// another thread: the command's thread waits for it, up to waitMillis.
// Chrome's policies (ChromePolicy: the blocked sites, BrowserSignin...) are
// written again afterwards (reapplyPolicy), whether or not Android kept them.
class ChromeDataClear(
    private val isDeviceOwner: () -> Boolean,
    private val sdkInt: Int,
    private val isChromeInstalled: () -> Boolean,
    private val clear: (pkg: String, done: (Boolean) -> Unit) -> Unit,
    private val reapplyPolicy: () -> Unit = {},
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
    private val waitMillis: Long = WAIT_MILLIS,
) {
    // The teacher's command (no payload) in, the CommandAck's result out.
    fun handle(payload: ByteArray): Pair<Boolean, String> =
        if (payload.isNotEmpty()) false to INVALID_REQUEST else run()

    fun run(): Pair<Boolean, String> {
        if (!isDeviceOwner()) return false to NOT_PREPARED
        if (sdkInt < ANDROID_9) return false to TOO_OLD
        if (!isChromeInstalled()) return false to NO_CHROME
        val answered = CountDownLatch(1)
        var cleared = false
        try {
            clear(ChromePolicy.PACKAGE) { succeeded ->
                cleared = succeeded
                answered.countDown()
            }
        } catch (e: RuntimeException) {
            log("Android refused to clear Chrome's data", e)
            return false to REFUSED
        }
        val inTime = try {
            answered.await(waitMillis, TimeUnit.MILLISECONDS)
        } catch (e: InterruptedException) {
            Thread.currentThread().interrupt()
            false
        }
        if (!inTime) {
            log("Android did not say in time whether Chrome's data was cleared", null)
            return false to NO_ANSWER
        }
        if (!cleared) {
            log("Android did not clear Chrome's data", null)
            return false to NOT_CLEARED
        }
        try {
            reapplyPolicy()
        } catch (e: RuntimeException) {
            log("Chrome's policies were not written again after clearing its data", e)
        }
        log("Chrome's data cleared.", null)
        return true to CLEARED
    }

    companion object {
        // Android takes a few seconds for a Chrome with a lot in it.
        const val WAIT_MILLIS = 60_000L
        private const val ANDROID_9 = 28

        const val CLEARED = "Chrome limpo: saiu das contas dos sites e apagou cookies, histórico e abas"
        const val NOT_PREPARED = "Este aparelho não foi preparado pelo Midview."
        const val TOO_OLD = "o Android deste aparelho é anterior ao 9 e não deixa apagar os dados do Chrome"
        const val NO_CHROME = "o Chrome não está instalado neste aparelho"
        const val REFUSED = "o Android recusou apagar os dados do Chrome"
        const val NOT_CLEARED = "o Android não apagou os dados do Chrome"
        const val NO_ANSWER = "o Android não respondeu a tempo se apagou os dados do Chrome"
        const val INVALID_REQUEST = "pedido inválido"
    }
}
