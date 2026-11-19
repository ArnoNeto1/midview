package io.github.arnoneto1.midview.agent.home

import android.accessibilityservice.AccessibilityService
import android.accessibilityservice.AccessibilityServiceInfo
import android.accessibilityservice.GestureDescription
import android.app.KeyguardManager
import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Path
import android.graphics.Rect
import android.os.Handler
import android.os.HandlerThread
import android.os.PowerManager
import android.os.SystemClock
import android.util.Log
import android.view.accessibility.AccessibilityNodeInfo
import io.github.arnoneto1.midview.agent.optimize.HomeArranger
import io.github.arnoneto1.midview.agent.screen.AccessibilityCapture
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

// The home screen arranged at the end of the teacher's "Otimizar"
// (Optimizer, HomeStep) on this device; teacherLocked: the teacher's lock
// screen is up (ScreenLock), so the home is not on the screen to arrange.
fun androidHomeArranger(context: Context, teacherLocked: () -> Boolean, log: (String, Throwable?) -> Unit): HomeArranger =
    HomeArranger { HomeArrangement.arrange(AndroidHomeDevice(context, teacherLocked), log) }

// The device as HomeArrangement sees it: its default home, the Midview
// accessibility service (AccessibilityCapture, turned on by cable), the
// screen on and unlocked (and not under the teacher's lock screen), and a
// driver over the service for one arrangement.
class AndroidHomeDevice(
    context: Context,
    private val teacherLocked: () -> Boolean,
) : HomeDevice {
    private val app = context.applicationContext

    override fun defaultHome(): String? =
        try {
            val home = Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME)
            app.packageManager.resolveActivity(home, PackageManager.MATCH_DEFAULT_ONLY)?.activityInfo?.packageName
        } catch (e: RuntimeException) {
            null
        }

    override fun accessibilityRunning(): Boolean = AccessibilityCapture.instance != null

    override fun screenReady(): Boolean =
        try {
            val power = app.getSystemService(PowerManager::class.java)
            val keyguard = app.getSystemService(KeyguardManager::class.java)
            power != null && power.isInteractive && keyguard != null && !keyguard.isKeyguardLocked && !teacherLocked()
        } catch (e: RuntimeException) {
            false
        }

    override fun open(): HomeSession? {
        val service = AccessibilityCapture.instance ?: return null
        return AccessibilityHomeDriver.open(service)
    }
}

