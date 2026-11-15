package io.github.arnoneto1.midview.agent.notifications

import android.app.admin.DevicePolicyManager
import android.content.ComponentName
import android.content.Context
import android.os.Build
import android.telecom.TelecomManager
import io.github.arnoneto1.midview.agent.AdminReceiver
import io.github.arnoneto1.midview.agent.leave.PhoneLeaving
import io.github.arnoneto1.midview.agent.policy.Lockdown

// NotificationPermissions on DevicePolicyManager, as the app's AdminReceiver.
// Once the Midview is leaving the phone, nothing is denied again (as
// AndroidDevicePolicyGateway's guards); setting it back still works.
class AndroidNotificationPermissions(context: Context) : NotificationPermissions {
    private val app = context.applicationContext
    private val dpm = app.getSystemService(DevicePolicyManager::class.java)
    private val admin = ComponentName(app, AdminReceiver::class.java)

    override val sdkInt: Int = Build.VERSION.SDK_INT

    override fun isDeviceOwner(): Boolean = dpm.isDeviceOwnerApp(app.packageName)

    override fun leaving(): Boolean = PhoneLeaving.isActive(app)

    override fun installed(): List<String> = app.packageManager.getInstalledApplications(0).map { it.packageName }

    override fun calls(): Set<String> {
        val telecom = app.getSystemService(TelecomManager::class.java) ?: return emptySet()
        return setOfNotNull(telecom.defaultDialerPackage, telecom.systemDialerPackage)
    }

    // An app Android cannot say anything of counts as not denied: deny says why.
    override fun isDenied(pkg: String): Boolean = runCatching {
        dpm.getPermissionGrantState(admin, pkg, Lockdown.POST_NOTIFICATIONS) == DevicePolicyManager.PERMISSION_GRANT_STATE_DENIED
    }.getOrDefault(false)

    override fun deny(pkg: String): Boolean =
        !leaving() &&
            dpm.setPermissionGrantState(admin, pkg, Lockdown.POST_NOTIFICATIONS, DevicePolicyManager.PERMISSION_GRANT_STATE_DENIED)
}
