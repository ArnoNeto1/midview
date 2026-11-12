package io.github.arnoneto1.midview.agent.update

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentSender
import android.content.pm.PackageInstaller
import android.os.Build
import android.util.Log
import java.io.File
import java.time.Instant

// What the update needs of Android's installer, behind an interface so the
// update is tested on the JVM.
interface AgentInstaller {
    // Hands the APK to Android to install over the agent; returns once it is
    // committed (Android's answer comes later, to InstallResult). Throws when
    // Android will not start it.
    fun install(apk: File, attemptId: String)

    // Whether Android is still installing an update of ours (the old agent
    // started again while it does).
    fun installing(): Boolean
}

// Installs the agent's next version over itself, silently: a PackageInstaller
// session of the Device Owner, which Android installs without asking anyone
// (from Android 12 the session also asks for that, USER_ACTION_NOT_REQUIRED).
// Android ends the agent to replace it; the new version starts by itself
// (PackageReplacedReceiver, and the watchdog) and closes the attempt
// (ApkUpdater). The student never gets a question: one Android asks anyway is
// refused (InstallResult).
class SelfInstaller(context: Context) : AgentInstaller {
    private val app = context.applicationContext
    private val sessions = app.packageManager.packageInstaller

    override fun install(apk: File, attemptId: String) {
        val params = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL).apply {
            setAppPackageName(app.packageName)
            setSize(apk.length())
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED)
            }
        }
        val id = sessions.createSession(params)
        var committed = false
        try {
            sessions.openSession(id).use { session ->
                session.openWrite(SESSION_FILE, 0, apk.length()).use { out ->
                    apk.inputStream().use { it.copyTo(out, BUFFER) }
                    session.fsync(out)
                }
                session.commit(InstallResult.sender(app, attemptId))
                committed = true
            }
        } catch (e: Exception) {
            if (!committed) runCatching { sessions.abandonSession(id) }
            throw e
        }
    }

    // A committed session stays active while Android installs it; one left
    // from before the phone restarted is not.
    override fun installing(): Boolean =
        sessions.mySessions.any { it.appPackageName == app.packageName && it.isActive }

    private companion object {
        const val SESSION_FILE = "agent.apk"
        const val BUFFER = 64 * 1024
    }
}

// Android's answer to the install of an attempt. Success is the new version's
// to record (ApkUpdater, at its start: this process is gone by then). A
// failure fails the attempt with what Android said; so does a question for
// the user (nobody is there to answer it on a school phone), whose session is
// abandoned so nothing is left waiting on the screen.
class InstallResult : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)
        if (status == PackageInstaller.STATUS_PENDING_USER_ACTION) {
            val session = intent.getIntExtra(PackageInstaller.EXTRA_SESSION_ID, -1)
            runCatching { context.packageManager.packageInstaller.abandonSession(session) }
        }
        val failure = InstallStatus.failure(status, intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE)) ?: return
        val attempt = intent.getStringExtra(EXTRA_ATTEMPT) ?: return
        Log.w(TAG, "Network update $attempt: Android did not install it ($status, ${intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE)}).")
        ApkUpdateStore(File(context.filesDir, ApkUpdater.ATTEMPT_FILE)).installFailed(attempt, failure, Instant.now())
    }

    companion object {
        private const val TAG = "Midview"
        private const val EXTRA_ATTEMPT = "io.github.arnoneto1.midview.agent.ATTEMPT"

        // Mutable from Android 12 on: Android puts the status in it.
        fun sender(context: Context, attemptId: String): IntentSender {
            val intent = Intent(context, InstallResult::class.java).putExtra(EXTRA_ATTEMPT, attemptId)
            val mutable = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) PendingIntent.FLAG_MUTABLE else 0
            return PendingIntent.getBroadcast(context, 0, intent, PendingIntent.FLAG_UPDATE_CURRENT or mutable).intentSender
        }
    }
}

// What the teacher reads for PackageInstaller's statuses: null for success.
// Android's own code (INSTALL_FAILED_..., not its English text) is kept for
// whoever looks into it.
object InstallStatus {
    fun failure(status: Int, message: String?): String? {
        val what = when (status) {
            PackageInstaller.STATUS_SUCCESS -> return null
            PackageInstaller.STATUS_PENDING_USER_ACTION -> return "o Android pediu uma confirmação na tela; nada foi instalado"
            PackageInstaller.STATUS_FAILURE_BLOCKED -> "o Android bloqueou a instalação"
            PackageInstaller.STATUS_FAILURE_ABORTED -> "a instalação foi cancelada"
            PackageInstaller.STATUS_FAILURE_INVALID -> "o Android recusou o arquivo"
            PackageInstaller.STATUS_FAILURE_CONFLICT -> "o Android recusou: conflito com o aplicativo instalado"
            PackageInstaller.STATUS_FAILURE_STORAGE -> "sem espaço no celular"
            PackageInstaller.STATUS_FAILURE_INCOMPATIBLE -> "o aplicativo não é compatível com este celular"
            else -> "o Android não instalou"
        }
        val code = message?.let { ANDROID_CODE.find(it)?.value }
        return if (code == null) what else "$what ($code)"
    }

    private val ANDROID_CODE = Regex("^INSTALL_[A-Z_]+")
}
