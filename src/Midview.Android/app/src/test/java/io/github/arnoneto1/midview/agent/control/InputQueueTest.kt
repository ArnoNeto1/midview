package io.github.arnoneto1.midview.agent.control

import io.github.arnoneto1.midview.agent.protocol.InputEvent
import org.junit.Assert.assertEquals
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test

class InputQueueTest {
    private fun event(kind: Int, x: Int = 0, y: Int = 0, key: Int = 0) = InputEvent(kind, x, y, key)

    @Test fun charactersKeysAndWheelNotchesInARowAreOneItem() {
        val queue = InputQueue()
        "olá".forEach { queue.put(event(InputEvent.TEXT, key = it.code), 0) }
        repeat(3) { queue.put(event(InputEvent.KEY_DOWN, key = 0x08), 0) }
        queue.put(event(InputEvent.KEY_UP, key = 0x08), 0)
        queue.put(event(InputEvent.KEY_DOWN, key = 0x0D), 0)
        queue.put(event(InputEvent.WHEEL, -120), 0)
        queue.put(event(InputEvent.WHEEL, -240), 0)
        queue.put(event(InputEvent.TEXT, key = 'x'.code), 0)
        val items = queue.takeAll()
        assertEquals(5, items.size)
        assertEquals("olá", (items[0] as InputQueue.Text).text.toString())
        assertEquals(0x08 to 3, (items[1] as InputQueue.Key).let { it.key to it.count })
        assertEquals(0x0D to 1, (items[2] as InputQueue.Key).let { it.key to it.count })
        assertEquals(-360, (items[3] as InputQueue.Wheel).delta)
        assertEquals("x", (items[4] as InputQueue.Text).text.toString())
    }

    @Test fun pastCapacityTheOldestMovesGoFirstAndNothingElse() {
        val queue = InputQueue(capacity = 4)
        queue.put(event(InputEvent.LEFT_DOWN, 1, 1), 0)
        for (y in 2..10) queue.put(event(InputEvent.MOUSE_MOVE, 1, y), 0)
        queue.put(event(InputEvent.KEY_DOWN, key = 0x1B), 0)
        queue.put(event(InputEvent.LEFT_UP, 1, 10), 0)
        val items = queue.takeAll()
        assertEquals(4, items.size)
        assertEquals(InputEvent.LEFT_DOWN, (items[0] as InputQueue.Event).event.kind)
        assertEquals(10, (items[1] as InputQueue.Event).event.y)
        assertEquals(0x1B, (items[2] as InputQueue.Key).key)
        assertEquals(InputEvent.LEFT_UP, (items[3] as InputQueue.Event).event.kind)
        assertEquals(8, queue.droppedMoves)
        assertEquals(0, queue.refused)
    }

    @Test fun withNoMoveToDropItGrowsToTwiceTheCapacityThenRefusesAndCounts() {
        val queue = InputQueue(capacity = 2)
        repeat(6) { queue.put(event(InputEvent.KEY_DOWN, key = 0x70 + it), 0) }
        assertEquals(4, queue.size)
        assertEquals(2, queue.refused)
    }

    @Test fun textPastTheLimitIsRefusedAndCounted() {
        val queue = InputQueue()
        repeat(InputQueue.TEXT_LIMIT + 5) { queue.put(event(InputEvent.TEXT, key = 'a'.code), 0) }
        assertEquals(InputQueue.TEXT_LIMIT, (queue.takeAll().single() as InputQueue.Text).text.length)
        assertEquals(5, queue.refused)
    }

    @Test fun stopComesAfterWhatIsWaitingAndClosesTheQueue() {
        val queue = InputQueue()
        queue.put(event(InputEvent.TEXT, key = 'a'.code), 0)
        queue.stop()
        queue.put(event(InputEvent.TEXT, key = 'b'.code), 0)
        val items = queue.takeAll()
        assertEquals(2, items.size)
        assertSame(InputQueue.Stop, items[1])
        assertTrue(items[0] is InputQueue.Text)
    }
}
