package io.github.arnoneto1.midview.agent

import android.app.admin.DeviceAdminReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import io.github.arnoneto1.midview.agent.agent.AgentService
import io.github.arnoneto1.midview.agent.provisioning.CableSetup
import io.github.arnoneto1.midview.agent.ui.ReadyActivity
import java.io.File
import io.github.arnoneto1.midview.agent.policy.AndroidDevicePolicyGateway

// The app's device admin: the Master names it over the cable (adb shell dpm
// set-device-owner io.github.arnoneto1.midview.agent/.AdminReceiver), and it
// makes the app the school phone's Device Owner. Once provisioning ends (or
// the app is made Device Owner by hand), the locks go on and the agent starts.
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
            // Prepared over the cable: the Wi-Fi and the name come from there.
            val setup = CableSetup(File(context.filesDir, CableSetup.FILE_NAME))
            try {
                setup.takeWifi()?.let { (ssid, password) ->
                    AndroidDevicePolicyGateway(context).addWifi(ssid, password)
                }
            } catch (e: RuntimeException) {
                Log.e("Midview", "The school Wi-Fi was not added", e)
            }
            AgentService.start(context)
            if (setup.number() != null && setup.markReadyShown()) {
                ReadyActivity.show(context)
            }
        }
    }
}
