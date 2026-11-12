package io.github.arnoneto1.midview.agent

import android.app.Application
import android.app.admin.DevicePolicyManager
import android.os.UserManager
import io.github.arnoneto1.midview.agent.agent.AgentService

// Every start of the app's process starts the agent on the school phone.
// After the student clears the recent apps, some phones (MIUI) kill the app
// and Android starts it again for the service, but stops that service at
// once as an "idle" app's one: the agent is started here instead, in the
// foreground, which a Device Owner may do from the background. Not while the
// phone is still locked after a restart (the lock screen's check at the
// locked boot): the agent needs storage Android opens only after the PIN,
// and starts when the phone is unlocked.
class MidviewApplication : Application() {
    override fun onCreate() {
        super.onCreate()
        val unlocked = getSystemService(UserManager::class.java).isUserUnlocked
        if (unlocked && getSystemService(DevicePolicyManager::class.java).isDeviceOwnerApp(packageName)) {
            AgentService.ensureRunning(this, "process started")
        }
    }
}
