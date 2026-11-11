package io.github.arnoneto1.midview.agent.screen

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class FrameCreditTest {
    @Test fun creditAllowsTwoUnackedThenWaits() {
        val credit = FrameCredit()
        assertEquals(1, credit.nextId())
        assertTrue(credit.hasRoom())
        credit.sent(1)
        assertEquals(2, credit.nextId())
        assertTrue(credit.hasRoom())
        credit.sent(2)
        assertEquals(3, credit.nextId())
        assertFalse(credit.hasRoom())
        assertEquals(2, credit.unacked())
    }

    @Test fun ackOfNewestAcksOlder() {
        val credit = FrameCredit()
        credit.sent(credit.nextId())
        credit.sent(credit.nextId())
        credit.ack(2)
        assertEquals(0, credit.unacked())
        assertTrue(credit.hasRoom())
    }

    @Test fun splitUpdateIsAckedByItsLastId() {
        val credit = FrameCredit()
        val first = credit.nextId() // update of 3 messages: ids 1..3
        assertEquals(1, first)
        credit.sent(first + 2)
        assertEquals(4, credit.nextId())
        credit.ack(2) // only part of the split update
        assertEquals(1, credit.unacked())
        credit.ack(3)
        assertEquals(0, credit.unacked())
        credit.ack(3) // repeat does nothing
        assertEquals(0, credit.unacked())
    }

    @Test fun awaitRoomReturnsAtOnceWhenThereIsRoomAndWakesOnAck() {
        val credit = FrameCredit()
        assertTrue(credit.awaitRoom(10_000))
        credit.sent(credit.nextId())
        credit.sent(credit.nextId())
        val acker = Thread { Thread.sleep(50); credit.ack(1) }
        acker.start()
        val start = System.nanoTime()
        assertTrue(credit.awaitRoom(10_000))
        assertTrue((System.nanoTime() - start) / 1_000_000 < 5_000)
        assertEquals(1, credit.unacked())
        acker.join()
    }

    @Test fun noRoomForOneSecondDropsOldest() {
        val credit = FrameCredit()
        credit.sent(credit.nextId())
        credit.sent(credit.nextId())
        val start = System.nanoTime()
        assertTrue(credit.awaitRoom(150)) // timed out: gave up on the oldest and carries on
        assertTrue((System.nanoTime() - start) / 1_000_000 >= 140)
        assertEquals(1, credit.unacked())
        credit.ack(2)
        assertEquals(0, credit.unacked())

        credit.sent(credit.nextId())
        credit.sent(credit.nextId())
        credit.dropOldest()
        assertEquals(1, credit.unacked())
        credit.dropOldest()
        credit.dropOldest() // nothing left: harmless
        assertEquals(0, credit.unacked())
    }
}
