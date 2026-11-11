package io.github.arnoneto1.midview.agent.provisioning

import android.app.Activity
import android.os.Bundle
import io.github.arnoneto1.midview.agent.AdminReceiver

// The last step of the QR-code provisioning: the app is already the Device
// Owner, so the locks go on and the agent starts before the setup goes on.
class PolicyComplianceActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        AdminReceiver.lockDownAndStart(this)
        setResult(RESULT_OK)
        finish()
    }
}
