package io.github.arnoneto1.midview.agent.agent

import io.github.arnoneto1.midview.agent.apps.AndroidCloseNotice
import io.github.arnoneto1.midview.agent.apps.AppCloser
import io.github.arnoneto1.midview.agent.apps.CloseCountdown
import io.github.arnoneto1.midview.agent.power.AndroidPhonePower
import io.github.arnoneto1.midview.agent.power.PowerCountdown
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

// The student's warning and the wait, then the closing, on one thread of its own.
internal fun AgentService.closeCountdown(
    closer: AppCloser,
    log: (String, Throwable?) -> Unit,
    own: (() -> Unit) -> Unit,
): CloseCountdown {
    val closing = Executors.newSingleThreadScheduledExecutor { Thread(it, "Midview close apps").apply { isDaemon = true } }
    own { closing.shutdownNow() }
    return CloseCountdown(
        closer, AndroidCloseNotice(this),
        schedule = { delay, task ->
            closing.schedule(
                Runnable {
                    try {
                        task()
                    } catch (e: RuntimeException) {
                        log("The warning before closing the apps failed", e)
                    }
                },
                delay, TimeUnit.MILLISECONDS,
            )
        },
        log = log,
    )
}

// "Desligar" and "Reiniciar": the student's warning and the wait,
// then the screen off or the restart, on a thread of their own.
internal fun AgentService.powerCountdown(
    log: (String, Throwable?) -> Unit,
    own: (() -> Unit) -> Unit,
): PowerCountdown {
    val powering = Executors.newSingleThreadScheduledExecutor { Thread(it, "Midview power").apply { isDaemon = true } }
    own { powering.shutdownNow() }
    return PowerCountdown(
        AndroidPhonePower(this), AndroidCloseNotice(this, AndroidCloseNotice.POWER_NOTIFICATION),
        schedule = { delay, task ->
            powering.schedule(
                Runnable {
                    try {
                        task()
                    } catch (e: RuntimeException) {
                        log("The warning before the restart or the screen off failed", e)
                    }
                },
                delay, TimeUnit.MILLISECONDS,
            )
        },
        log = log,
    )
}