// Launcher3Home's hands and eyes through the accessibility service: the
// active window read as uiautomator reads it over the cable (only what the
// user can see, resource ids and views Android deems unimportant included:
// the launcher's cell layouts are such views, so for one arrangement the
// service asks for both and gives them back after), Home and Back as global
// actions, and taps, swipes and long-press drags as gestures (dispatchGesture,
// each waited for until Android played it).
class AccessibilityHomeDriver private constructor(
    private val service: AccessibilityService,
    private val flagsBefore: Int?,
) : HomeDriver, HomeSession {
    private val started = SystemClock.elapsedRealtime()

    override val driver: HomeDriver get() = this

    override fun close() {
        val flags = flagsBefore ?: return
        try {
            service.serviceInfo = service.serviceInfo?.apply { this.flags = flags } ?: return
        } catch (e: RuntimeException) {
            Log.w(TAG, "The accessibility flags could not be given back", e)
        }
    }

    override fun screen(): HomeNode? =
        try {
            service.rootInActiveWindow?.let { root -> read(root, 0, intArrayOf(MAX_NODES)) }
        } catch (e: RuntimeException) {
            Log.w(TAG, "The screen could not be read", e)
            null
        }

    private fun read(node: AccessibilityNodeInfo, depth: Int, budget: IntArray): HomeNode {
        val bounds = Rect()
        node.getBoundsInScreen(bounds)
        val children = mutableListOf<HomeNode>()
        if (depth < MAX_DEPTH) {
            for (i in 0 until node.childCount) {
                if (budget[0]-- <= 0) break
                val child = node.getChild(i) ?: continue
                if (child.isVisibleToUser) children += read(child, depth + 1, budget)
            }
        }
        return HomeNode(
            id = node.viewIdResourceName.orEmpty(),
            cls = node.className?.toString().orEmpty(),
            pkg = node.packageName?.toString().orEmpty(),
            text = node.text?.toString().orEmpty(),
            description = node.contentDescription?.toString().orEmpty(),
            checked = node.isChecked,
            left = bounds.left,
            top = bounds.top,
            right = bounds.right,
            bottom = bounds.bottom,
            children = children,
        )
    }

    override fun home() {
        service.performGlobalAction(AccessibilityService.GLOBAL_ACTION_HOME)
    }

    override fun back() {
        service.performGlobalAction(AccessibilityService.GLOBAL_ACTION_BACK)
    }

    override fun tap(x: Int, y: Int) {
        dispatch(GestureDescription.StrokeDescription(point(x, y), 0, TAP_MS, false))
    }

    override fun swipe(x1: Int, y1: Int, x2: Int, y2: Int, millis: Long) {
        dispatch(GestureDescription.StrokeDescription(line(x1, y1, x2, y2), 0, millis, false))
    }

    // As "input draganddrop x1 y1 x2 y2 2500": held past the long press, so
    // the launcher picks the item up, then moved and held over the target a
    // moment, so it takes the drop, then lifted.
    override fun drag(x1: Int, y1: Int, x2: Int, y2: Int) {
        val held = dispatch(GestureDescription.StrokeDescription(point(x1, y1), 0, PICK_UP_MS, true)) ?: return
        val moved = dispatch(held.continueStroke(line(x1, y1, x2, y2), 0, MOVE_MS, true)) ?: return
        dispatch(moved.continueStroke(point(x2, y2), 0, DROP_MS, false))
    }

    override fun openLauncherSettings(): Boolean {
        val intent = Intent(Launcher3Home.SETTINGS_ACTION)
            .setPackage(Launcher3Home.LAUNCHER)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        return try {
            if (service.packageManager.resolveActivity(intent, 0) == null) return false
            service.startActivity(intent)
            true
        } catch (e: ActivityNotFoundException) {
            false
        } catch (e: SecurityException) {
            Log.w(TAG, "The launcher's settings would not open", e)
            false
        }
    }

    override fun isLaunchable(pkg: String): Boolean =
        try {
            service.packageManager.getLaunchIntentForPackage(pkg) != null
        } catch (e: RuntimeException) {
            false
        }

    override fun pause(millis: Long) = Thread.sleep(millis)

    override fun elapsedMillis(): Long = SystemClock.elapsedRealtime() - started

    // Sends one stroke and waits until Android has played it; the stroke
    // when it played whole, null when it was cancelled (someone touched the
    // screen) or refused.
    private fun dispatch(stroke: GestureDescription.StrokeDescription): GestureDescription.StrokeDescription? {
        val done = CountDownLatch(1)
        val completed = AtomicBoolean(false)
        val sent = try {
            service.dispatchGesture(
                GestureDescription.Builder().addStroke(stroke).build(),
                object : AccessibilityService.GestureResultCallback() {
                    override fun onCompleted(gestureDescription: GestureDescription?) {
                        completed.set(true)
                        done.countDown()
                    }

                    override fun onCancelled(gestureDescription: GestureDescription?) = done.countDown()
                },
                callbacks,
            )
        } catch (e: RuntimeException) {
            Log.w(TAG, "A gesture was refused", e)
            false
        }
        if (!sent) return null
        done.await(stroke.duration + GESTURE_SLACK_MS, TimeUnit.MILLISECONDS)
        return if (completed.get()) stroke else null
    }

    private fun point(x: Int, y: Int) = Path().apply { moveTo(x.toFloat(), y.toFloat()) }

    private fun line(x1: Int, y1: Int, x2: Int, y2: Int) = Path().apply {
        moveTo(x1.toFloat(), y1.toFloat())
        lineTo(x2.toFloat(), y2.toFloat())
    }

    companion object {
        private const val TAG = "Midview"

        // What one screen read may hold: a launcher's screens have a few
        // hundred elements at most.
        private const val MAX_DEPTH = 40
        private const val MAX_NODES = 3_000

        private const val TAP_MS = 50L
        private const val PICK_UP_MS = 800L
        private const val MOVE_MS = 1_400L
        private const val DROP_MS = 300L
        private const val GESTURE_SLACK_MS = 1_000L

        private val callbacks: Handler by lazy {
            Handler(HandlerThread("Midview home gestures").apply { start() }.looper)
        }

        // The service asked for resource ids and every view for this
        // arrangement; the flags it had are given back on close.
        fun open(service: AccessibilityService): AccessibilityHomeDriver {
            val info = try {
                service.serviceInfo
            } catch (e: RuntimeException) {
                null
            }
            val wanted = AccessibilityServiceInfo.FLAG_REPORT_VIEW_IDS or AccessibilityServiceInfo.FLAG_INCLUDE_NOT_IMPORTANT_VIEWS
            if (info == null || info.flags and wanted == wanted) return AccessibilityHomeDriver(service, null)
            val before = info.flags
            return try {
                info.flags = before or wanted
                service.serviceInfo = info
                AccessibilityHomeDriver(service, before)
            } catch (e: RuntimeException) {
                Log.w(TAG, "The accessibility flags could not be set", e)
                AccessibilityHomeDriver(service, null)
            }
        }
    }
}
