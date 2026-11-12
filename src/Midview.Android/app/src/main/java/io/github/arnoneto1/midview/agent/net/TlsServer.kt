package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.thumbprintOf
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageFramer
import java.io.BufferedInputStream
import java.io.Closeable
import java.io.IOException
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket
import java.security.KeyStore
import java.security.SecureRandom
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.concurrent.Semaphore
import javax.net.ssl.KeyManager
import javax.net.ssl.KeyManagerFactory
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLEngine
import javax.net.ssl.SSLServerSocket
import javax.net.ssl.SSLSocket
import javax.net.ssl.X509ExtendedTrustManager

// A TLS listener as the Windows Agent's (SecureChannel.
// AuthenticateAsServerAcceptingAnyClient): the phone shows its own
// certificate, every teacher must show one, and any certificate gets through
// the handshake. What a teacher may then do is decided from its thumbprint,
// by the handler. Each connection runs on a thread of its own, so a
// connection that opens and then sends nothing holds up no other.
class TlsServer(
    identity: AgentIdentity,
    private val port: Int,
    private val bindAddress: InetAddress? = null,
    private val handler: (Channel) -> Unit,
) : Closeable {
    private val context = SSLContext.getInstance("TLS").apply {
        init(keyManagers(identity), arrayOf(AcceptAnyCertificate), SecureRandom())
    }
    private val connections = Semaphore(MAX_CONNECTIONS)

    @Volatile private var listener: SSLServerSocket? = null

    // Starts listening; returns the port (the one picked, for port 0).
    fun start(): Int {
        val socket = context.serverSocketFactory.createServerSocket() as SSLServerSocket
        try {
            socket.reuseAddress = true
            socket.needClientAuth = true
            socket.enabledProtocols = modernProtocols(socket.supportedProtocols)
            socket.bind(InetSocketAddress(bindAddress, port), BACKLOG)
        } catch (e: IOException) {
            socket.close()
            throw e
        }
        listener = socket
        Thread({ acceptLoop(socket) }, "Midview TLS $port").apply { isDaemon = true }.start()
        return socket.localPort
    }

    override fun close() {
        listener?.close()
        listener = null
    }

    private fun acceptLoop(socket: SSLServerSocket) {
        while (!socket.isClosed) {
            val client = try {
                socket.accept() as SSLSocket
            } catch (e: IOException) {
                if (socket.isClosed) return
                // A failure that repeats (no file handles left, say) must
                // not keep the CPU busy.
                Thread.sleep(ACCEPT_RETRY_MILLIS)
                continue
            }
            if (!connections.tryAcquire()) {
                // Far more than a classroom opens: something is flooding.
                closeQuietly(client)
                continue
            }
            Thread({
                try {
                    serve(client)
                } finally {
                    connections.release()
                }
            }, "Midview TLS $port client").apply { isDaemon = true }.start()
        }
    }

    private fun serve(socket: SSLSocket) {
        try {
            socket.use {
                // A handshake that never finishes is dropped quickly.
                socket.soTimeout = HANDSHAKE_TIMEOUT_MILLIS
                socket.tcpNoDelay = true
                socket.startHandshake()
                val teacher = socket.session.peerCertificates.firstOrNull() as? X509Certificate ?: return
                handler(SocketChannel(socket, thumbprintOf(teacher)))
            }
        } catch (e: Exception) {
            // The teacher went away, timed out or spoke nonsense: that
            // connection ends, the others go on.
        }
    }

    private class SocketChannel(private val socket: SSLSocket, override val remoteThumbprint: String) : Channel {
        private val input = BufferedInputStream(socket.inputStream)
        private val output = socket.outputStream

        override fun receive(maxLength: Int): Message = MessageFramer.read(input, maxLength)

        override fun send(message: Message) = MessageFramer.write(output, message)

        override fun setTimeout(millis: Int) {
            socket.soTimeout = millis
        }

        override fun close() = socket.close()
    }

    // Lets every certificate through the handshake (the thumbprint decides
    // later) but still insists that the teacher shows one.
    private object AcceptAnyCertificate : X509ExtendedTrustManager() {
        override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) = requireOne(chain)
        override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?, socket: Socket?) = requireOne(chain)
        override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?, engine: SSLEngine?) = requireOne(chain)

        // The phone never connects out as a TLS client.
        override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) = refuse()
        override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?, socket: Socket?) = refuse()
        override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?, engine: SSLEngine?) = refuse()

        // Empty: the teacher may send any certificate it has.
        override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()

        private fun requireOne(chain: Array<out X509Certificate>?) {
            if (chain.isNullOrEmpty()) throw CertificateException("No certificate")
        }

        private fun refuse(): Unit = throw CertificateException("Not a TLS client")
    }

    companion object {
        // The Windows Agent's TimeoutMs: generous for a LAN round trip.
        const val HANDSHAKE_TIMEOUT_MILLIS = 10_000

        private const val BACKLOG = 50
        private const val ACCEPT_RETRY_MILLIS = 1000L
        private const val MAX_CONNECTIONS = 64
        private const val ALIAS = "midview-agent"

        // TLS 1.2 and 1.3, as far as the platform has them: what the
        // Master's Windows speaks, and nothing older.
        fun modernProtocols(supported: Array<String>): Array<String> =
            supported.filter { it == "TLSv1.2" || it == "TLSv1.3" }.sorted().toTypedArray()

        // The identity's key and certificate, for a TLS context.
        fun keyManagers(identity: AgentIdentity): Array<KeyManager> {
            // Only in memory, never written: any password does.
            val secret = "midview".toCharArray()
            val store = KeyStore.getInstance("PKCS12").apply {
                load(null, null)
                setKeyEntry(ALIAS, identity.privateKey, secret, arrayOf(identity.certificate))
            }
            return KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm()).apply {
                init(store, secret)
            }.keyManagers
        }

        private fun closeQuietly(socket: Socket) {
            try {
                socket.close()
            } catch (e: IOException) {
                // Nothing to do.
            }
        }
    }
}
