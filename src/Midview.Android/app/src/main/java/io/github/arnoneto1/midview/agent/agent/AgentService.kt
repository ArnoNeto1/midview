package io.github.arnoneto1.midview.agent.agent

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.wifi.WifiManager
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.util.Log
import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.net.CommandHandler
import io.github.arnoneto1.midview.agent.net.DiscoveryResponder
import io.github.arnoneto1.midview.agent.net.ScreenStreamHandler
import io.github.arnoneto1.midview.agent.net.ThumbnailHandler
import io.github.arnoneto1.midview.agent.net.TlsServer
import io.github.arnoneto1.midview.agent.pairing.PairingFlow
import io.github.arnoneto1.midview.agent.policy.AndroidDevicePolicyGateway
import io.github.arnoneto1.midview.agent.protocol.Discovery
import io.github.arnoneto1.midview.agent.screen.BitmapJpeg
import io.github.arnoneto1.midview.agent.screen.CaptureCadence
import io.github.arnoneto1.midview.agent.screen.FrameCredit
import io.github.arnoneto1.midview.agent.screen.ScreenCaptureService
import io.github.arnoneto1.midview.agent.screen.ThumbnailPicture
import io.github.arnoneto1.midview.agent.status.StatusImage
import io.github.arnoneto1.midview.agent.ui.PairingCodeActivity
import java.io.File
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

