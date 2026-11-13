package io.github.arnoneto1.midview.agent.leave

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.WallpaperManager
import android.app.admin.DevicePolicyManager
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.pm.ApplicationInfo
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import io.github.arnoneto1.midview.agent.AdminReceiver
import io.github.arnoneto1.midview.agent.agent.AgentService
import io.github.arnoneto1.midview.agent.agent.AgentWatchdog
import io.github.arnoneto1.midview.agent.agent.DeviceWord
import io.github.arnoneto1.midview.agent.lock.LockActivity
import io.github.arnoneto1.midview.agent.optimize.PrivateDns
import io.github.arnoneto1.midview.agent.policy.LockScreenKeeper
import io.github.arnoneto1.midview.agent.screen.AccessibilityCapture
import io.github.arnoneto1.midview.agent.screen.ConsentActivity
import io.github.arnoneto1.midview.agent.screen.ScreenCaptureService
import io.github.arnoneto1.midview.agent.sites.AlwaysOnVpn
import io.github.arnoneto1.midview.agent.sites.BlockVpnService
import io.github.arnoneto1.midview.agent.sites.ChromePolicy
import io.github.arnoneto1.midview.agent.sites.InternetBlock
import io.github.arnoneto1.midview.agent.sites.Previous
import java.io.File

// LeaveGateway on DevicePolicyManager, PackageManager and the agent's own
// pieces, as the app's AdminReceiver (the Device Owner). Its calls go
// straight to Android: the guards that keep a leaving phone from being
// locked again (AndroidDevicePolicyGateway's) are for everything else.
class AndroidLeaveGateway(context: Context, private val keep: File) : LeaveGateway {
    private val app = context.applicationContext
    private val dpm = app.getSystemService(DevicePolicyManager::class.java)
    private val pm = app.packageManager
    private val admin = ComponentName(app, AdminReceiver::class.java)

    override fun isDeviceOwner(): Boolean = dpm.isDeviceOwnerApp(app.packageName)

    override fun restrictions(): Set<String> {
        val held = dpm.getUserRestrictions(admin)
        return held.keySet().filter { held.getBoolean(it) }.toSet()
    }

    override fun clearRestriction(r: String) = dpm.clearUserRestriction(admin, r)

