package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.protocol.Message
import java.io.EOFException
import java.io.IOException

// A teacher's connection played from a script: receive hands out the
// scripted messages in order, then fails as a closed connection does; what
// the agent sends is kept. events records both, in order, for the tests
// that care what happened before what.
class FakeChannel(
    override val remoteThumbprint: String,
    vararg incoming: Message,
    val events: MutableList<String> = mutableListOf(),
) : Channel {
    private val queue = ArrayDeque(incoming.toList())

    val sent = mutableListOf<Message>()
    var timeoutMillis = 0
        private set
    var closed = false
        private set

    // Called with each message just before receive returns it.
    var onReceive: (Message) -> Unit = {}

    override fun receive(maxLength: Int): Message {
        val next = queue.removeFirstOrNull() ?: throw EOFException("No more scripted messages")
        if (next.payload.size + 1 > maxLength) {
            throw IOException("Invalid message length ${next.payload.size + 1}")
        }
        events += "receive ${next.type}"
        onReceive(next)
        return next
    }

    override fun send(message: Message) {
        events += "send ${message.type}"
        sent += message
    }

    override fun setTimeout(millis: Int) {
        timeoutMillis = millis
    }

    // pairingStarted was called.
    var pairing = false
        private set

    override fun pairingStarted() {
        pairing = true
    }

    override fun close() {
        closed = true
    }
}
