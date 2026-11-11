package io.github.arnoneto1.midview.agent.screen

import android.annotation.SuppressLint
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.graphics.PixelFormat
import android.hardware.display.DisplayManager
import android.hardware.display.VirtualDisplay
import android.media.ImageReader
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Handler
import android.os.HandlerThread
import android.os.IBinder
import android.os.SystemClock
import android.util.DisplayMetrics
import android.util.Log
import android.view.Display

// The capture of the screen, once the student tapped "Iniciar agora"
// (ConsentActivity): a foreground service of type mediaProjection, as
// Android 10 and newer require, mirroring the screen into an ImageReader
// whose pictures go to frames, at most MAX_CAPTURE_SIDE on the long side.
// When the phone turns, the capture takes the new shape. When the capture
// stops (the student or the system ends it), consent hears of it. All the
// capture's work runs on one thread of its own.
class ScreenCaptureService : Service() {
    private lateinit var thread: HandlerThread
    private lateinit var handler: Handler

    // Only touched on handler's thread.
    private var projection: MediaProjection? = null
    private var display: VirtualDisplay? = null
    private var reader: ImageReader? = null
    private var size = 0 to 0
    private var converting = false
    private var lastConvertedMillis = 0L

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        thread = HandlerThread("Midview capture").apply { start() }
        handler = Handler(thread.looper)
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        // First, as Android wants of a service started in the foreground;
        // the consent is already given, as Android 14 wants of this type.
        goForeground()
        val resultCode = intent?.getIntExtra(EXTRA_RESULT_CODE, 0) ?: 0
        val data = intent?.let { consentData(it) }
        if (data == null) {
            consent.stopped()
            stopSelf()
            return START_NOT_STICKY
        }
        handler.post {
            try {
                begin(resultCode, data)
            } catch (e: Exception) {
                Log.e(TAG, "The screen capture could not start", e)
                end()
            }
        }
        return START_NOT_STICKY
    }

    override fun onDestroy() {
        handler.post {
            if (projection != null) {
                release()
                frames.clear()
                consent.stopped()
            }
        }
        thread.quitSafely()
        displays().unregisterDisplayListener(displayListener)
        super.onDestroy()
    }

    private fun begin(resultCode: Int, data: Intent) {
        // A second answer while capturing: the new capture replaces the old.
        release()
        val manager = getSystemService(MediaProjectionManager::class.java)
        val started = manager.getMediaProjection(resultCode, data)
            ?: throw IllegalStateException("No projection for this answer")
        projection = started
        // Before the virtual display, as Android 14 requires.
        started.registerCallback(stopCallback, handler)

        val (width, height, dpi) = screen()
        size = width to height
        val images = newReader(width, height)
        reader = images
        display = started.createVirtualDisplay(
            "Midview", width, height, dpi, DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR, images.surface, null, handler,
        )
        displays().registerDisplayListener(displayListener, handler)
        consent.granted()
        Log.i(TAG, "Screen capture started at ${width}x$height.")
    }

    // The projection ended by the student or the system: no picture any more.
    private fun end() {
        release()
        frames.clear()
        consent.stopped()
        stopSelf()
    }

    private fun release() {
        displays().unregisterDisplayListener(displayListener)
        projection?.unregisterCallback(stopCallback)
        display?.release()
        display = null
        reader?.setOnImageAvailableListener(null, null)
        reader?.close()
        reader = null
        projection?.stop()
        projection = null
        handler.removeCallbacks(convert)
        converting = false
    }

    private val stopCallback = object : MediaProjection.Callback() {
        override fun onStop() {
            Log.i(TAG, "Screen capture stopped.")
            end()
        }
    }

    // The phone turned (or the screen changed size): the capture takes the
    // new shape on the same projection, which Android 14 allows only once.
    private val displayListener = object : DisplayManager.DisplayListener {
        override fun onDisplayChanged(displayId: Int) {
            if (displayId != Display.DEFAULT_DISPLAY) return
            val virtual = display ?: return
            val (width, height, dpi) = screen()
            if (size == width to height) return
            size = width to height
            val old = reader
            old?.setOnImageAvailableListener(null, null)
            val images = newReader(width, height)
            reader = images
            converting = false
            virtual.resize(width, height, dpi)
            virtual.surface = images.surface
            old?.close()
            Log.i(TAG, "Screen capture now ${width}x$height.")
        }

        override fun onDisplayAdded(displayId: Int) = Unit

        override fun onDisplayRemoved(displayId: Int) = Unit
    }

    private fun newReader(width: Int, height: Int): ImageReader =
        ImageReader.newInstance(width, height, PixelFormat.RGBA_8888, MAX_IMAGES).apply {
            setOnImageAvailableListener({ convertSoon() }, handler)
        }

    // Pictures come as often as the screen changes; one is turned into
    // pixels at most every ACTIVE_INTERVAL_MS, always the newest, so the last
    // change before the screen goes still is never lost.
    private fun convertSoon() {
        if (converting) return
        converting = true
        val wait = CaptureCadence.ACTIVE_INTERVAL_MS - (SystemClock.uptimeMillis() - lastConvertedMillis)
        handler.postDelayed(convert, maxOf(0L, wait))
    }

    private val convert = Runnable {
        converting = false
        val image = try {
            reader?.acquireLatestImage()
        } catch (e: IllegalStateException) {
            null
        } ?: return@Runnable
        try {
            val plane = image.planes[0]
            val pixels = RgbaPixels.toArgb(plane.buffer, image.width, image.height, plane.pixelStride, plane.rowStride)
            frames.publish(pixels, image.width, image.height)
        } finally {
            image.close()
            lastConvertedMillis = SystemClock.uptimeMillis()
        }
    }

    private data class Screen(val width: Int, val height: Int, val dpi: Int)

    // The phone's whole screen as it is held now, at the capture's size.
    @Suppress("DEPRECATION")
    private fun screen(): Screen {
        val metrics = DisplayMetrics()
        displays().getDisplay(Display.DEFAULT_DISPLAY).getRealMetrics(metrics)
        val (width, height) = FrameScale.capture(metrics.widthPixels, metrics.heightPixels)
        return Screen(width, height, metrics.densityDpi)
    }

    private fun displays() = getSystemService(DisplayManager::class.java)

    private fun goForeground() {
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(NotificationChannel(CHANNEL, "Tela compartilhada", NotificationManager.IMPORTANCE_LOW))
        val notification = Notification.Builder(this, CHANNEL)
            .setSmallIcon(android.R.drawable.ic_menu_view)
            .setContentTitle(NOTIFICATION_TEXT)
            .setOngoing(true)
            .build()
        startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION)
    }

    companion object {
        const val NOTIFICATION_TEXT = "O professor pode ver a tela deste celular"

        private const val TAG = "Midview"
        private const val CHANNEL = "screen"
        private const val NOTIFICATION_ID = 3
        private const val MAX_IMAGES = 2
        private const val EXTRA_RESULT_CODE = "resultCode"
        private const val EXTRA_DATA = "data"

        // The newest picture of the screen, for the thumbnails and the big window.
        val frames = LatestFrame()

        // The application's context, which lives as long as the app.
        @SuppressLint("StaticFieldLeak")
        @Volatile private var context: Context? = null

        // Asked once per run of the app: after a restart the student is asked again.
        val consent = ScreenConsent(
            clock = { SystemClock.elapsedRealtime() },
            askAgainWhenStopped = Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE,
        ) {
            context?.let { ConsentActivity.show(it) }
        }

        // Called by the agent as it starts: consent needs a context to show its request.
        fun init(context: Context) {
            this.context = context.applicationContext
        }

        // The student's answer was yes: start capturing with it.
        fun start(context: Context, resultCode: Int, data: Intent) {
            context.startForegroundService(
                Intent(context, ScreenCaptureService::class.java)
                    .putExtra(EXTRA_RESULT_CODE, resultCode)
                    .putExtra(EXTRA_DATA, data),
            )
        }

        private fun consentData(intent: Intent): Intent? =
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                intent.getParcelableExtra(EXTRA_DATA, Intent::class.java)
            } else {
                @Suppress("DEPRECATION")
                intent.getParcelableExtra(EXTRA_DATA)
            }
    }
}