    override fun releaseOwnApp() {
        dpm.setUninstallBlocked(admin, app.packageName, false)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) dpm.setUserControlDisabledPackages(admin, emptyList())
    }

    override fun endLockTask() {
        LockActivity.hide()
        dpm.setLockTaskPackages(admin, emptyArray())
        dpm.setLockTaskFeatures(admin, DevicePolicyManager.LOCK_TASK_FEATURE_GLOBAL_ACTIONS)
    }

    override fun allowEveryAccessibilityService() {
        if (!dpm.setPermittedAccessibilityServices(admin, null)) throw SecurityException("accessibility services still limited")
    }

    // Hidden apps too: only MATCH_UNINSTALLED_PACKAGES lists them.
    private fun everyApp(): List<ApplicationInfo> = pm.getInstalledApplications(PackageManager.MATCH_UNINSTALLED_PACKAGES)

    override fun suspendedApps(): List<String> =
        everyApp().filter { (it.flags and ApplicationInfo.FLAG_SUSPENDED) != 0 }.map { it.packageName }

    override fun unsuspend(pkgs: List<String>): List<String> = dpm.setPackagesSuspended(admin, pkgs.toTypedArray(), false).toList()

    override fun hiddenApps(): List<String> =
        everyApp().map { it.packageName }.filter { runCatching { dpm.isApplicationHidden(admin, it) }.getOrDefault(false) }

    override fun unhide(pkg: String): Boolean = dpm.setApplicationHidden(admin, pkg, false) || !dpm.isApplicationHidden(admin, pkg)

    override fun clearChromePolicy() = dpm.setApplicationRestrictions(admin, ChromePolicy.PACKAGE, Bundle())

    override fun alwaysOnVpn(): AlwaysOnVpn? {
        val pkg = dpm.getAlwaysOnVpnPackage(admin) ?: return null
        return AlwaysOnVpn(pkg, dpm.isAlwaysOnVpnLockdownEnabled(admin))
    }

    override fun vpnBeforeBlock(): Previous? = InternetBlock.loadPrevious(File(app.filesDir, AgentService.INTERNET_BLOCK_FILE))

    override fun setAlwaysOnVpn(vpn: AlwaysOnVpn?) {
        try {
            dpm.setAlwaysOnVpnPackage(admin, vpn?.pkg, vpn?.lockdown ?: false)
        } catch (e: PackageManager.NameNotFoundException) {
            throw IllegalStateException("The VPN app ${vpn?.pkg} is not installed", e)
        }
    }

    override fun stopBlockVpn() = BlockVpnService.stop(app)

    override fun privateDns(): PrivateDns = PrivateDns(dpm.getGlobalPrivateDnsMode(admin), dpm.getGlobalPrivateDnsHost(admin))

    override fun setPrivateDnsAutomatic(): Int = dpm.setGlobalPrivateDnsModeOpportunistic(admin)

    override fun unlockOwnersNetworks() = dpm.setGlobalSetting(admin, WIFI_DEVICE_OWNER_CONFIGS_LOCKDOWN, "0")

    // The teacher's PIN goes as the teacher's "tirar o PIN" does (the reset
    // token); then nothing of the lock screen's keeping is left.
    @Suppress("DEPRECATION")
    override fun removeScreenPin(): Boolean {
        val keeper = LockScreenKeeper.get(app)
        if (keeper.teacherPinSet() && !keeper.setTeacherPin(ByteArray(0)).first) return false
        dpm.setPasswordQuality(admin, DevicePolicyManager.PASSWORD_QUALITY_UNSPECIFIED)
        runCatching { dpm.clearResetPasswordToken(admin) }
        LockScreenKeeper.forget(app)
        return true
    }

    override fun clearWallpaper() = WallpaperManager.getInstance(app).clear(WallpaperManager.FLAG_SYSTEM or WallpaperManager.FLAG_LOCK)

    override fun packages(): List<KnownPackage> =
        everyApp().map { KnownPackage(it.packageName, (it.flags and ApplicationInfo.FLAG_INSTALLED) != 0) }

    override fun installExisting(pkg: String): Boolean = dpm.installExistingPackage(admin, pkg)

    // The app's private storage holds nothing of the student's: every file
    // there goes (the teachers, the identity, the passwords, the blocks).
    // The device-protected one keeps only the leaving's own record (and its copy).
    override fun forgetTeachers() {
        app.filesDir.listFiles()?.forEach { it.deleteRecursively() }
        app.createDeviceProtectedStorageContext().filesDir.listFiles()
            ?.filter { it.absoluteFile != keep.absoluteFile && it.absolutePath != keep.absolutePath + LeaveStore.COPY_SUFFIX }
            ?.forEach { it.deleteRecursively() }
    }

    override fun clearDeviceOwner() {
        @Suppress("DEPRECATION")
        dpm.clearDeviceOwnerApp(app.packageName)
        dropAdmin()
    }

    // Android 13 leaves the app an active device admin once it is no longer
    // the Device Owner (seen on the Redmi 10C), and refuses to uninstall it
    // then ("DELETE_FAILED_DEVICE_POLICY_MANAGER"): the admin goes too. Android
    // removes it a moment later; waited for, at most a few seconds.
    private fun dropAdmin() {
        if (isDeviceOwner() || !dpm.isAdminActive(admin)) return
        dpm.removeActiveAdmin(admin)
        val until = SystemClock.elapsedRealtime() + ADMIN_GONE_MILLIS
        while (dpm.isAdminActive(admin) && SystemClock.elapsedRealtime() < until) {
            Thread.sleep(ADMIN_POLL_MILLIS)
        }
    }

    override fun stopAgent() {
        AgentWatchdog.cancel(app)
        ScreenCaptureService.stop(app)
        app.stopService(Intent(app, AgentService::class.java))
    }

    // Started right away (a Device Owner that just stopped being one, with its
    // accessibility service on, may start it from the background), and in a
    // notification for a screen that was off. The accessibility service goes
    // after it: it was only ever the teacher's.
    override fun askUninstall() {
        dropAdmin()
        // A request to see the screen (the accessibility service going) means nothing now.
        Handler(Looper.getMainLooper()).post { ConsentActivity.closeIfOpen(app) }
        val uninstall = Intent(Intent.ACTION_DELETE, Uri.fromParts("package", app.packageName, null))
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        val notifications = app.getSystemService(NotificationManager::class.java)
        notifications.createNotificationChannel(NotificationChannel(CHANNEL, "Tirar o Midview", NotificationManager.IMPORTANCE_HIGH))
        val tap = PendingIntent.getActivity(app, 0, uninstall, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        notifications.notify(
            NOTIFICATION,
            Notification.Builder(app, CHANNEL)
                .setSmallIcon(android.R.drawable.ic_menu_delete)
                .setContentTitle(TAP_TITLE)
                .setContentText(DeviceWord.say(TAP_TEXT))
                .setStyle(Notification.BigTextStyle().bigText(DeviceWord.say(TAP_TEXT)))
                .setContentIntent(tap)
                .setOngoing(true)
                .build(),
        )
        try {
            app.startActivity(uninstall)
        } catch (e: RuntimeException) {
            // The notification is still there.
        }
        AccessibilityCapture.instance?.disableSelf()
    }

    companion object {
        // Settings.Global.WIFI_DEVICE_OWNER_CONFIGS_LOCKDOWN.
        private const val WIFI_DEVICE_OWNER_CONFIGS_LOCKDOWN = "wifi_device_owner_configs_lockdown"
        private const val CHANNEL = "leaving"
        private const val NOTIFICATION = 3
        private const val ADMIN_GONE_MILLIS = 5_000L
        private const val ADMIN_POLL_MILLIS = 100L

        const val TAP_TITLE = "Toque para terminar de tirar o Midview"
        const val TAP_TEXT = "As travas da escola já saíram deste celular. Falta só confirmar a desinstalação do Midview."
    }
}
