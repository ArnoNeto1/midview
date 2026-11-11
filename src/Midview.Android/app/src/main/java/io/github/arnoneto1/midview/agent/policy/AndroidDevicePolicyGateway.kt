package io.github.arnoneto1.midview.agent.policy

import android.app.admin.DevicePolicyManager
import android.content.ComponentName
import android.content.Context
import android.content.pm.ApplicationInfo
import android.os.Build
import android.util.Log
import io.github.arnoneto1.midview.agent.AdminReceiver

// DevicePolicyManager for Lockdown, as the app's AdminReceiver.
class AndroidDevicePolicyGateway(context: Context) : DevicePolicyGateway {
    private val app = context.applicationContext
    private val dpm = app.getSystemService(DevicePolicyManager::class.java)
    private val admin = ComponentName(app, AdminReceiver::class.java)

    override val sdkInt: Int = Build.VERSION.SDK_INT

    override val debuggable: Boolean = (app.applicationInfo.flags and ApplicationInfo.FLAG_DEBUGGABLE) != 0

    override fun isDeviceOwner(): Boolean = dpm.isDeviceOwnerApp(app.packageName)

    override fun addUserRestriction(r: String) = dpm.addUserRestriction(admin, r)

    override fun setUninstallBlocked(pkg: String, blocked: Boolean) = dpm.setUninstallBlocked(admin, pkg, blocked)

    override fun setUserControlDisabled(pkgs: List<String>) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            dpm.setUserControlDisabledPackages(admin, pkgs)
        }
    }

    override fun grantPermission(pkg: String, permission: String) {
        if (!dpm.setPermissionGrantState(admin, pkg, permission, DevicePolicyManager.PERMISSION_GRANT_STATE_GRANTED)) {
            throw SecurityException("Permission $permission was not granted to $pkg")
        }
    }

    override fun setLockTaskPackages(pkgs: List<String>) = dpm.setLockTaskPackages(admin, pkgs.toTypedArray())

    override fun setLockTaskFeatures(features: Int) = dpm.setLockTaskFeatures(admin, features)

    override fun setPermittedAccessibilityServices(pkgs: List<String>?) {
        if (!dpm.setPermittedAccessibilityServices(admin, pkgs)) {
            throw SecurityException("Accessibility services $pkgs were not permitted")
        }
    }

    // Whether "Bloquear telas" can hold this phone: the Device Owner, with
    // lock task mode allowed for the app (Lockdown).
    fun canLockScreen(): Boolean = isDeviceOwner() && dpm.isLockTaskPermitted(app.packageName)

    companion object {
        private const val TAG = "Midview"

        // Applies the locks and never fails its caller: a refused lock is
        // logged, and the next start of the agent tries again.
        fun lockDown(context: Context) {
            try {
                if (Lockdown(AndroidDevicePolicyGateway(context)).apply()) {
                    Log.i(TAG, "School phone locks applied.")
                }
            } catch (e: RuntimeException) {
                Log.e(TAG, "A school phone lock was refused", e)
            }
        }
    }
}
