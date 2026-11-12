package io.github.arnoneto1.midview.agent.agent

import android.app.ActivityManager
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
import android.os.Environment
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.StatFs
import android.util.Log
import io.github.arnoneto1.midview.agent.apps.AppBlockStore
import io.github.arnoneto1.midview.agent.apps.AndroidCloseNotice
import io.github.arnoneto1.midview.agent.apps.AppCloser
import io.github.arnoneto1.midview.agent.apps.CloseCountdown
import io.github.arnoneto1.midview.agent.apps.AppSuspender
import io.github.arnoneto1.midview.agent.apps.InstalledApps
import io.github.arnoneto1.midview.agent.apps.PackageWatcher
import io.github.arnoneto1.midview.agent.classfolder.ClassFileTimes
import io.github.arnoneto1.midview.agent.classfolder.ClassFolderFollow
import io.github.arnoneto1.midview.agent.classfolder.ClassLessonNotice
import io.github.arnoneto1.midview.agent.classfolder.ClassLessonStore
import io.github.arnoneto1.midview.agent.classfolder.PhoneClassFolder
import io.github.arnoneto1.midview.agent.control.AndroidControlTarget
import io.github.arnoneto1.midview.agent.files.DownloadFolder
import io.github.arnoneto1.midview.agent.files.TeacherFiles
import io.github.arnoneto1.midview.agent.identity.AgentIdentity
import io.github.arnoneto1.midview.agent.identity.TRUSTED_MASTERS_FILE
import io.github.arnoneto1.midview.agent.identity.TrustedMasters
import io.github.arnoneto1.midview.agent.lab.LabConfigStore
import io.github.arnoneto1.midview.agent.lock.LockActivity
import io.github.arnoneto1.midview.agent.lock.ScreenLock
import io.github.arnoneto1.midview.agent.net.ChatHandler
import io.github.arnoneto1.midview.agent.net.ClassFolderHandler
import io.github.arnoneto1.midview.agent.net.CommandHandler
import io.github.arnoneto1.midview.agent.net.DiscoveryResponder
import io.github.arnoneto1.midview.agent.net.LockScreenHandler
import io.github.arnoneto1.midview.agent.net.ScreenStreamHandler
import io.github.arnoneto1.midview.agent.net.ThumbnailHandler
import io.github.arnoneto1.midview.agent.net.TlsServer
import io.github.arnoneto1.midview.agent.optimize.AdblockDns
import io.github.arnoneto1.midview.agent.optimize.AndroidDnsNetwork
import io.github.arnoneto1.midview.agent.optimize.DnsWatcher
import io.github.arnoneto1.midview.agent.optimize.OptimizeStore
import io.github.arnoneto1.midview.agent.optimize.Optimizer
import io.github.arnoneto1.midview.agent.pairing.PairingFlow
import io.github.arnoneto1.midview.agent.policy.AndroidDevicePolicyGateway
import io.github.arnoneto1.midview.agent.policy.LockScreenKeeper
import io.github.arnoneto1.midview.agent.power.AndroidPhonePower
import io.github.arnoneto1.midview.agent.power.PowerCountdown
import io.github.arnoneto1.midview.agent.programs.TeacherPrograms
import io.github.arnoneto1.midview.agent.protocol.CommandResult
import io.github.arnoneto1.midview.agent.protocol.Discovery
import io.github.arnoneto1.midview.agent.screen.BitmapJpeg
import io.github.arnoneto1.midview.agent.screen.CaptureCadence
import io.github.arnoneto1.midview.agent.screen.FrameCredit
import io.github.arnoneto1.midview.agent.screen.ScreenCaptureService
import io.github.arnoneto1.midview.agent.screen.ThumbnailPicture
import io.github.arnoneto1.midview.agent.sites.BlockVpnService
import io.github.arnoneto1.midview.agent.sites.ChromeConfig
import io.github.arnoneto1.midview.agent.sites.ChromePolicy
import io.github.arnoneto1.midview.agent.sites.InternetBlock
import io.github.arnoneto1.midview.agent.sites.OtherBrowsers
import io.github.arnoneto1.midview.agent.sites.SiteBlockStore
import io.github.arnoneto1.midview.agent.sites.SitePolicy
import io.github.arnoneto1.midview.agent.status.StatusImage
import io.github.arnoneto1.midview.agent.talk.TeacherTalk
import io.github.arnoneto1.midview.agent.ui.PairingCodeActivity
import io.github.arnoneto1.midview.agent.update.ApkFingerprint
import io.github.arnoneto1.midview.agent.update.ApkUpdateStore
import io.github.arnoneto1.midview.agent.update.ApkUpdater
import io.github.arnoneto1.midview.agent.update.ApkUpload
import io.github.arnoneto1.midview.agent.update.PackageApkReader
import io.github.arnoneto1.midview.agent.update.SelfInstaller
import java.io.File
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.RejectedExecutionException
import java.util.concurrent.ScheduledExecutorService
import java.util.concurrent.TimeUnit

