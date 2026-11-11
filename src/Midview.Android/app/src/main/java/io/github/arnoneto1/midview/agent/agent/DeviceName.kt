package io.github.arnoneto1.midview.agent.agent

import android.content.Context
import android.os.Build
import android.provider.Settings

// The phone's name, for its certificate (CN) and for the teacher's search:
// the one given in the phone's settings ("Nome do dispositivo"), or else
// "Celular" and the end of its Android ID (which tells two phones of the
// same model apart), or else its model.
object DeviceName {
    fun choose(settingsName: String?, androidId: String?, model: String?): String {
        settingsName?.trim()?.takeIf { it.isNotEmpty() }?.let { return it }
        val id = androidId?.trim().orEmpty()
        if (id.length >= 4) {
            return "Celular " + id.takeLast(4).uppercase()
        }
        val phoneModel = model?.trim().orEmpty()
        return if (phoneModel.isNotEmpty()) "Celular $phoneModel" else "Celular"
    }

    fun read(context: Context): String = choose(
        Settings.Global.getString(context.contentResolver, Settings.Global.DEVICE_NAME),
        Settings.Secure.getString(context.contentResolver, Settings.Secure.ANDROID_ID),
        Build.MODEL,
    )
}
