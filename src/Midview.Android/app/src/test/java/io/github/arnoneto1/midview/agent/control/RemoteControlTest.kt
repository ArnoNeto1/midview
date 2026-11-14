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

        val locks: MutableList<Boolean> = Collections.synchronizedList(mutableListOf())
        override fun lockStudent(on: Boolean) {
            locks += on
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

    // "Travar o toque do aluno" asked before control: on at the start.
    @Test fun theTouchLockAskedBeforeComesOnWithControl() {
        control.lockTouch(true)
        assertTrue(target.locks.isEmpty())
        control.start()
        assertEquals(listOf(true), target.locks.toList())
        control.stopAndWait()
        assertEquals(listOf(true, false), target.locks.toList())
    }

    @Test fun theTeacherTogglesTheTouchLockWhileControlling() {
        control.start()
        control.lockTouch(true)
        control.lockTouch(false)
        assertEquals(listOf(true, false), target.locks.toList())
        control.stopAndWait()
    }

    // However control ends, the student's touch is freed.
    @Test fun theEndOfControlAlwaysFreesTheTouch() {
        control.start()
        control.lockTouch(true)
        control.stopAndWait()
        assertEquals(false, target.locks.last())
        assertEquals("hide", target.notice.last())
    }

    // A teacher's window from before it never sends it: the touch stays free.
    @Test fun withoutTheToggleTheTouchIsNeverLocked() {
        control.start()
        control.stopAndWait()
        assertFalse(target.locks.contains(true))
    }

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
        control.stopAndWait()
        control.stopAndWait()
        assertFalse(control.controlling)
        assertEquals(listOf("show", "hide"), target.notice)
    }

    @Test fun inputBeforeStartIsIgnored() {
        control.input(event(InputEvent.LEFT_DOWN, 10, 10))
        assertNull(control.start())
        control.input(event(InputEvent.KEY_DOWN, key = 0x1B))
        assertEquals(listOf<TouchStep>(TouchStep.Global(GlobalKey.BACK)), target.waitFor(1))
        control.stopAndWait()
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
        control.stopAndWait()
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
        control.stopAndWait()
    }

    @Test fun stoppingMidDragLiftsTheFinger() {
        control.start()
        control.input(event(InputEvent.LEFT_DOWN, 10, 10))
        control.input(event(InputEvent.MOUSE_MOVE, 10, 50))
        target.waitFor(2)
        control.stopAndWait()
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
        control.stopAndWait()
        assertFalse(target.done.any { it is TouchStep.Global })
    }

    @Test fun controlCanStartAgainAfterStopping() {
        control.start()
        control.stopAndWait()
        assertNull(control.start())
        control.input(event(InputEvent.KEY_DOWN, key = 0x08))
        assertEquals(listOf<TouchStep>(TouchStep.Edit(EditKey.BACKSPACE)), target.waitFor(1))
        control.stopAndWait()
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
        control.stopAndWait()
    }

    // "Parar de controlar" answers at once, even with a long gesture under way;
    // the gesture finishes and the strip goes after.
    @Test fun stopDoesNotWaitForTheGestureUnderWay() {
        val gate = CountDownLatch(1)
        target.slow = gate
        control.start()
        control.input(event(InputEvent.RIGHT_DOWN, 10, 10))
        Thread.sleep(100)
        val began = System.nanoTime()
        control.stop()
        assertTrue((System.nanoTime() - began) / 1_000_000 < 200)
        assertFalse(control.controlling)
        assertEquals(listOf("show"), target.notice)
        gate.countDown()
        target.slow = null
        control.stopAndWait()
        assertEquals(listOf("show", "hide"), target.notice)
    }

    // Started again right after a stop: the old finger finishes first, never two at once.
    @Test fun startingAgainWaitsForTheStoppedControl() {
        val gate = CountDownLatch(1)
        target.slow = gate
        control.start()
        control.input(event(InputEvent.RIGHT_DOWN, 10, 10))
        Thread.sleep(100)
        control.stop()
        Thread { Thread.sleep(150); gate.countDown() }.start()
        assertNull(control.start())
        assertEquals(listOf("show", "hide", "show"), target.notice)
        target.slow = null
        control.stopAndWait()
    }

    @Test fun manyCharactersArriveAsOneText() {
        val gate = CountDownLatch(1)
        target.slow = gate
        control.start()
        control.input(event(InputEvent.RIGHT_DOWN, 1, 1))
        Thread.sleep(100)
        repeat(20_000) { control.input(event(InputEvent.TEXT, key = 'a'.code + it % 26)) }
        gate.countDown()
        target.slow = null
        val typed = target.waitFor(2)[1] as TouchStep.Type
        assertEquals(20_000, typed.text.length)
        assertEquals("abc", typed.text.substring(0, 3))
        control.stopAndWait()
    }
}
