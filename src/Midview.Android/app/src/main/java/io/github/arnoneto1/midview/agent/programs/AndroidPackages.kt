package io.github.arnoneto1.midview.agent.programs

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentSender
import android.content.pm.ApplicationInfo
import android.content.pm.PackageInstaller
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.Browser
import io.github.arnoneto1.midview.agent.apps.Preinstalled
import io.github.arnoneto1.midview.agent.update.ApkCheck
import io.github.arnoneto1.midview.agent.update.ApkFacts
import io.github.arnoneto1.midview.agent.update.ApkReader
import io.github.arnoneto1.midview.agent.update.InstallStatus
import io.github.arnoneto1.midview.agent.update.PackageApkReader
import java.io.File
import java.util.concurrent.CompletableFuture
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit
import java.util.concurrent.TimeoutException
import java.util.concurrent.atomic.AtomicInteger

// TeacherLaunch's and TeacherInstalls' Android side: what the PackageManager
// says of an app, opening it or a site, and the Device Owner's silent
// installs and uninstalls (PackageInstaller sessions, which Android carries
// out without asking anyone: the agent is the Device Owner).
class AndroidPackages(
    context: Context,
    private val receiveFolder: File,
    private val apks: ApkReader = PackageApkReader(context),
) : LaunchScreen, PackageFacts, PackageChanger {
    private val app = context.applicationContext
    private val pm = app.packageManager
    private val installer = pm.packageInstaller

    override fun state(pkg: String): AppState {
        val info = try {
            pm.getApplicationInfo(pkg, 0)
        } catch (_: PackageManager.NameNotFoundException) {
            return AppState.MISSING
        }
        if (!info.enabled || pm.getLaunchIntentForPackage(pkg) == null) return AppState.MISSING
        return if ((info.flags and ApplicationInfo.FLAG_SUSPENDED) != 0) AppState.BLOCKED else AppState.READY
    }

    override fun open(pkg: String) {
        val intent = pm.getLaunchIntentForPackage(pkg) ?: throw IllegalStateException("o app não tem tela para abrir")
        app.startActivity(intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_RESET_TASK_IF_NEEDED))
    }

    // A new tab each time: several sites open side by side, as on a PC.
    override fun openSite(address: String) {
        app.startActivity(
            Intent(Intent.ACTION_VIEW, Uri.parse(address))
                .setPackage(TeacherLaunch.CHROME)
                .addCategory(Intent.CATEGORY_BROWSABLE)
                .putExtra(Browser.EXTRA_CREATE_NEW_TAB, true)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
        )
    }

    override fun read(apk: File): ApkFacts? = apks.read(apk)

    @Suppress("DEPRECATION")
    override fun installed(pkg: String): PhoneAppFacts? {
        val info = try {
            pm.getPackageInfo(pkg, PackageManager.GET_SIGNING_CERTIFICATES)
        } catch (_: PackageManager.NameNotFoundException) {
            return null
        }
        val application = info.applicationInfo ?: return null
        val facts = ApkFacts(
            info.packageName,
            info.longVersionCode,
            info.versionName ?: info.longVersionCode.toString(),
            ApkCheck.signers(info.signingInfo?.apkContentsSigners.orEmpty().map { it.toByteArray() }),
        )
        val system = Preinstalled.cameWithPhone(
            (application.flags and (ApplicationInfo.FLAG_SYSTEM or ApplicationInfo.FLAG_UPDATED_SYSTEM_APP)) != 0,
            info.firstInstallTime, Build.TIME,
        )
        val label = runCatching { application.loadLabel(pm).toString().trim() }.getOrNull()?.takeIf { it.isNotEmpty() } ?: pkg
        return PhoneAppFacts(facts, label, system)
    }

    override fun isSuspended(pkg: String): Boolean =
        try {
            (pm.getApplicationInfo(pkg, 0).flags and ApplicationInfo.FLAG_SUSPENDED) != 0
        } catch (_: PackageManager.NameNotFoundException) {
            false
        }

    override fun freeBytes(): Long = receiveFolder.apply { mkdirs() }.usableSpace

    override fun install(apk: File, pkg: String): String? {
        val params = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL).apply {
            setAppPackageName(pkg)
            setSize(apk.length())
            setInstallReason(PackageManager.INSTALL_REASON_POLICY)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED)
            }
        }
        val id = installer.createSession(params)
        val (token, result) = PackageResults.expect()
        var committed = false
        try {
            installer.openSession(id).use { session ->
                session.openWrite(SESSION_FILE, 0, apk.length()).use { out ->
                    apk.inputStream().use { it.copyTo(out, BUFFER) }
                    session.fsync(out)
                }
                session.commit(PackageResults.sender(app, token))
                committed = true
            }
        } catch (e: Exception) {
            PackageResults.forget(token)
            if (!committed) runCatching { installer.abandonSession(id) }
            throw e
        }
        val (status, message) = PackageResults.await(token, result, INSTALL_WAIT_MINUTES)
            ?: return "o Android não respondeu sobre a instalação"
        if (status == PackageInstaller.STATUS_PENDING_USER_ACTION) runCatching { installer.abandonSession(id) }
        return InstallStatus.failure(status, message)
    }

    override fun uninstall(pkg: String): String? {
        val (token, result) = PackageResults.expect()
        try {
            installer.uninstall(pkg, PackageResults.sender(app, token))
        } catch (e: Exception) {
            PackageResults.forget(token)
            throw e
        }
        val (status, message) = PackageResults.await(token, result, UNINSTALL_WAIT_MINUTES)
            ?: return "o Android não respondeu sobre a desinstalação"
        return UninstallStatus.failure(status, message)
    }

    private companion object {
        const val SESSION_FILE = "app.apk"
        const val BUFFER = 64 * 1024
        const val INSTALL_WAIT_MINUTES = 5L
        const val UNINSTALL_WAIT_MINUTES = 2L
    }
}

