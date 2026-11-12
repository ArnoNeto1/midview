package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageFramer
import java.io.Closeable

// One teacher's connection, after the TLS handshake: framed messages both
// ways (MessageFramer), and who the teacher is.
interface Channel : Closeable {
    // The teacher's certificate thumbprint (thumbprintOf): what decides what
    // that teacher may do here.
    val remoteThumbprint: String

    // The teacher's IP address as text ("192.168.0.10"; an IPv4 address
    // never in its IPv6-mapped form), as the PC Agent records a class
    // folders' home; empty when not known.
    val remoteAddress: String get() = ""

    // maxLength: for a teacher not trusted yet, a message whose type and
    // payload are longer is refused before it is read.
    fun receive(maxLength: Int = MessageFramer.MAX): Message

    fun send(message: Message)

    // How long a receive waits before the connection is given up.
    fun setTimeout(millis: Int)
}
