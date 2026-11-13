package io.github.arnoneto1.midview.agent.policy

import android.app.KeyguardManager
import android.app.admin.DevicePolicyManager
import android.content.BroadcastReceiver
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.os.SystemClock
import android.provider.Settings
import android.util.Log
import io.github.arnoneto1.midview.agent.AdminReceiver
import io.github.arnoneto1.midview.agent.leave.PhoneLeaving
import java.io.File
import java.security.SecureRandom
import java.util.concurrent.Executors

// The lock screen's calls to DevicePolicyManager and KeyguardManager, as the
// app's AdminReceiver (the Device Owner).
class AndroidScreenLockGateway(context: Context) : ScreenLockGateway {
    private val app = context.applicationContext
    private val dpm = app.getSystemService(DevicePolicyManager::class.java)
    private val keyguard = app.getSystemService(KeyguardManager::class.java)
    private val admin = ComponentName(app, AdminReceiver::class.java)

    override fun isDeviceOwner(): Boolean = dpm.isDeviceOwnerApp(app.packageName)

    override fun isDeviceSecure(): Boolean = keyguard.isDeviceSecure

    override fun isResetTokenActive(): Boolean = dpm.isResetPasswordTokenActive(admin)

    override fun setResetToken(token: ByteArray): Boolean = dpm.setResetPasswordToken(admin, token)

    // Android makes digits only a PIN (PasswordMetrics.isNumericOnly); null
    // removes the lock.
    // Once the Midview is leaving (PhoneLeaving), a PIN is only ever taken off.
    override fun resetPassword(pin: String, token: ByteArray): Boolean {
        if (pin.isNotEmpty() && PhoneLeaving.isActive(app)) return false
        return dpm.resetPasswordWithToken(admin, pin.ifEmpty { null }, token, 0)
    }

    // PASSWORD_QUALITY_NUMERIC, at least 4 digits (the admin declares
    // limit-password for it), or no rule at all.
    @Suppress("DEPRECATION")
    override fun requirePin(required: Boolean) {
        if (required) {
            check(!PhoneLeaving.isActive(app)) { "The Midview is leaving this phone" }
            dpm.setPasswordQuality(admin, DevicePolicyManager.PASSWORD_QUALITY_NUMERIC)
            dpm.setPasswordMinimumLength(admin, PIN_LENGTH)
        } else {
            dpm.setPasswordQuality(admin, DevicePolicyManager.PASSWORD_QUALITY_UNSPECIFIED)
        }
    }

    override fun bootCount(): Int = Settings.Global.getInt(app.contentResolver, Settings.Global.BOOT_COUNT, -1)

    private companion object {
        const val PIN_LENGTH = 4
    }
}

// The process's one ScreenPassword, its checks run one at a time on a thread
// of their own (Android's calls to change the lock take a moment). Its file
// is in device-protected storage (ScreenPasswordStore says why), so it also
// works at the locked boot, before the PIN is entered.
object LockScreenKeeper {
    private const val TAG = "Midview"
    private const val STATE_FILE = "screen-password.txt"
    private const val TOKEN_BYTES = 32

    private val worker = Executors.newSingleThreadExecutor { Thread(it, "Midview lock screen").apply { isDaemon = true } }

    @Volatile private var instance: ScreenPassword? = null

    fun get(context: Context): ScreenPassword =
        instance ?: synchronized(this) {
            instance ?: create(context.applicationContext).also { instance = it }
        }

    private fun create(app: Context): ScreenPassword {
        val log: (String, Throwable?) -> Unit = { text, e -> if (e == null) Log.i(TAG, text) else Log.w(TAG, text, e) }
        val folder = app.createDeviceProtectedStorageContext().filesDir
        return ScreenPassword(
            AndroidScreenLockGateway(app),
            ScreenPasswordStore(File(folder, STATE_FILE), log),
            newToken = { ByteArray(TOKEN_BYTES).also { SecureRandom().nextBytes(it) } },
            clock = SystemClock::elapsedRealtime,
            log = log,
        )
    }

    // The Midview left the phone (PhoneLeave): the lock screen's state is
    // deleted and the next get starts from nothing.
    fun forget(context: Context) {
        synchronized(this) {
            val folder = context.applicationContext.createDeviceProtectedStorageContext().filesDir
            File(folder, STATE_FILE).delete()
            File(folder, STATE_FILE + ScreenPasswordStore.BACKUP_SUFFIX).delete()
            instance = null
        }
    }

    // A check in the background; never fails its caller. done runs after it.
    // Nothing once the Midview is leaving the phone (PhoneLeaving).
    fun check(context: Context, starting: Boolean = false, done: () -> Unit = {}) {
        val app = context.applicationContext
        worker.execute {
            try {
                if (!PhoneLeaving.isActive(app)) get(app).check(starting)
            } catch (e: RuntimeException) {
                Log.e(TAG, "The lock screen check failed", e)
            } finally {
                done()
            }
        }
    }

    // Android says the lock changed (AdminReceiver): undone unless the
    // agent did it.
    fun lockChanged(context: Context) {
        if (PhoneLeaving.isActive(context)) return
        val keeper = get(context)
        if (keeper.isOwnChange()) return
        worker.execute {
            try {
                keeper.lockChanged()
            } catch (e: RuntimeException) {
                Log.e(TAG, "The lock screen check failed", e)
            }
        }
    }

    // The phone started and is still locked: the teacher's PIN is checked
    // before anyone enters one (the only part of the app that runs then).
    class AtLockedBoot : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            if (intent.action != Intent.ACTION_LOCKED_BOOT_COMPLETED) return
            val pending = goAsync()
            check(context, starting = true) { pending.finish() }
        }
    }
}
