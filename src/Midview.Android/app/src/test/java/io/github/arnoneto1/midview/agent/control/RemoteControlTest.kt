package io.github.arnoneto1.midview.agent.control

import io.github.arnoneto1.midview.agent.protocol.InputEvent
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.Collections
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

class RemoteControlTest {
    // A phone whose screen is the picture's size, recording what it is asked to do.
    class FakeTarget : ControlTarget {
        @Volatile var refusing: String? = null
        @Volatile override var locked = false
        val done: MutableList<TouchStep> = Collections.synchronizedList(mutableListOf())
        val notice: MutableList<String> = Collections.synchronizedList(mutableListOf())
        @Volatile var pointer: Spot? = null

        // Held by the first step until released: a gesture that takes time.
        @Volatile var slow: CountDownLatch? = null

        override fun refusal() = refusing
        override fun screenSize() = Size(100, 200)
        override fun perform(step: TouchStep) {
            slow?.await(2, TimeUnit.SECONDS)
            done += step
        }
        override fun showNotice() {
            notice += "show"
        }
        override fun hideNotice() {
            notice += "hide"
        }
        override fun pointerAt(spot: Spot) {
            pointer = spot
        }

        fun waitFor(count: Int): List<TouchStep> {
            val until = System.currentTimeMillis() + 2_000
            while (done.size < count && System.currentTimeMillis() < until) Thread.sleep(5)
            return synchronized(done) { done.toList() }
        }
    }

    private val target = FakeTarget()
    private val control = RemoteControl(target, frameSize = { Size(100, 200) })

    private fun event(kind: Int, x: Int = 0, y: Int = 0, key: Int = 0) = InputEvent(kind, x, y, key)

    @Test fun refusedWhenThePhoneCannotBeControlled() {
        target.refusing = "a acessibilidade está desligada"
        assertEquals("a acessibilidade está desligada", control.start())
        assertFalse(control.controlling)
        assertEquals(emptyList<String>(), target.notice)
    }

    @Test fun startShowsTheStripOnceAndStopTakesItAway() {
        assertNull(control.start())
        assertNull(control.start())
        assertTrue(control.controlling)
        control.stop()
        control.stop()
        assertFalse(control.controlling)
        assertEquals(listOf("show", "hide"), target.notice)
    }

    @Test fun inputBeforeStartIsIgnored() {
        control.input(event(InputEvent.LEFT_DOWN, 10, 10))
        assertNull(control.start())
        control.input(event(InputEvent.KEY_DOWN, key = 0x1B))
        assertEquals(listOf<TouchStep>(TouchStep.Global(GlobalKey.BACK)), target.waitFor(1))
        control.stop()
    }

    @Test fun aClickBecomesATapInOrder() {
        control.start()
        control.input(event(InputEvent.MOUSE_MOVE, 10, 20))
        control.input(event(InputEvent.LEFT_DOWN, 10, 20))
        control.input(event(InputEvent.LEFT_UP, 10, 20))
        control.input(event(InputEvent.TEXT, key = 'x'.code))
        assertEquals(
            listOf(TouchStep.Down(Spot(10.5f, 20.5f)), TouchStep.Up(Spot(10.5f, 20.5f)), TouchStep.Type("x")),
            target.waitFor(3),
        )
        assertEquals(Spot(10.5f, 20.5f), target.pointer)
        control.stop()
    }

    @Test fun eventsArrivingDuringAGestureAreMergedNotLost() {
        val gate = CountDownLatch(1)
        target.slow = gate
        control.start()
        control.input(event(InputEvent.LEFT_DOWN, 10, 10))
        Thread.sleep(100) // the Down is under way
        for (y in 11..30) control.input(event(InputEvent.MOUSE_MOVE, 10, y))
        "abc".forEach { control.input(event(InputEvent.TEXT, key = it.code)) }
        gate.countDown()
        target.slow = null
        val done = target.waitFor(3)
        assertEquals(TouchStep.Down(Spot(10.5f, 10.5f)), done[0])
        val move = done[1] as TouchStep.Move
        assertEquals(Spot(10.5f, 10.5f), move.from)
        assertEquals(Spot(10.5f, 30.5f), move.to)
        assertEquals(TouchStep.Type("abc"), done[2])
        control.stop()
    }

    @Test fun stoppingMidDragLiftsTheFinger() {
        control.start()
        control.input(event(InputEvent.LEFT_DOWN, 10, 10))
        control.input(event(InputEvent.MOUSE_MOVE, 10, 50))
        target.waitFor(2)
        control.stop()
        assertEquals(TouchStep.Up(Spot(10.5f, 50.5f)), target.done.last())
        assertEquals(listOf("show", "hide"), target.notice)
    }

    @Test fun underTheTeachersLockTheButtonsAreSkippedButTouchesStillGo() {
        target.locked = true
        control.start()
        control.input(event(InputEvent.KEY_DOWN, key = 0x1B))
        control.input(event(InputEvent.KEY_DOWN, key = 0x5B))
        control.input(event(InputEvent.KEY_DOWN, key = 0x5D))
        control.input(event(InputEvent.LEFT_DOWN, 5, 5))
        control.input(event(InputEvent.LEFT_UP, 5, 5))
        assertEquals(listOf(TouchStep.Down(Spot(5.5f, 5.5f)), TouchStep.Up(Spot(5.5f, 5.5f))), target.waitFor(2))
        control.stop()
        assertFalse(target.done.any { it is TouchStep.Global })
    }

    @Test fun controlCanStartAgainAfterStopping() {
        control.start()
        control.stop()
        assertNull(control.start())
        control.input(event(InputEvent.KEY_DOWN, key = 0x08))
        assertEquals(listOf<TouchStep>(TouchStep.Edit(EditKey.BACKSPACE)), target.waitFor(1))
        control.stop()
        assertEquals(listOf("show", "hide", "show", "hide"), target.notice)
    }

    @Test fun aStepThatFailsDoesNotEndControl() {
        val failing = object : ControlTarget by target {
            var first = true
            override fun perform(step: TouchStep) {
                if (first) {
                    first = false
                    throw IllegalStateException("the field went away")
                }
                target.perform(step)
            }
        }
        val control = RemoteControl(failing, frameSize = { Size(100, 200) })
        control.start()
        control.input(event(InputEvent.KEY_DOWN, key = 0x08))
        Thread.sleep(100)
        control.input(event(InputEvent.KEY_DOWN, key = 0x2E))
        assertEquals(listOf<TouchStep>(TouchStep.Edit(EditKey.DELETE)), target.waitFor(1))
        control.stop()
    }
}
