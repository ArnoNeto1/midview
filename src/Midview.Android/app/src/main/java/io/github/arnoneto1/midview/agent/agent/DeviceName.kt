package io.github.arnoneto1.midview.agent.agent

import android.content.Context
import android.os.Build
import android.provider.Settings

// The phone's name, for its certificate (CN) and for the teacher's search:
// "Celular" and the end of its Android ID, which tells apart a classroom of
// the same model. Not the name in the phone's settings: MIUI fills it with
// the model, so every phone of a set would answer with the same one. Only
// without an Android ID, the model.
object DeviceName {
    fun choose(androidId: String?, model: String?): String {
        val id = androidId?.trim().orEmpty()
        if (id.length >= 4) {
            return "Celular " + id.takeLast(4).uppercase()
        }
        val phoneModel = model?.trim().orEmpty()
        return if (phoneModel.isNotEmpty()) "Celular $phoneModel" else "Celular"
    }

    fun read(context: Context): String = choose(
        Settings.Secure.getString(context.contentResolver, Settings.Secure.ANDROID_ID),
        Build.MODEL,
    )
}
