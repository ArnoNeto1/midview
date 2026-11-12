package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.lock.ScreenLock
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageType
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.net.SocketTimeoutException

class LockScreenHandlerTest {
    private val teacher = "A".repeat(40)
    private val events = mutableListOf<String>()
    private val lock = ScreenLock(show = { events += "show" }, hide = { events += "hide" })

    private fun heartbeat() = Message(MessageType.HEARTBEAT, ByteArray(0))

    private fun ack(channel: FakeChannel) = CommandResult.decode(channel.sent.single().payload)

    // "Desbloquear" (or the teacher's program closing) closes the
    // connection: the screen comes back at once.
    @Test fun connectionEndReleasesAtOnce() {
        val channel = FakeChannel(teacher, heartbeat(), heartbeat(), events = events)
        var lockedMeanwhile = true
        channel.onReceive = { lockedMeanwhile = lockedMeanwhile && lock.locked }

        LockScreenHandler(channel, lock, isDeviceOwner = { true }).run()

        assertEquals(true to "tela bloqueada", ack(channel))
        assertTrue(lockedMeanwhile)
        assertFalse(lock.locked)
        // Locked before the teacher reads "tela bloqueada"; then only heartbeats.
        assertEquals(
            listOf(
                "show",
                "send ${MessageType.COMMAND_ACK}",
                "receive ${MessageType.HEARTBEAT}",
                "receive ${MessageType.HEARTBEAT}",
                "hide",
            ),
            events,
        )
    }

    // The teacher's PC stopped answering: the receive gives up after 15 s
    // without a word, and the class is not left locked.
    @Test fun silenceOfFifteenSecondsReleases() {
        val channel = SilentChannel(heartbeats = 2)

        LockScreenHandler(channel, lock, isDeviceOwner = { true }).run()

        assertEquals(15_000, channel.timeoutAtFirstReceive)
        assertEquals(3, channel.receives)
        assertFalse(lock.locked)
        assertEquals(listOf("show", "hide"), events)
    }

    // A Master that reconnects overlaps the connection it replaces: the new
    // lock is accepted, and the screen stays locked until both have ended.
    @Test fun secondLockWhileLockedIsAccepted() {
        val earlier = lock.hold()
        val channel = FakeChannel(teacher, heartbeat())

        LockScreenHandler(channel, lock, isDeviceOwner = { true }).run()

        assertEquals(true to "tela bloqueada", ack(channel))
        assertTrue(lock.locked)
        assertEquals(listOf("show"), events)

        earlier.release()
        assertEquals(listOf("show", "hide"), events)
    }

    @Test fun notDeviceOwnerRefusesWithText() {
        val channel = FakeChannel(teacher, heartbeat())

        LockScreenHandler(channel, lock, isDeviceOwner = { false }).run()

        assertEquals(false to "este celular não bloqueia a tela", ack(channel))
        assertTrue(events.isEmpty())
        assertFalse(lock.locked)
        // Nothing more is read: the connection ends.
        assertTrue(channel.events.none { it.startsWith("receive") })
    }

    // The lock screen could not come up: the teacher reads why, nothing stays held.
    @Test fun aLockThatCannotShowIsRefused() {
        val failing = ScreenLock(show = { throw IllegalStateException("no screen") }, hide = { events += "hide" })
        val channel = FakeChannel(teacher, heartbeat())

        LockScreenHandler(channel, failing, isDeviceOwner = { true }).run()

        assertEquals(false to "a tela não pôde ser bloqueada", ack(channel))
        assertFalse(failing.locked)
        assertTrue(events.isEmpty())
    }

    // Heartbeats, then a receive that times out as a silent socket does.
    private class SilentChannel(private var heartbeats: Int) : Channel {
        override val remoteThumbprint = "A".repeat(40)
        private var timeout = 0
        var timeoutAtFirstReceive = 0
        var receives = 0

        override fun receive(maxLength: Int): Message {
            if (receives++ == 0) timeoutAtFirstReceive = timeout
            if (heartbeats-- > 0) return Message(MessageType.HEARTBEAT, ByteArray(0))
            throw SocketTimeoutException("Read timed out")
        }

        override fun send(message: Message) = Unit

        override fun setTimeout(millis: Int) {
            timeout = millis
        }

        override fun close() = Unit
    }
}
