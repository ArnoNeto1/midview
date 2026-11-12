package io.github.arnoneto1.midview.agent.update

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import io.github.arnoneto1.midview.agent.agent.AgentService

// The app was replaced by a new version (over the network, or by cable):
// Android ended the agent to do it, and it starts again here, in the new
// version, which then closes the update's attempt (ApkUpdater).
class PackageReplacedReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == Intent.ACTION_MY_PACKAGE_REPLACED) {
            AgentService.start(context)
        }
    }
}