// The Midview agent on the phone: always running (a foreground service with
// its fixed notification, started again after every restart), it answers
// the teacher's search (UDP 28901), takes pairing and commands (TLS 28900)
// and serves the screen (TLS 5901: the grid's thumbnail and the big
// window), the same ports as on a PC.
class AgentService : Service() {
    private val lock = Any()
    private var tlsServers = emptyList<TlsServer>()
    private var discoverySocket: DatagramSocket? = null
    private var multicastLock: WifiManager.MulticastLock? = null
    private var wifiLock: WifiManager.WifiLock? = null
    private val main = Handler(Looper.getMainLooper())
    private var stopped = false
    private val stopping = CountDownLatch(1)

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        createChannels()
        ScreenCaptureService.init(this)
        goForeground(STATUS_TEXT)
        holdWifi()
        // Making the identity the first time takes a moment (an RSA key).
        Thread({ startAgentUntilItRuns() }, "Midview agent start").start()
    }

    // The school phone's locks, again at every start (nothing on a phone
    // that is not the school's Device Owner). Made Device Owner by
    // `dpm set-device-owner`, the app is told (and starts the agent) before
    // it really is one: the locks are tried once more a little later.
    // Applying them twice changes nothing.
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        AndroidDevicePolicyGateway.lockDown(this)
        main.removeCallbacks(lockAgain)
        main.postDelayed(lockAgain, LOCK_AGAIN_MILLIS)
        return START_STICKY
    }

    private val lockAgain = Runnable { AndroidDevicePolicyGateway.lockDown(this) }

    override fun onDestroy() {
        synchronized(lock) {
            stopped = true
            closeListeners()
        }
        stopping.countDown()
        main.removeCallbacks(lockAgain)
        wifiLock?.let { if (it.isHeld) it.release() }
        wifiLock = null
        PairingCodeActivity.hide()
        super.onDestroy()
    }

    // A port still held (an agent just stopped) or no storage yet: tried
    // again every little while, as long as the service runs.
    private fun startAgentUntilItRuns() {
        while (true) {
            try {
                startAgent()
                return
            } catch (e: Exception) {
                Log.e(TAG, "Agent could not start; trying again soon", e)
                synchronized(lock) { closeListeners() }
                if (stopping.await(RETRY_MILLIS, TimeUnit.MILLISECONDS)) return
            }
        }
    }

    // Many phones put the Wi-Fi to sleep with the screen off, and the
    // teacher's search and commands would go unanswered.
    private fun holdWifi() {
        val wifi = applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
        wifiLock = wifi.createWifiLock(wifiLockMode(Build.VERSION.SDK_INT), "Midview agent").apply {
            setReferenceCounted(false)
            acquire()
        }
    }

    private fun closeListeners() {
        tlsServers.forEach { it.close() }
        tlsServers = emptyList()
        discoverySocket?.close()
        discoverySocket = null
        multicastLock?.let { if (it.isHeld) it.release() }
        multicastLock = null
    }

    private fun startAgent() {
        val name = DeviceName.read(this)
        val identity = AgentIdentity(filesDir, name)
        if (identity.replaced) {
            goForeground(REPLACED_TEXT)
        }
        val trusted = TrustedMasters(File(filesDir, TRUSTED_MASTERS_FILE))
        val pairing = PairingFlow(
            identity, trusted,
            showCode = { code, teacher -> showCode(code, teacher) },
            hideCode = { hideCode() },
            deviceName = { DeviceName.read(this) },
        )
        val consent = ScreenCaptureService.consent
        val commands = CommandHandler(trusted, pairing, consent)
        val frames = ScreenCaptureService.frames
        val picture = ThumbnailPicture(
            consent, frames,
            status = { text, width, height -> StatusImage.render(width, height, DeviceName.read(this), text) },
            jpeg = BitmapJpeg::thumbnail,
        )
        val thumbnails = ThumbnailHandler(
            trusted,
            render = picture::render,
            teacherConnected = consent::teacherConnected,
            stream = { ScreenStreamHandler(it, frames, FrameCredit(), CaptureCadence(), BitmapJpeg).run() },
        )
        val responder = DiscoveryResponder(pairing, identity.thumbprint) { DeviceName.read(this) }

        synchronized(lock) {
            if (stopped) return
            val agentServer = TlsServer(identity, AGENT_PORT) { commands.handle(it) }
            val sessionServer = TlsServer(identity, SESSION_PORT) { thumbnails.handle(it) }
            tlsServers = listOf(agentServer, sessionServer)
            agentServer.start()
            sessionServer.start()

            // Many phones drop broadcast packets to save battery unless
            // an app asks for them.
            multicastLock = (applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager)
                .createMulticastLock("Midview discovery").apply {
                    setReferenceCounted(false)
                    acquire()
                }
            val socket = DatagramSocket(null).apply {
                reuseAddress = true
                broadcast = true
                bind(InetSocketAddress(Discovery.PORT))
            }
            discoverySocket = socket
            Thread({ responder.run(socket) }, "Midview discovery").apply { isDaemon = true }.start()
        }
        Log.i(TAG, "Agent listening as $name (${identity.thumbprint}).")
    }

    // True only when the code is really on the screen: the notification
    // can show (the app's notifications and the pairing channel are on), or
    // else the code screen came up.
    private fun showCode(code: String, teacher: String): Boolean {
        PairingCodeActivity.show(this, code, teacher)
        val manager = notifications()
        val notified = manager.areNotificationsEnabled() &&
            manager.getNotificationChannel(PAIRING_CHANNEL)?.importance != NotificationManager.IMPORTANCE_NONE
        if (notified) {
            val open = PendingIntent.getActivity(
                this, 0, PairingCodeActivity.intent(this), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
            val (title, detail) = PairingCodeActivity.texts(code, teacher)
            val notification = Notification.Builder(this, PAIRING_CHANNEL)
                .setSmallIcon(android.R.drawable.ic_lock_idle_lock)
                .setContentTitle("$title: $code")
                .setContentText(detail)
                .setCategory(Notification.CATEGORY_CALL)
                .setOngoing(true)
                .setContentIntent(open)
                .setFullScreenIntent(open, true)
                .build()
            manager.notify(PAIRING_NOTIFICATION, notification)
            return true
        }
        return PairingCodeActivity.awaitShown(SHOW_TIMEOUT_MILLIS)
    }

    private fun hideCode() {
        PairingCodeActivity.hide()
        notifications().cancel(PAIRING_NOTIFICATION)
    }

    private fun goForeground(text: String) {
        val notification = Notification.Builder(this, STATUS_CHANNEL)
            .setSmallIcon(android.R.drawable.ic_lock_idle_lock)
            .setContentTitle(text)
            .setStyle(Notification.BigTextStyle().bigText(text))
            .setOngoing(true)
            .build()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            startForeground(STATUS_NOTIFICATION, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE)
        } else {
            startForeground(STATUS_NOTIFICATION, notification)
        }
    }

    private fun createChannels() {
        notifications().createNotificationChannel(
            NotificationChannel(STATUS_CHANNEL, "Midview", NotificationManager.IMPORTANCE_LOW),
        )
        notifications().createNotificationChannel(
            NotificationChannel(PAIRING_CHANNEL, "Pareamento com o professor", NotificationManager.IMPORTANCE_HIGH),
        )
    }

    private fun notifications() = getSystemService(NotificationManager::class.java)

    // Starts the agent after the phone restarts (and after the app is updated).
    class StartAtBoot : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            if (intent.action == Intent.ACTION_BOOT_COMPLETED || intent.action == Intent.ACTION_MY_PACKAGE_REPLACED) {
                start(context)
            }
        }
    }

    companion object {
        // ConnectionClient.AgentPort and ConnectionClient.SessionPort.
        const val AGENT_PORT = 28900
        const val SESSION_PORT = 5901

        const val STATUS_TEXT = "Celular da escola, gerenciado pelo Midview"
        const val REPLACED_TEXT =
            "Celular da escola, gerenciado pelo Midview. A identificação deste celular foi refeita: os professores precisam parear de novo."

        private const val TAG = "Midview"
        private const val RETRY_MILLIS = 30_000L
        private const val LOCK_AGAIN_MILLIS = 3_000L

        // How long the code screen has to come up when it is the only way
        // to show the code.
        private const val SHOW_TIMEOUT_MILLIS = 3_000L
        private const val TRUSTED_MASTERS_FILE = "trusted-masters.txt"
        private const val STATUS_CHANNEL = "status"
        private const val PAIRING_CHANNEL = "pairing"
        private const val STATUS_NOTIFICATION = 1
        private const val PAIRING_NOTIFICATION = 2

        // WifiManager.WIFI_MODE_FULL_HIGH_PERF, deprecated from Android 14 in
        // favor of WIFI_MODE_FULL_LOW_LATENCY.
        @Suppress("DEPRECATION")
        fun wifiLockMode(sdkInt: Int): Int =
            if (sdkInt >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
                WifiManager.WIFI_MODE_FULL_LOW_LATENCY
            } else {
                WifiManager.WIFI_MODE_FULL_HIGH_PERF
            }

        fun start(context: Context) {
            context.startForegroundService(Intent(context, AgentService::class.java))
        }
    }
}
