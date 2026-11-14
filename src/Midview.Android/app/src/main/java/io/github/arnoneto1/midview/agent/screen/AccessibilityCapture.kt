package io.github.arnoneto1.midview.agent.screen

import android.accessibilityservice.AccessibilityService
import android.annotation.TargetApi
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.os.Build
import android.provider.Settings
import android.util.Log
import android.view.Display
import android.view.KeyEvent
import android.view.accessibility.AccessibilityEvent
import io.github.arnoneto1.midview.agent.control.StudentTouchLock
import java.util.concurrent.CountDownLatch
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

// The teacher's view of a school phone's screen without asking the student:
// an accessibility service, turned on once by cable while the phone is
// prepared (README, "Celulares"), that takes a picture of the screen
// (takeScreenshot, Android 11 and newer) only when a thumbnail or a big
// window asks for one, at most every MIN_SHOT_INTERVAL_MILLIS, into the
// same LatestFrame as the "Iniciar agora" capture. Only while the teacher
// controls the phone ("Controlar", control/AndroidControlTarget) does it do
// more: the teacher's touches as gestures, and the teacher's typing into the
// focused text field, read on demand, and, with "Travar o toque do aluno",
// the student's touches and keys taken (StudentTouchLock); no events
// (res/xml/accessibility_capture.xml). What it puts on the screen are the
// teacher's warning before "Fechar programas" (AndroidCloseNotice) and the
// "being controlled" strip (ControlNotice), both letting every touch
// through, and the student's touch lock. Android binds it
// in the app's own process; the agent is started there as for any start of
// the process (MidviewApplication).
class AccessibilityCapture : AccessibilityService() {
    private var shots: ExecutorService? = null

    // A screenshot failed and none worked since: the next failures go unlogged.
    @Volatile private var failing = false

    override fun onServiceConnected() {
        super.onServiceConnected()
        instance = this
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R) {
            Log.i(TAG, "Accessibility on, but this Android takes no screenshots: the student is asked instead.")
            return
        }
        val executor = Executors.newSingleThreadExecutor { Thread(it, "Midview screenshot") }
        shots = executor
        connected = true
        ScreenCaptureService.frames.attachShots(MIN_SHOT_INTERVAL_MILLIS) { shoot(executor) }
        ScreenCaptureService.consent.sourceChanged()
        // Seen without asking now: an "Iniciar agora" capture is not
        // needed, nor a request still on the screen.
        ScreenCaptureService.stop(this)
        ConsentActivity.closeIfOpen(this)
        Log.i(TAG, "Screen seen through accessibility.")
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) = Unit

    // Only while the teacher locks the student's touch (StudentTouchLock):
    // the student's Back, Home, Recents and volume keys are taken; any other
    // time every key goes on as it came.
    override fun onKeyEvent(event: KeyEvent): Boolean = StudentTouchLock.takesKey(event.keyCode)

    override fun onInterrupt() = Unit

    // Turned off in the settings (or the app is being replaced).
    override fun onUnbind(intent: Intent?): Boolean {
        gone()
        return super.onUnbind(intent)
    }

    override fun onDestroy() {
        gone()
        super.onDestroy()
    }

    private fun gone() {
        if (instance === this) instance = null
        val executor = shots ?: return
        shots = null
        connected = false
        ScreenCaptureService.frames.detachShots()
        ScreenCaptureService.consent.sourceChanged()
        executor.shutdown()
        Log.i(TAG, "Screen no longer seen through accessibility.")
    }

    // A picture of the screen now, at most MAX_CAPTURE_SIDE on the long
    // side; null if Android gave none (too soon, the screen off, a secure
    // window). Waits for Android's answer, a moment.
    @TargetApi(Build.VERSION_CODES.R)
    private fun shoot(executor: ExecutorService): Picture? {
        val done = CountDownLatch(1)
        val picture = AtomicReference<Picture?>()
        try {
            takeScreenshot(
                Display.DEFAULT_DISPLAY, executor,
                object : TakeScreenshotCallback {
                    override fun onSuccess(screenshot: ScreenshotResult) {
                        try {
                            picture.set(toPicture(screenshot))
                            failing = false
                        } catch (e: Throwable) {
                            Log.w(TAG, "A screenshot could not be read", e)
                        } finally {
                            done.countDown()
                        }
                    }

                    override fun onFailure(errorCode: Int) {
                        // Logged once until a shot works again (the screen
                        // off fails every time).
                        if (errorCode != ERROR_TAKE_SCREENSHOT_INTERVAL_TIME_SHORT && !failing) {
                            failing = true
                            Log.w(TAG, "No screenshot (error $errorCode)")
                        }
                        done.countDown()
                    }
                },
            )
        } catch (e: RuntimeException) {
            // The service is going away, or the executor was shut down.
            Log.w(TAG, "No screenshot", e)
            return null
        }
        if (!done.await(SHOT_TIMEOUT_MILLIS, TimeUnit.MILLISECONDS)) return null
        return picture.get()
    }

    // Android's picture (in the graphics memory) copied once into pixels.
    @TargetApi(Build.VERSION_CODES.R)
    private fun toPicture(screenshot: ScreenshotResult): Picture? {
        val buffer = screenshot.hardwareBuffer
        var hardware: Bitmap? = null
        var full: Bitmap? = null
        var sized: Bitmap? = null
        try {
            hardware = Bitmap.wrapHardwareBuffer(buffer, screenshot.colorSpace) ?: return null
            full = hardware.copy(Bitmap.Config.ARGB_8888, false) ?: return null
            hardware.recycle()
            hardware = null
            val (width, height) = FrameScale.capture(full.width, full.height)
            sized = if (width == full.width && height == full.height) full else Bitmap.createScaledBitmap(full, width, height, true)
            val pixels = IntArray(width * height)
            sized.getPixels(pixels, 0, width, 0, 0, width, height)
            for (i in pixels.indices) pixels[i] = pixels[i] or OPAQUE
            return Picture(pixels, width, height)
        } finally {
            sized?.recycle()
            full?.recycle()
            hardware?.recycle()
            buffer.close()
        }
    }

    companion object {
        private const val TAG = "Midview"

        // Android refuses a screenshot asked for less than a third of a
        // second after the one before (ERROR_TAKE_SCREENSHOT_INTERVAL_TIME_SHORT):
        // under 3 pictures a second.
        const val MIN_SHOT_INTERVAL_MILLIS = 350L
        private const val SHOT_TIMEOUT_MILLIS = 2_000L
        private const val OPAQUE = 0xFF000000.toInt()

        // The service is connected and taking the pictures.
        @Volatile var connected = false
            private set

        // The service while Android has it connected (any Android version):
        // what draws the warning strips and carries out the teacher's control.
        @Volatile var instance: AccessibilityCapture? = null
            private set

        // Turned on in the settings (by cable), connected or not yet.
        fun enabledInSettings(context: Context): Boolean = try {
            val resolver = context.contentResolver
            CaptureSource.enabledInSettings(
                Settings.Secure.getString(resolver, Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES),
                Settings.Secure.getInt(resolver, Settings.Secure.ACCESSIBILITY_ENABLED, 0) == 1,
            )
        } catch (e: RuntimeException) {
            false
        }
    }
}
