package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.pairing.PairingFlow
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.nio.file.Files
import kotlin.concurrent.thread

class DiscoveryResponderTest {
    @get:Rule val folder = TemporaryFolder()

    private val teacher = "A".repeat(40)
    private val other = "B".repeat(40)
    private val probeId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301"

    private val trusted by lazy { TrustedMasters(File(folder.newFolder(), "trusted-masters.txt")) }
    private val pairing by lazy { PairingFlow(identity, trusted, { _, _ -> true }, {}, deviceName = { "Cel 01" }, elapsed = { 1_000L }) }
    private val responder by lazy { DiscoveryResponder(pairing, identity.thumbprint) { "Cel 01" } }

    // What the Master broadcasts (.NET's Discovery.Encode(DiscoveryProbe)).
    private fun probe(master: String) =
        """{"midview":1,"probe":"$probeId","master":"$master"}""".toByteArray(Charsets.UTF_8)

    private fun answer(master: String) = JSONObject(String(responder.answer(probe(master))!!, Charsets.UTF_8))

    @Test fun aProbeGetsThePhone() {
        val reply = answer(teacher)
        assertEquals(probeId, reply.getString("probe"))
        assertEquals("Cel 01", reply.getString("name"))
        assertEquals(identity.thumbprint, reply.getString("agent"))
        assertEquals("android", reply.getString("kind"))
        assertEquals("none", reply.getString("teachers"))
        assertFalse(reply.getBoolean("newTeacherOpen"))
    }

    @Test fun teachersAsTheAskingTeacherSeesThem() {
        trusted.add(teacher)
        assertEquals("you", answer(teacher).getString("teachers"))
        assertEquals("others", answer(other).getString("teachers"))
    }

    @Test fun theOpenWindowIsTold() {
        trusted.add(teacher)
        pairing.allowNewTeacher("K7PQ2M94".toByteArray(Charsets.US_ASCII))
        assertTrue(answer(other).getBoolean("newTeacherOpen"))
    }

    @Test fun anythingButAProbeIsIgnored() {
        assertNull(responder.answer(ByteArray(0)))
        assertNull(responder.answer("hello".toByteArray()))
        assertNull(responder.answer(responder.answer(probe(teacher))!!))
    }

    @Test fun answersTheSenderOverUdp() {
        val socket = DatagramSocket(InetSocketAddress(InetAddress.getLoopbackAddress(), 0))
        val worker = thread { responder.run(socket) }
        try {
            DatagramSocket().use { teacherSocket ->
                teacherSocket.soTimeout = 5000
                val packet = probe(teacher)
                teacherSocket.send(DatagramPacket(packet, packet.size, InetAddress.getLoopbackAddress(), socket.localPort))
                val buffer = ByteArray(4096)
                val reply = DatagramPacket(buffer, buffer.size)
                teacherSocket.receive(reply)
                assertEquals("Cel 01", JSONObject(String(buffer, 0, reply.length, Charsets.UTF_8)).getString("name"))
            }
        } finally {
            socket.close()
            worker.join(5000)
        }
        assertFalse(worker.isAlive)
    }

    private companion object {
        val identity: AgentIdentity by lazy {
            AgentIdentity(Files.createTempDirectory("discovery-responder").toFile().apply { deleteOnExit() }, "Cel 01")
        }
    }
}
