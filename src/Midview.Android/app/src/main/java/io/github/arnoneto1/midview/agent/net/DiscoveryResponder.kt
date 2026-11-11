package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.pairing.PairingFlow
import io.github.arnoneto1.midview.agent.protocol.Discovery
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket

// Answers the Master's "who's there?" (see Discovery), as the Windows
// DiscoveryResponder: this phone's name and thumbprint, and whom it trusts
// as the asking teacher sees it.
class DiscoveryResponder(
    private val pairing: PairingFlow,
    private val agentThumbprint: String,
    private val deviceName: () -> String,
) {
    fun answer(packet: ByteArray): ByteArray? {
        val probe = Discovery.parseProbe(packet) ?: return null
        return Discovery.encodeReply(
            Discovery.Reply(
                probe.id, deviceName(), agentThumbprint,
                pairing.teachersSeenBy(probe.masterThumbprint), pairing.newTeacherOpen,
            ),
        )
    }

    // Until the socket is closed. Each probe is answered to its sender.
    fun run(socket: DatagramSocket) {
        // One more than a packet may be: a bigger one shows as too big.
        val buffer = ByteArray(Discovery.MAX_PACKET_SIZE + 1)
        while (!socket.isClosed) {
            val received = DatagramPacket(buffer, buffer.size)
            try {
                socket.receive(received)
            } catch (e: IOException) {
                if (socket.isClosed) return
                continue
            }

            val reply = answer(buffer.copyOf(received.length)) ?: continue
            try {
                socket.send(DatagramPacket(reply, reply.size, received.socketAddress))
            } catch (e: IOException) {
                // The teacher's PC went away; nothing to do.
            }
        }
    }
}
