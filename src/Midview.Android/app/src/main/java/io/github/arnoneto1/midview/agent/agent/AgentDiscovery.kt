package io.github.arnoneto1.midview.agent.agent

import android.content.Context
import android.net.wifi.WifiManager
import io.github.arnoneto1.midview.agent.net.DiscoveryResponder
import io.github.arnoneto1.midview.agent.protocol.Discovery
import java.net.DatagramSocket
import java.net.InetSocketAddress

// The agent's answer to the teacher's search (UDP 28901): the socket, the
// thread that answers on it and the Wi-Fi's multicast lock, held while the
// agent listens and let go by close.
internal class AgentDiscovery(private val context: Context) {
    private var socket: DatagramSocket? = null
    private var multicastLock: WifiManager.MulticastLock? = null

    fun start(responder: DiscoveryResponder) {
        // Many phones drop broadcast packets to save battery unless
        // an app asks for them.
        multicastLock = (context.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager)
            .createMulticastLock("Midview discovery").apply {
                setReferenceCounted(false)
                acquire()
            }
        val socket = DatagramSocket(null).apply {
            reuseAddress = true
            broadcast = true
            bind(InetSocketAddress(Discovery.PORT))
        }
        this.socket = socket
        Thread({ responder.run(socket) }, "Midview discovery").apply { isDaemon = true }.start()
    }

    fun close() {
        socket?.close()
        socket = null
        multicastLock?.let { if (it.isHeld) it.release() }
        multicastLock = null
    }
}
