package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.protocol.PhoneHomeStates
import io.github.arnoneto1.midview.agent.protocol.PhoneOptimizeReport
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.withLock

// The home screen arranged as the last step of a teacher's "Otimizar"
// (home.HomeArrangement, through the accessibility service), never of the
// optimization the agent runs by itself after pairing: arranging drives the
// screen, which only the teacher asks for. Arranging takes up to four
// minutes (168 s measured on a cluttered M10, 18 s on one already arranged),
// longer than the Master waits for the answer (90 s): it runs on a thread of
// its own, and the teacher's answer waits for it at most until
// ANSWER_WITHIN after the teacher asked (an uninstall in the background may
// then hold the report for up to 15 s more). Not done by then, the report
// says it is still being arranged (PhoneHomeStates.WORKING) and the outcome
// is kept when it ends, for the next "Otimizar" to send, the same way the
// optimizer leaves slow uninstalls for later. One arrangement at a time: a
// "Otimizar" while one runs reads WORKING. The report is changed under the
// optimizer's lock, like every other change to it.
class HomeStep(
    private val arranger: HomeArranger?,
    private val lock: ReentrantLock,
    private val store: OptimizeStore,
    private val isDeviceOwner: () -> Boolean,
    private val ticker: () -> Long,
    private val background: (Runnable) -> Unit,
    private val log: (String, Throwable?) -> Unit,
) {
    private val running = AtomicBoolean(false)

    private class Outcome {
        val done = CountDownLatch(1)
        var ended = false
    }

    // The report once the home is arranged or left for later, or null when
    // there is nothing to arrange it with (no arranger, not the school's
    // device), and the report stays as it was. asked: ticker time.
    fun arrange(asked: Long): PhoneOptimizeReport? {
        if (arranger == null || !owner()) return null
        if (!running.compareAndSet(false, true)) {
            return lock.withLock {
                if (running.get()) keep(PhoneHomeStates.WORKING)
                store.current.report
            }
        }
        val outcome = Outcome()
        try {
            background(Runnable { finish(arranger, outcome) })
        } catch (e: Exception) {
            log("The home screen could not be arranged", e)
            running.set(false)
            return lock.withLock {
                keep(PhoneHomeStates.BY_HAND)
                store.current.report
            }
        }
        val wait = TimeUnit.NANOSECONDS.toMillis(asked + ANSWER_WITHIN_NANOS - ticker())
        if (wait > 0) {
            try {
                outcome.done.await(wait, TimeUnit.MILLISECONDS)
            } catch (_: InterruptedException) {
                Thread.currentThread().interrupt()
            }
        }
        return lock.withLock {
            if (!outcome.ended) {
                log("The home screen is still being arranged: the teacher reads it next time.", null)
                keep(PhoneHomeStates.WORKING)
            }
            store.current.report
        }
    }

    private fun finish(arranger: HomeArranger, outcome: Outcome) {
        val state = try {
            arranger.arrange()
        } catch (e: Exception) {
            log("The home screen arrangement failed", e)
            PhoneHomeStates.BY_HAND
        }
        try {
            lock.withLock {
                keep(state)
                outcome.ended = true
                running.set(false)
            }
        } finally {
            running.set(false)
            outcome.done.countDown()
        }
        log("Home screen: $state.", null)
    }

    private fun keep(state: String) {
        try {
            val held = store.current
            if (held.report.home != state) store.save(held.copy(report = held.report.copy(home = state)))
        } catch (e: Exception) {
            log("The report could not be updated", e)
        }
    }

    private fun owner(): Boolean =
        try {
            isDeviceOwner()
        } catch (e: Exception) {
            false
        }

    private companion object {
        // 70 s after the teacher asked: with an uninstall in the background
        // holding the report up to 15 s more, still within the Master's 90 s.
        val ANSWER_WITHIN_NANOS = TimeUnit.SECONDS.toNanos(70)
    }
}

// Arranges the home screen now (minutes at most) and says how it was left
// (PhoneHomeStates).
fun interface HomeArranger {
    fun arrange(): String
}
