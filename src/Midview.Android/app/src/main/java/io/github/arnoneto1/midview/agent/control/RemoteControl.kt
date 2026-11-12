package io.github.arnoneto1.midview.agent.control

import io.github.arnoneto1.midview.agent.protocol.InputEvent
import java.util.concurrent.LinkedBlockingQueue

// What carries the teacher's input out on the phone (AndroidControlTarget;
// a test's fake).
interface ControlTarget {
    // Why the phone cannot be controlled now, for the teacher; null when it can.
    fun refusal(): String?

    // The real screen now, in the current rotation; null when unknown.
    fun screenSize(): Size?

    // Does the step and returns once it is done (a gesture finished).
    fun perform(step: TouchStep)

    // The student's "being controlled" strip: one show per controlling
    // connection, one hide when it stops.
    fun showNotice()

    fun hideNotice()

    // Where the teacher's mouse is: the strip fades while it is close.
    fun pointerAt(spot: Spot)

    // The teacher's screen lock is on: the phone's buttons lead nowhere.
    val locked: Boolean
}

// "Controlar" on one teacher's screen connection, as a PC's Session does it
// (StartControl, InputEvent, StopControl): start answers null when control
// begins (or already had), or why not; input queues one event, which a
// thread of the control's own carries out in order, so the screen stream
// never waits for a gesture; stop ends it, lifting a finger still down and
// taking the strip away. While the teacher's lock is on, the teacher's
// taps land on the padlock and Back, Home and Recents are skipped: control
// never takes the student out of the lock.
class RemoteControl(
    private val target: ControlTarget,
    private val frameSize: () -> Size?,
    private val clock: () -> Long = { System.nanoTime() / 1_000_000 },
) {
    private object Stop

    private class Queued(val event: InputEvent, val atMs: Long)

    private val lock = Any()
    private var queue: LinkedBlockingQueue<Any>? = null
    private var worker: Thread? = null

    val controlling: Boolean get() = synchronized(lock) { queue != null }

    fun start(): String? = synchronized(lock) {
        if (queue != null) return null
        target.refusal()?.let { return it }
        val events = LinkedBlockingQueue<Any>()
        queue = events
        target.showNotice()
        worker = Thread({ carryOut(events) }, "Midview control").apply {
            isDaemon = true
            start()
        }
        null
    }

    // Ignored while not controlling, as the Session does.
    fun input(event: InputEvent) {
        synchronized(lock) { queue }?.put(Queued(event, clock()))
    }

    fun stop() {
        val (events, thread) = synchronized(lock) {
            val events = queue ?: return
            queue = null
            events to worker.also { worker = null }
        }
        events.put(Stop)
        thread?.join(STOP_WAIT_MILLIS)
    }

    private fun carryOut(events: LinkedBlockingQueue<Any>) {
        val planner = TouchPlanner(frameSize, target::screenSize)
        try {
            while (true) {
                val batch = mutableListOf(events.take())
                events.drainTo(batch)
                val stopping = batch.any { it === Stop }
                val steps = mutableListOf<TouchStep>()
                for (item in batch) {
                    if (item !is Queued) break
                    steps += planner.plan(item.event, item.atMs)
                }
                if (stopping) steps += planner.release()
                planner.pointer?.let { target.pointerAt(it) }
                for (step in planner.merge(steps)) {
                    if (step is TouchStep.Global && target.locked) continue
                    try {
                        target.perform(step)
                    } catch (e: RuntimeException) {
                        // One step that failed (the accessibility service
                        // going away, a field that went) is skipped.
                    }
                }
                if (stopping) return
            }
        } catch (e: InterruptedException) {
            // Ending.
        } finally {
            target.hideNotice()
        }
    }

    companion object {
        // A gesture under way finishes first; never longer than this.
        private const val STOP_WAIT_MILLIS = 3_000L
    }
}
