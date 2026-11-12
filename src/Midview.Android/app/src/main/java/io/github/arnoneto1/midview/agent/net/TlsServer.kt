package io.github.arnoneto1.midview.agent.net

import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.thumbprintOf
import io.github.arnoneto1.midview.agent.protocol.Message
import io.github.arnoneto1.midview.agent.protocol.MessageFramer
import io.github.arnoneto1.midview.agent.protocol.Pairing
import java.io.BufferedInputStream
import java.io.Closeable
import java.io.IOException
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.security.KeyStore
import java.security.SecureRandom
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.concurrent.RejectedExecutionException
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.ScheduledThreadPoolExecutor
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference
import javax.net.ssl.KeyManager
import javax.net.ssl.KeyManagerFactory
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLEngine
import javax.net.ssl.SSLSocket
import javax.net.ssl.X509ExtendedTrustManager

// A TLS listener as the Windows Agent's (SecureChannel.
// AuthenticateAsServerAcceptingAnyClient): the phone shows its own
// certificate, every teacher must show one, and any certificate gets through
// the handshake. What a teacher may then do is decided from its thumbprint,
// by the handler. Each connection runs on a thread of its own, so a
// connection that opens and then sends nothing holds up no other.
//
// Nobody on the network may keep the teacher out by filling the
// connections (ConnectionAdmission says who may hold one): a connection not
// known as a trusted teacher's (isTrusted, asked with the handshake's
// certificate) is closed strangerDeadlineMillis after it was accepted,
// however slowly it keeps sending, unless it started a pairing
// (Channel.pairingStarted), which then has as long as a pairing takes.
class TlsServer(
    identity: AgentIdentity,
    private val port: Int,
    private val bindAddress: InetAddress? = null,
    private val isTrusted: (String) -> Boolean = { false },
    private val strangerDeadlineMillis: Long = STRANGER_DEADLINE_MILLIS,
    private val admission: ConnectionAdmission = ConnectionAdmission(clock = ::monotonicMillis),
    private val handler: (Channel) -> Unit,
) : Closeable {
    private val context = SSLContext.getInstance("TLS").apply {
        init(keyManagers(identity), arrayOf(AcceptAnyCertificate), SecureRandom())
    }
    private val deadlines = ScheduledThreadPoolExecutor(1) { Thread(it, "Midview TLS $port deadlines").apply { isDaemon = true } }
        .apply { removeOnCancelPolicy = true }

    @Volatile private var listener: ServerSocket? = null

    // Starts listening; returns the port (the one picked, for port 0). The
    // listener takes plain TCP, and TLS is layered over each connection
    // (serve): the deadline closes the plain socket, which ends a handshake
    // under way at once (closing a TLS socket can wait for that handshake).
    fun start(): Int {
        val socket = ServerSocket()
        try {
            socket.reuseAddress = true
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
        deadlines.shutdownNow()
    }

    private fun acceptLoop(socket: ServerSocket) {
        while (!socket.isClosed) {
            val client = try {
                socket.accept()
            } catch (e: IOException) {
                if (socket.isClosed) return
                // A failure that repeats (no file handles left, say) must
                // not keep the CPU busy.
                Thread.sleep(ACCEPT_RETRY_MILLIS)
                continue
            }
            val address = client.inetAddress?.hostAddress.orEmpty()
            val seat = admission.admit(address)
            if (seat == null) {
                // Too many from that address, or from strangers at all:
                // something is flooding.
                closeQuietly(client)
                continue
            }
            try {
                Thread({ serve(client, address, seat) }, "Midview TLS $port client").apply { isDaemon = true }.start()
            } catch (e: Throwable) {
                admission.release(address, seat)
                closeQuietly(client)
            }
        }
    }

    private fun serve(plain: Socket, address: String, admitted: ConnectionAdmission.Seat) {
        var seat: ConnectionAdmission.Seat? = admitted
        val deadline = AtomicReference(closeIn(plain, strangerDeadlineMillis))
        try {
            // A handshake that never finishes is dropped quickly.
            plain.soTimeout = HANDSHAKE_TIMEOUT_MILLIS
            plain.tcpNoDelay = true
            val socket = context.socketFactory.createSocket(plain, address, plain.port, true) as SSLSocket
            socket.use {
                socket.useClientMode = false
                socket.needClientAuth = true
                socket.enabledProtocols = modernProtocols(socket.supportedProtocols)
                socket.startHandshake()
                val teacher = socket.session.peerCertificates.firstOrNull() as? X509Certificate ?: return
                val thumbprint = thumbprintOf(teacher)
                if (trusts(thumbprint)) {
                    seat = admission.trust(address, admitted)
                    // The teachers' pool is full; the old seat is given back already.
                    if (seat == null) return
                    deadline.getAndSet(null)?.cancel(false)
                }
                handler(
                    SocketChannel(socket, thumbprint) {
                        deadline.getAndSet(closeIn(plain, PAIRING_DEADLINE_MILLIS))?.cancel(false)
                    },
                )
            }
        } catch (e: Exception) {
            // The teacher went away, timed out or spoke nonsense: that
            // connection ends, the others go on.
        } finally {
            deadline.getAndSet(null)?.cancel(false)
            closeQuietly(plain)
            seat?.let { admission.release(address, it) }
        }
    }

    private fun trusts(thumbprint: String): Boolean =
        try {
            isTrusted(thumbprint)
        } catch (e: Exception) {
            false
        }

    // The connection closed after millis, unless cancelled before; null
    // (and closed now) when the server is closing anyway.
    private fun closeIn(socket: Socket, millis: Long): ScheduledFuture<*>? =
        try {
            deadlines.schedule({ closeQuietly(socket) }, millis, TimeUnit.MILLISECONDS)
        } catch (e: RejectedExecutionException) {
            closeQuietly(socket)
            null
        }

    private class SocketChannel(
        private val socket: SSLSocket,
        override val remoteThumbprint: String,
        private val onPairing: () -> Unit,
    ) : Channel {
        private val input = BufferedInputStream(socket.inputStream)
        private val output = socket.outputStream

        override fun receive(maxLength: Int): Message = MessageFramer.read(input, maxLength)

        override fun send(message: Message) = MessageFramer.write(output, message)

        override fun setTimeout(millis: Int) {
            socket.soTimeout = millis
        }

        override fun pairingStarted() = onPairing()

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

        // From the accept to a trusted teacher's certificate: a LAN
        // handshake takes well under a second.
        const val STRANGER_DEADLINE_MILLIS = 15_000L

        // A pairing waits for the teacher to compare the codes.
        const val PAIRING_DEADLINE_MILLIS = Pairing.CONFIRM_TIMEOUT_MILLIS + 60_000L

        private const val BACKLOG = 50
        private const val ACCEPT_RETRY_MILLIS = 1000L
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

        private fun monotonicMillis(): Long = System.nanoTime() / 1_000_000

        private fun closeQuietly(socket: Socket) {
            try {
                socket.close()
            } catch (e: IOException) {
                // Nothing to do.
            }
        }
    }
}
