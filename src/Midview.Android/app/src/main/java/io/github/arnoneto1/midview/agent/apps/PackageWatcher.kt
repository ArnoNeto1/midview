package io.github.arnoneto1.midview.agent.apps

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Build
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

// An app installed, updated, removed or with its components turned on or off:
// the app block is applied again (off the main thread), so an app installed
// during "Liberar só estes" is suspended as it arrives. Several changes in a
// row (an update is a removal and an install) make one apply.
class PackageWatcher(private val context: Context, private val changed: () -> Unit) {
    private val worker: ExecutorService = Executors.newSingleThreadExecutor { Thread(it, "Midview app block") }
    private val pending = AtomicBoolean(false)

    private val receiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) = run()
    }

    // Also runs once now: apps installed while the agent was not running.
    fun start() {
        val filter = IntentFilter().apply {
            addAction(Intent.ACTION_PACKAGE_ADDED)
            addAction(Intent.ACTION_PACKAGE_REPLACED)
            addAction(Intent.ACTION_PACKAGE_REMOVED)
            addAction(Intent.ACTION_PACKAGE_CHANGED)
            addDataScheme("package")
        }
        // Package broadcasts come only from the system.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            context.registerReceiver(receiver, filter, Context.RECEIVER_EXPORTED)
        } else {
            context.registerReceiver(receiver, filter)
        }
        run()
    }

    fun stop() {
        runCatching { context.unregisterReceiver(receiver) }
        worker.shutdown()
    }

    // Runs the apply on the worker, unless one is already waiting to run.
    fun run() {
        if (!pending.compareAndSet(false, true)) return
        runCatching {
            worker.execute {
                pending.set(false)
                changed()
            }
        }.onFailure { pending.set(false) }
    }
}
