package io.github.arnoneto1.midview.agent.control

import io.github.arnoneto1.midview.agent.protocol.InputEvent

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
// begins (or already had), or why not; input queues one event (InputQueue,
// which stays small), which a thread of the control's own carries out in
// order, so the screen stream never waits for a gesture; stop ends it
// without waiting (the gesture under way finishes, a finger still down
// lifts, the strip goes), and stopAndWait waits for that, at the end of the
// connection. While the teacher's lock is on, the teacher's taps land on
// the padlock and Back, Home and Recents are skipped: control never takes
// the student out of the lock.
class RemoteControl(
    private val target: ControlTarget,
    private val frameSize: () -> Size?,
    private val clock: () -> Long = { System.nanoTime() / 1_000_000 },
    private val log: (String) -> Unit = {},
) {
    private val lock = Any()
    private var queue: InputQueue? = null
    private var worker: Thread? = null

    // The thread of a control that was stopped and may still be finishing.
    private var ending: Thread? = null

    val controlling: Boolean get() = synchronized(lock) { queue != null }

    fun start(): String? = synchronized(lock) {
        if (queue != null) return null
        target.refusal()?.let { return it }
        // One finger at a time: a control stopped a moment ago finishes first.
        ending?.join(STOP_WAIT_MILLIS)
        ending = null
        val events = InputQueue()
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
        synchronized(lock) { queue }?.put(event, clock())
    }

    fun stop() {
        synchronized(lock) {
            val events = queue ?: return
            queue = null
            ending = worker
            worker = null
            events.stop()
        }
    }

    fun stopAndWait() {
        stop()
        val thread = synchronized(lock) { ending.also { ending = null } }
        thread?.join(STOP_WAIT_MILLIS)
    }

    private fun carryOut(events: InputQueue) {
        val planner = TouchPlanner(frameSize, target::screenSize)
        var refused = 0
        try {
            while (true) {
                val batch = events.takeAll()
                val stopping = batch.any { it === InputQueue.Stop }
                val steps = mutableListOf<TouchStep>()
                for (item in batch) {
                    steps += when (item) {
                        is InputQueue.Event -> planner.plan(item.event, item.atMs)
                        is InputQueue.Text -> planner.typed(item.text)
                        is InputQueue.Key -> (1..item.count).flatMap { planner.plan(InputEvent(InputEvent.KEY_DOWN, 0, 0, item.key), item.atMs) }
                        is InputQueue.Wheel -> planner.plan(InputEvent(InputEvent.WHEEL, item.delta, 0, 0), item.atMs)
                        InputQueue.Stop -> break
                    }
                }
                if (stopping) steps += planner.release()
                if (events.refused != refused) {
                    refused = events.refused
                    log("The teacher's input came faster than the phone could take it: $refused event(s) refused")
                }
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