// The Midview agent on the phone: always running (a foreground service with
// its fixed notification, started again after every restart), it answers
// the teacher's search (UDP 28901), takes pairing and commands (TLS 28900)
// and serves the screen (TLS 5901: the grid's thumbnail, the big window and
// "Bloquear telas"), the same ports as on a PC.
class AgentService : Service() {
    private val lock = Any()
    private var tlsServers = emptyList<TlsServer>()
    private var discoverySocket: DatagramSocket? = null
    private var multicastLock: WifiManager.MulticastLock? = null
    private var packageWatcher: PackageWatcher? = null
    private var dnsWatcher: DnsWatcher? = null
    private var wifiLock: WifiManager.WifiLock? = null
    private var classFolderTimer: ScheduledExecutorService? = null
    private val main = Handler(Looper.getMainLooper())
    private var stopped = false
    private val stopping = CountDownLatch(1)

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        createChannels()
        ScreenCaptureService.init(this)
        goForeground(STATUS_TEXT)
        keeper(this).created()
        AgentWatchdog.arm(this)
        holdWifi()
        // Making the identity the first time takes a moment (an RSA key).
        Thread({ startAgentUntilItRuns() }, "Midview agent start").start()
    }

    // The school phone's locks, again at every start (nothing on a phone
    // that is not the school's Device Owner). Made Device Owner by
    // `dpm set-device-owner`, the app is told (and starts the agent) before
    // it really is one: the locks are tried once more a little later.
    // Applying them twice changes nothing.
    //
    // No intent: Android started the service again after killing the app
    // (START_STICKY). onCreate already put it back in the foreground.
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent == null) Log.i(TAG, "Agent started again by Android.")
        AgentWatchdog.arm(this)
        AndroidDevicePolicyGateway.lockDown(this)
        // The lock screen's reset token and the teacher's PIN.
        LockScreenKeeper.check(this, starting = true)
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
        keeper(this).destroyed()
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
        classFolderTimer?.shutdownNow()
        classFolderTimer = null
        packageWatcher?.stop()
        packageWatcher = null
        dnsWatcher?.stop()
        dnsWatcher = null
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
        // The app block: kept in the app's storage, applied again now (it
        // holds after a restart, or MIUI ending the app) and whenever an app
        // is installed, updated or removed.
        val installedApps = InstalledApps(this)
        val log: (String, Throwable?) -> Unit = { text, e -> if (e == null) Log.i(TAG, text) else Log.w(TAG, text, e) }
        val suspender = AppSuspender(installedApps, AndroidDevicePolicyGateway(this), log)
        val appBlock = AppBlockStore(File(filesDir, APP_BLOCK_FILE), apply = suspender::apply, log = log)
        // "Otimizar": the ad apps hidden and the ad-free DNS, run by itself at
        // the first command of a trusted teacher and repeated by "Otimizar";
        // the DNS is tried again when the internet block is freed, and goes
        // back to automatic if the network stops validating it (DnsWatcher).
        val policyGateway = AndroidDevicePolicyGateway(this)
        lateinit var internet: InternetBlock
        val internetBlocked = { internet.isBlocked }
        // The lab panel ("Travar downloads", "Bloqueador de anúncios"): the
        // download block goes in Chrome's configuration with the site block
        // (ChromeConfig), the ad blocker is whether the optimizer turns the
        // ad-free DNS on.
        lateinit var chromeConfig: ChromeConfig
        lateinit var optimizer: Optimizer
        val lab = LabConfigStore(
            File(filesDir, LAB_CONFIG_FILE), policyGateway::isDeviceOwner,
            downloads = { chromeConfig.setDownloadsBlocked(it) },
            adblockChanged = { optimizer.adblockChanged() },
            chromeVersion = ::chromeMajorVersion,
            log = log,
        )
        chromeConfig = ChromeConfig(policyGateway, lab.current.blockDownloads, log)
        optimizer = Optimizer(
            installedApps, policyGateway,
            AdblockDns(policyGateway, AndroidDnsNetwork(this), internetBlocked, log),
            OptimizeStore(File(filesDir, OPTIMIZE_FILE), log),
            internetBlocked, adblockWanted = { lab.adblock }, log = log,
        )
        // The site block: Chrome's policy and the internet block, kept the
        // same way and applied again now. Other browsers are hidden, checked
        // again with every app installed (the package watcher).
        internet = InternetBlock(
            policyGateway, File(filesDir, INTERNET_BLOCK_FILE),
            startVpn = { BlockVpnService.start(this) },
            stopVpn = { BlockVpnService.stop(this) },
            log = log,
            freed = optimizer::internetFreed,
        )
        val sites = SiteBlockStore(File(filesDir, SITE_BLOCK_FILE), apply = SitePolicy(chromeConfig, internet, log)::apply, log = log)
        // Chrome's configuration, with the site block and the download block.
        sites.reapply()
        val otherBrowsers = OtherBrowsers(installedApps, policyGateway, File(filesDir, HIDDEN_BROWSERS_FILE), log)
        // "Atualizar celulares": the agent's next version comes over the
        // network and installs itself. The attempt a restart cut short, or
        // the one that installed this version, is closed here; the APK's
        // fingerprint is worked out in the background.
        val updater = ApkUpdater(
            ApkUpdateStore(File(filesDir, ApkUpdater.ATTEMPT_FILE), log),
            ApkUpload(File(filesDir, ApkUpdater.UPLOAD_FOLDER)),
            ApkFingerprint { ApkFingerprint.of(File(applicationInfo.sourceDir)) },
            PackageApkReader(this),
            SelfInstaller(this),
            isDeviceOwner = policyGateway::isDeviceOwner,
            log = log,
        )
        val lockScreen = LockScreenKeeper.get(this)
        lateinit var closer: AppCloser
        lateinit var programs: TeacherPrograms
        // A closing asked for under the lock is done when it goes, and the
        // teacher's messages and conversations, sites and apps come up then.
        val screenLock = ScreenLock(
            show = { LockActivity.show(this) },
            hide = {
                LockActivity.hide()
                closer.unlocked()
                TeacherTalk.unlocked()
                programs.launch.unlocked()
            },
        )
        // "Abrir site", "Abrir programa", "Instalar programa", "Desinstalar programa".
        programs = TeacherPrograms(
            this, { screenLock.locked }, internetBlocked, { sites.current.domains }, installedApps::protected,
            appBlock::reapply, policyGateway::isDeviceOwner, log,
        )
        // "Mensagem" and "Bate-papo": never over the teacher's lock screen.
        TeacherTalk.start(this, isLocked = { screenLock.locked })
        // "Fechar programas": the home screen in front, the student's apps
        // closed by suspending them for a moment, and the app block applied
        // again; under the teacher's lock screen, once it goes.
        closer = AppCloser(
            suspender, policyGateway, packageName,
            reapplyBlock = appBlock::reapply,
            isScreenLocked = { screenLock.locked },
            goHome = {
                // As on a PC, the teacher's messages close with the apps.
                TeacherTalk.closeMessages()
                startActivity(Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
            },
            killBackground = { getSystemService(ActivityManager::class.java).killBackgroundProcesses(it) },
            log = log,
        )
        // The student's warning and the wait, then the closing, on one thread of its own.
        val closing = Executors.newSingleThreadScheduledExecutor { Thread(it, "Midview close apps").apply { isDaemon = true } }
        val countdown = CloseCountdown(
            closer, AndroidCloseNotice(this),
            schedule = { delay, task ->
                closing.schedule(
                    Runnable {
                        try {
                            task()
                        } catch (e: RuntimeException) {
                            log("The warning before closing the apps failed", e)
                        }
                    },
                    delay, TimeUnit.MILLISECONDS,
                )
            },
            log = log,
        )
        // "Desligar" and "Reiniciar": the student's warning and the wait,
        // then the screen off or the restart, on a thread of their own.
        val powering = Executors.newSingleThreadScheduledExecutor { Thread(it, "Midview power").apply { isDaemon = true } }
        val power = PowerCountdown(
            AndroidPhonePower(this), AndroidCloseNotice(this, AndroidCloseNotice.POWER_NOTIFICATION),
            schedule = { delay, task ->
                powering.schedule(
                    Runnable {
                        try {
                            task()
                        } catch (e: RuntimeException) {
                            log("The warning before the restart or the screen off failed", e)
                        }
                    },
                    delay, TimeUnit.MILLISECONDS,
                )
            },
            log = log,
        )
        // "Pasta da turma": the lesson and the class folders' home (a PC
        // Agent's part, on the commands port) and the lesson's folder in
        // Download/Midview (a PC Session's part, on the screen port),
        // followed every few seconds and at once when the lesson changes.
        val downloads = DownloadFolder(this)
        lateinit var classLessons: ClassLessonStore
        val classFolder = PhoneClassFolder(
            downloads.directory, { classLessons.lessonOn }, ClassFileTimes(File(filesDir, CLASS_TIMES_FILE), log),
            canSee = { downloads.seesEverything },
            freeBytes = { StatFs(Environment.getExternalStorageDirectory().path).availableBytes },
        )
        val lessonNotice = ClassLessonNotice(this)
        val follow = ClassFolderFollow(
            classFolder, { classLessons.current }, File(filesDir, CLASS_FOLLOW_FILE), canSee = { downloads.seesEverything },
            notice = { if (it == null) lessonNotice.hide() else lessonNotice.show(it) },
            remind = lessonNotice::showIfMissing,
            log = log,
        )
        val classTimer = Executors.newSingleThreadScheduledExecutor { Thread(it, "Midview class folder").apply { isDaemon = true } }
        classLessons = ClassLessonStore(
            File(filesDir, CLASS_LESSON_FILE),
            prepare = follow::prepare,
            changed = {
                try {
                    classTimer.execute { follow.follow() }
                } catch (e: RejectedExecutionException) {
                    // The agent is stopping.
                }
            },
            log = log,
        )
        val commands = CommandHandler(
            trusted, pairing, consent, appBlock, sites, optimizer, updater, policyGateway,
            log = log, screenPin = lockScreen::setTeacherPin, closeApps = countdown::request, power = power::request,
            showMessage = { TeacherTalk.messages?.receive(it) ?: (false to CommandResult.NOT_ON_PHONE) },
            chat = { channel -> TeacherTalk.chats?.let { ChatHandler(channel, it).run() } },
            // "Enviar arquivo" and "Recolher arquivos": Download/Midview.
            files = TeacherFiles.handler(this, isLocked = { screenLock.locked }),
            openWebsite = programs.launch::openWebsite, openApp = programs.launch::openApp, installs = programs.installs,
            lab = lab,
            classLesson = classLessons,
            listApps = installedApps::list,
        )
        val frames = ScreenCaptureService.frames
        val picture = ThumbnailPicture(
            consent, frames,
            status = { text, width, height -> StatusImage.render(width, height, DeviceName.read(this), text) },
            jpeg = BitmapJpeg::thumbnail,
        )
        val policy = AndroidDevicePolicyGateway(this)
        val thumbnails = ThumbnailHandler(
            trusted,
            render = picture::render,
            teacherConnected = consent::teacherConnected,
            stream = {
                // "Controlar" through the accessibility service; under the teacher's lock, never out of it.
                ScreenStreamHandler(
                    it, frames, FrameCredit(), CaptureCadence(), BitmapJpeg,
                    control = AndroidControlTarget(isLocked = { screenLock.locked }),
                ).run()
            },
            lockScreen = { LockScreenHandler(it, screenLock, isDeviceOwner = policy::canLockScreen).run() },
            classFolder = ClassFolderHandler(classFolder)::serve,
        )
        val responder = DiscoveryResponder(pairing, identity.thumbprint) { DeviceName.read(this) }

        synchronized(lock) {
            if (stopped) {
                classTimer.shutdownNow()
                return
            }
            val agentServer = TlsServer(identity, AGENT_PORT) { commands.handle(it) }
            val sessionServer = TlsServer(identity, SESSION_PORT) { thumbnails.handle(it) }
            tlsServers = listOf(agentServer, sessionServer)
            packageWatcher = PackageWatcher(this) {
                appBlock.reapply()
                otherBrowsers.apply()
            }.also { it.start() }
            dnsWatcher = DnsWatcher(this, optimizer, log).also { it.start() }
            classFolderTimer = classTimer
            classTimer.scheduleWithFixedDelay(
                {
                    try {
                        classLessons.endIfFromAnEarlierDay()
                        follow.follow()
                    } catch (e: RuntimeException) {
                        log("The class folder's look failed", e)
                    }
                },
                0, CLASS_FOLDER_LOOK_SECONDS, TimeUnit.SECONDS,
            )
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

    // Chrome's major version ("126.0.6478.71" -> 126); null without Chrome.
    private fun chromeMajorVersion(): Int? =
        try {
            packageManager.getPackageInfo(ChromePolicy.PACKAGE, 0).versionName?.substringBefore('.')?.toIntOrNull()
        } catch (e: Exception) {
            null
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

    // Starts the agent after the phone restarts (after the app is updated:
    // PackageReplacedReceiver).
    class StartAtBoot : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            if (intent.action == Intent.ACTION_BOOT_COMPLETED) {
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
        private const val APP_BLOCK_FILE = "app-block.bin"
        private const val SITE_BLOCK_FILE = "site-block.txt"
        private const val INTERNET_BLOCK_FILE = "internet-block.txt"
        private const val HIDDEN_BROWSERS_FILE = "hidden-browsers.txt"
        private const val OPTIMIZE_FILE = "optimize.txt"
        private const val LAB_CONFIG_FILE = "lab-config.bin"
        private const val CLASS_LESSON_FILE = "class-lesson.bin"
        private const val CLASS_FOLLOW_FILE = "class-folder-follow.txt"
        private const val CLASS_TIMES_FILE = "class-folder-times.txt"

        // As often as a PC's Session follows the lesson.
        private const val CLASS_FOLDER_LOOK_SECONDS = 5L
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

        @Volatile private var keeper: AgentKeeper? = null

        private fun keeper(context: Context): AgentKeeper =
            keeper ?: synchronized(this) {
                keeper ?: AgentKeeper { start(context.applicationContext) }.also { keeper = it }
            }

        // Starts the agent unless it already runs in this process (the
        // watchdog and every start of the process). Never fails its caller:
        // a start Android refuses is logged and tried at the next check.
        fun ensureRunning(context: Context, reason: String) {
            try {
                if (keeper(context).ensureRunning()) Log.i(TAG, "Agent started ($reason).")
            } catch (e: RuntimeException) {
                Log.e(TAG, "Agent could not be started ($reason)", e)
            }
        }
    }
}
