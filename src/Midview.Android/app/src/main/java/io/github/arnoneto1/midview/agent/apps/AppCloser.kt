package io.github.arnoneto1.midview.agent.apps

import io.github.arnoneto1.midview.agent.policy.DevicePolicyGateway

// The teacher's "Fechar programas" on a phone (CloseApps, from a trusted
// teacher): the home screen comes to the front and every app the student can
// open is closed. The Device Owner's way that holds on the phone is
// suspending an app, which makes Android close its screens at once; so the
// apps are suspended for a moment (AppSuspender.suspendBriefly), their
// background processes are ended meanwhile, and the suspension is let go.
// The teacher's app block is applied again afterwards, to be safe. Never
// touched: the agent, the home screen, the system's pieces, calls and
// emergency, Settings (ProtectedApps), and what is suspended already (the
// teacher's block, someone else's suspension). The internet block, the
// optimization and the teacher's PIN are not this class's business and stay
// as they are.
// The teacher's lock screen stays on top. Under it (lock task mode) Android
// does not close a suspended app (seen on the Redmi 10C, Android 13: Chrome
// came back when the lock was lifted), so a closing asked for then waits for
// the lock to go (unlocked, called by the lock's last release) and is done
// a moment later, once the lock task has ended.
// A phone has no warning nor question before it: the apps close at once
// (the Master says so), whatever the mode and the time the teacher picked.
class AppCloser(
    private val suspender: AppSuspender,
    private val gw: DevicePolicyGateway,
    private val self: String,
    private val reapplyBlock: () -> Unit,
    private val isScreenLocked: () -> Boolean,
    private val goHome: () -> Unit,
    private val killBackground: (String) -> Unit,
    private val pause: () -> Unit = { Thread.sleep(HOLD_MILLIS) },
    private val runLater: (delayMillis: Long, task: () -> Unit) -> Unit = ::onNewThread,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    // A closing asked for under the teacher's lock screen, done when it goes.
    @Volatile private var pending = false

    val isPending: Boolean get() = pending

    // The CommandAck's result: whether it was done, and what the teacher reads.
    @Synchronized
    fun close(): Pair<Boolean, String> {
        if (!gw.isDeviceOwner()) return false to NOT_DEVICE_OWNER
        if (isScreenLocked()) {
            pending = true
            // Lifted while this was asked: nothing to wait for.
            if (isScreenLocked()) return true to AFTER_LOCK
            pending = false
        }
        return closeNow()
    }

    // The teacher's lock screen went away (its last release): a closing
    // asked for under it is done a moment later, once the lock task ended.
    fun unlocked() {
        if (pending) runLater(UNLOCK_SETTLE_MILLIS) { closePending() }
    }

    @Synchronized
    private fun closePending() {
        // Locked again meanwhile: it waits for that lock to go.
        if (!pending || isScreenLocked()) return
        pending = false
        val (closed, detail) = closeNow()
        log("Closing asked for under the lock screen, done: $closed, $detail", null)
    }

    private fun closeNow(): Pair<Boolean, String> {
        try {
            goHome()
        } catch (e: RuntimeException) {
            // The apps still close, and the home screen is what is left.
            log("The home screen did not come up", e)
        }
        val brief = try {
            suspender.suspendBriefly(self) { closing ->
                pause()
                for (pkg in closing) {
                    try {
                        killBackground(pkg)
                    } catch (e: RuntimeException) {
                        log("Couldn't end $pkg in the background", e)
                    }
                }
            }
        } catch (e: RuntimeException) {
            log("Closing the apps was refused", e)
            return false to NOT_CLOSED
        } finally {
            reapplyBlock()
        }
        log("Apps closed: ${brief.closed.size}, refused: ${brief.refused}.", null)
        return true to detail(brief.refused.size)
    }

    companion object {
        // How long the apps stay suspended: enough for Android to close them.
        const val HOLD_MILLIS = 1_000L

        const val NOT_DEVICE_OWNER = "este celular não fecha apps"
        const val NOT_CLOSED = "os apps não puderam ser fechados"
        // After the lock task ended, before the apps are closed.
        const val UNLOCK_SETTLE_MILLIS = 1_500L

        const val CLOSED = "apps fechados; tela inicial na frente"
        const val AFTER_LOCK = "a tela está bloqueada: os apps fecham quando ela for liberada"

        // The apps to close: every one the student can open, except the
        // protected ones, the agent itself and those suspended already (the
        // teacher's block, or another app's suspension, which closing must
        // never lift). Sorted, as Android is told.
        fun toClose(launchable: Set<String>, protected: Set<String>, suspended: Set<String>, self: String): List<String> =
            (launchable - protected - suspended - self).sorted()

        // What the teacher reads for one phone.
        fun detail(refused: Int): String = when (refused) {
            0 -> CLOSED
            1 -> "$CLOSED (1 app não fechou)"
            else -> "$CLOSED ($refused apps não fecharam)"
        }

        private fun onNewThread(delayMillis: Long, task: () -> Unit) {
            Thread {
                try {
                    Thread.sleep(delayMillis)
                    task()
                } catch (e: InterruptedException) {
                    return@Thread
                }
            }.apply { isDaemon = true; name = "midview-close-apps" }.start()
        }
    }
}
