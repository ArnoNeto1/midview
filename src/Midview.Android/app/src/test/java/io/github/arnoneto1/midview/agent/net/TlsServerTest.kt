package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.thumbprintOf
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageFramer
import io.github.arnoneto1.midview.agent.protocol.MessageType
import org.junit.After
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import java.net.InetAddress
import java.nio.file.Files
import java.security.SecureRandom
import java.security.cert.X509Certificate
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import javax.net.ssl.KeyManager
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLException
import javax.net.ssl.SSLSocket
import javax.net.ssl.X509TrustManager

class TlsServerTest {
    private val seen = LinkedBlockingQueue<String>()
    private val server = TlsServer(phone, 0, InetAddress.getLoopbackAddress()) { channel ->
        seen += channel.remoteThumbprint
        val message = channel.receive()
        channel.send(Message(MessageType.HEARTBEAT, message.payload))
    }

    @After fun stop() = server.close()

    // The teacher's side: its own certificate, any phone certificate (it
    // pins the thumbprint itself).
    private fun connect(port: Int, keyManagers: Array<KeyManager>?): SSLSocket {
        val acceptAny = object : X509TrustManager {
            override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) = Unit
            override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) = Unit
            override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
        }
        val context = SSLContext.getInstance("TLS").apply { init(keyManagers, arrayOf(acceptAny), SecureRandom()) }
        return (context.socketFactory.createSocket(InetAddress.getLoopbackAddress(), port) as SSLSocket).apply {
            soTimeout = 10_000
        }
    }

    @Test fun theTeachersThumbprintIsKnownAfterTheHandshake() {
        val port = server.start()
        connect(port, TlsServer.keyManagers(teacher)).use { socket ->
            socket.startHandshake()
            assertEquals(phone.thumbprint, thumbprintOf(socket.session.peerCertificates[0] as X509Certificate))

            MessageFramer.write(socket.outputStream, Message(MessageType.HEARTBEAT, byteArrayOf(7)))
            val echo = MessageFramer.read(socket.inputStream)
            assertEquals(Message(MessageType.HEARTBEAT, byteArrayOf(7)), echo)
        }
        assertEquals(teacher.thumbprint, seen.poll(5, TimeUnit.SECONDS))
    }

    @Test fun onlyTls12And13AreOffered() {
        assertArrayEquals(
            arrayOf("TLSv1.2", "TLSv1.3"),
            TlsServer.modernProtocols(arrayOf("SSLv3", "TLSv1", "TLSv1.1", "TLSv1.2", "TLSv1.3")),
        )
        assertArrayEquals(arrayOf("TLSv1.2"), TlsServer.modernProtocols(arrayOf("TLSv1", "TLSv1.2")))
    }

    @Test fun aTls11TeacherIsTurnedAway() {
        val port = server.start()
        connect(port, TlsServer.keyManagers(teacher)).use { socket ->
            socket.enabledProtocols = arrayOf("TLSv1.1")
            try {
                socket.startHandshake()
                fail("TLS 1.1 got through")
            } catch (e: java.io.IOException) {
                // Refused.
            }
        }
        assertTrue(seen.poll(1, TimeUnit.SECONDS) == null)
    }

    @Test fun aTeacherWithoutACertificateIsTurnedAway() {
        val port = server.start()
        connect(port, null).use { socket ->
            try {
                socket.startHandshake()
                MessageFramer.write(socket.outputStream, Message(MessageType.HEARTBEAT, ByteArray(0)))
                MessageFramer.read(socket.inputStream)
                fail("A connection without a certificate got through")
            } catch (e: SSLException) {
                // Refused in the handshake.
            } catch (e: java.io.IOException) {
                // Or the connection was closed right after it.
            }
        }
        assertTrue(seen.poll(1, TimeUnit.SECONDS) == null)
    }

    @Test fun closeStopsListening() {
        val port = server.start()
        server.close()
        try {
            connect(port, TlsServer.keyManagers(teacher)).use { it.startHandshake() }
            fail("Still listening")
        } catch (e: java.io.IOException) {
            // Nobody there.
        }
    }

    private companion object {
        val phone: AgentIdentity by lazy {
            AgentIdentity(Files.createTempDirectory("tls-phone").toFile().apply { deleteOnExit() }, "Cel 01")
        }
        val teacher: AgentIdentity by lazy {
            AgentIdentity(Files.createTempDirectory("tls-teacher").toFile().apply { deleteOnExit() }, "PROF-01")
        }
    }
}
