package io.github.arnoneto1.midview.agent.power

import android.app.admin.DeviceAdminInfo
import android.app.admin.DevicePolicyManager
import android.content.ComponentName
import android.content.Context
import io.github.arnoneto1.midview.agent.AdminReceiver

// PhonePower through DevicePolicyManager, as the app's AdminReceiver.
class AndroidPhonePower(context: Context) : PhonePower {
    private val app = context.applicationContext
    private val dpm = app.getSystemService(DevicePolicyManager::class.java)
    private val admin = ComponentName(app, AdminReceiver::class.java)

    override fun canRestart(): Boolean = dpm.isDeviceOwnerApp(app.packageName)

    // Android checks the policy, Device Owner or not.
    override fun canTurnScreenOff(): Boolean =
        canRestart() && dpm.hasGrantedPolicy(admin, DeviceAdminInfo.USES_POLICY_FORCE_LOCK)

    override fun restart() = dpm.reboot(admin)

    override fun turnScreenOff() = dpm.lockNow()
}
