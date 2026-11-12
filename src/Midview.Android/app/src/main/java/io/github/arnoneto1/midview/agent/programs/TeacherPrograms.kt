package io.github.arnoneto1.midview.agent.programs

import android.content.Context
import io.github.arnoneto1.midview.agent.update.ApkUpload
import java.io.File
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

// The teacher's "Abrir site", "Abrir programa" (launch), "Instalar programa"
// and "Desinstalar programa" (installs) on this phone, put together for the
// agent (AgentService). The teacher's APK is received in the app's files,
// under FOLDER; what an agent stopped midway left there goes now.
class TeacherPrograms(
    context: Context,
    isLocked: () -> Boolean,
    internetBlocked: () -> Boolean,
    blockedSites: () -> Set<String>,
    protectedApps: () -> Set<String>,
    reapplyBlock: () -> Unit,
    isDeviceOwner: () -> Boolean,
    log: (String, Throwable?) -> Unit,
    hideBrowsers: () -> Unit = {},
) {
    private val folder = File(context.filesDir, FOLDER)
    private val packages = AndroidPackages(context, folder)
    private val waiting = Executors.newSingleThreadScheduledExecutor { Thread(it, "Midview open after lock").apply { isDaemon = true } }

    val launch = TeacherLaunch(
        packages, context.packageName, isLocked, internetBlocked, blockedSites,
        later = { delay, task ->
            waiting.schedule(
                Runnable {
                    try {
                        task()
                    } catch (e: RuntimeException) {
                        log("Opening what waited under the lock screen failed", e)
                    }
                },
                delay, TimeUnit.MILLISECONDS,
            )
        },
        log = log,
    )

    val installs = TeacherInstalls(
        ApkUpload(folder, APK_NAME).also { it.delete() }, packages, packages, context.packageName,
        isDeviceOwner, protectedApps, reapplyBlock, log, hideBrowsers,
    )

    // The thread that waits for the lock screen to go, ended with the agent.
    fun close() {
        waiting.shutdownNow()
    }

    private companion object {
        const val FOLDER = "teacher-app"
        const val APK_NAME = "app.apk"
    }
}
