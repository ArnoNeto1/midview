package io.github.arnoneto1.midview.agent

import android.app.admin.DeviceAdminReceiver
import android.content.Context
import android.content.Intent
import io.github.arnoneto1.midview.agent.agent.AgentService
import io.github.arnoneto1.midview.agent.policy.AndroidDevicePolicyGateway

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

    companion object {
        fun lockDownAndStart(context: Context) {
            AndroidDevicePolicyGateway.lockDown(context)
            AgentService.start(context)
        }
    }
}
