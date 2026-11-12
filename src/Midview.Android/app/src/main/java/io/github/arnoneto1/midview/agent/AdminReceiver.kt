package io.github.arnoneto1.midview.agent

import android.app.admin.DeviceAdminReceiver
import android.content.Context
import android.content.Intent
import android.os.UserHandle
import io.github.arnoneto1.midview.agent.agent.AgentService
import io.github.arnoneto1.midview.agent.policy.AndroidDevicePolicyGateway
import io.github.arnoneto1.midview.agent.policy.LockScreenKeeper

// The app's device admin: the Master's QR code names it
// (io.github.arnoneto1.midview.agent/.AdminReceiver), and it makes the app
// the school phone's Device Owner. Once provisioning ends (or the app is
// made Device Owner by hand), the locks go on and the agent starts.
class AdminReceiver : DeviceAdminReceiver() {
    override fun onEnabled(context: Context, intent: Intent) {
        lockDownAndStart(context)
    }

    override fun onProfileProvisioningComplete(context: Context, intent: Intent) {
        lockDownAndStart(context)
    }

    // The lock screen's PIN changed (the admin declares limit-password, or
    // Android would not say): the teacher's PIN is put back unless the
    // agent changed it (LockScreenKeeper).
    override fun onPasswordChanged(context: Context, intent: Intent, user: UserHandle) {
        LockScreenKeeper.lockChanged(context)
    }

    companion object {
        fun lockDownAndStart(context: Context) {
            AndroidDevicePolicyGateway.lockDown(context)
            AgentService.start(context)
        }
    }
}
