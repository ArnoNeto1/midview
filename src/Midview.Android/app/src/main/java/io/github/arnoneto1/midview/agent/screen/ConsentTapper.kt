package io.github.arnoneto1.midview.agent.screen

import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.util.Log
import android.view.accessibility.AccessibilityNodeInfo

// Presses "Iniciar agora" on the system's request to capture the screen, for
// a few seconds after ConsentActivity asked (ConsentTap says which window and
// which button), through the agent's accessibility service: the student is
// not asked on a school device. Nothing happens without the service. Main
// thread.
internal object ConsentTapper {
    private const val TAG = "Midview"
    private const val EVERY_MILLIS = 300L
    private const val MAX_NODES = 200

    private val main = Handler(Looper.getMainLooper())

    // When the agent last asked (elapsedRealtime); null once done.
    private var requestedAt: Long? = null

    private val look = object : Runnable {
        override fun run() {
            if (!ConsentTap.due(requestedAt, SystemClock.elapsedRealtime()) || pressed()) {
                requestedAt = null
                return
            }
            main.postDelayed(this, EVERY_MILLIS)
        }
    }

    // ConsentActivity just asked Android to capture the screen.
    fun asked() {
        main.post {
            requestedAt = SystemClock.elapsedRealtime()
            main.removeCallbacks(look)
            main.postDelayed(look, EVERY_MILLIS)
        }
    }

    private fun pressed(): Boolean {
        val service = AccessibilityCapture.instance ?: return false
        val windows = try {
            service.windows
        } catch (e: RuntimeException) {
            return false
        }
        for (window in windows) {
            val root = window.root ?: continue
            val nodes = nodes(root)
            if (!ConsentTap.isOwnRequest(root.packageName, nodes.flatMap { listOf(it.text, it.contentDescription) })) continue
            val button = nodes.firstOrNull { ConsentTap.isStartButton(it.viewIdResourceName, it.text) } ?: continue
            val clicked = button.performAction(AccessibilityNodeInfo.ACTION_CLICK)
            Log.i(TAG, "The system asked to capture the screen: \"Iniciar agora\" pressed by the agent ($clicked).")
            return clicked
        }
        return false
    }

    // The window's nodes, breadth first, at most MAX_NODES.
    private fun nodes(root: AccessibilityNodeInfo): List<AccessibilityNodeInfo> {
        val found = mutableListOf(root)
        var next = 0
        while (next < found.size && found.size < MAX_NODES) {
            val node = found[next++]
            for (i in 0 until node.childCount) {
                node.getChild(i)?.let { found += it }
            }
        }
        return found
    }
}