// What the teacher reads for PackageInstaller's statuses after an uninstall:
// null for success. Android's own code (DELETE_FAILED_...) is kept.
object UninstallStatus {
    fun failure(status: Int, message: String?): String? {
        val what = when (status) {
            PackageInstaller.STATUS_SUCCESS -> return null
            PackageInstaller.STATUS_PENDING_USER_ACTION -> return "o Android pediu uma confirmação na tela; nada foi desinstalado"
            PackageInstaller.STATUS_FAILURE_BLOCKED -> "o Android não deixou desinstalar"
            else -> "o Android não desinstalou"
        }
        val code = message?.let { ANDROID_CODE.find(it)?.value }
        return if (code == null) what else "$what ($code)"
    }

    private val ANDROID_CODE = Regex("^DELETE_[A-Z_]+")
}

// Android's answers to the teacher's installs and uninstalls, each awaited by
// the connection that asked (one token each), come to PackageResult.
object PackageResults {
    private val next = AtomicInteger(1)
    private val waiting = ConcurrentHashMap<Int, CompletableFuture<Pair<Int, String?>>>()

    fun expect(): Pair<Int, CompletableFuture<Pair<Int, String?>>> {
        val token = next.getAndIncrement()
        val result = CompletableFuture<Pair<Int, String?>>()
        waiting[token] = result
        return token to result
    }

    fun forget(token: Int) {
        waiting.remove(token)
    }

    // Null when Android said nothing in time.
    fun await(token: Int, result: CompletableFuture<Pair<Int, String?>>, minutes: Long): Pair<Int, String?>? =
        try {
            result.get(minutes, TimeUnit.MINUTES)
        } catch (_: TimeoutException) {
            null
        } finally {
            forget(token)
        }

    fun complete(token: Int, status: Int, message: String?) {
        waiting[token]?.complete(status to message)
    }

    // Mutable from Android 12 on: Android puts the status in it.
    fun sender(context: Context, token: Int): IntentSender {
        val intent = Intent(context, PackageResult::class.java).putExtra(EXTRA_TOKEN, token)
        val mutable = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) PendingIntent.FLAG_MUTABLE else 0
        return PendingIntent.getBroadcast(context, token, intent, PendingIntent.FLAG_UPDATE_CURRENT or mutable).intentSender
    }

    const val EXTRA_TOKEN = "io.github.arnoneto1.midview.agent.PACKAGE_TOKEN"
}

// Android's answer to one of the teacher's installs or uninstalls.
class PackageResult : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val token = intent.getIntExtra(PackageResults.EXTRA_TOKEN, -1)
        val status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)
        PackageResults.complete(token, status, intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE))
    }
}
