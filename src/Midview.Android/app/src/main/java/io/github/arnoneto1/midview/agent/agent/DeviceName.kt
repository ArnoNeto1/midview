package io.github.arnoneto1.midview.agent.agent

import android.content.Context
import android.os.Build
import android.provider.Settings
import io.github.arnoneto1.midview.agent.provisioning.CableSetup
import java.io.File

// The phone's name, for its certificate (CN) and for the teacher's search:
// "Celular" and the end of its Android ID, which tells apart a classroom of
// the same model. Not the name in the phone's settings: MIUI fills it with
// the model, so every phone of a set would answer with the same one. Only
// without an Android ID, the model.
object DeviceName {
    // number: the one the Master gave while preparing the phone over the
    // cable; it names the phone for good ("Celular 07"). tablet: the Master
    // prepared it as a tablet, so the number makes "Tablet 07"; without a
    // number it changes nothing, as only the cable gives both.
    fun choose(number: String?, androidId: String?, model: String?, tablet: Boolean = false): String {
        val given = number?.trim().orEmpty()
        if (given.isNotEmpty()) {
            return if (tablet) "Tablet $given" else "Celular $given"
        }
        val id = androidId?.trim().orEmpty()
        if (id.length >= 4) {
            return "Celular " + id.takeLast(4).uppercase()
        }
        val phoneModel = model?.trim().orEmpty()
        return if (phoneModel.isNotEmpty()) "Celular $phoneModel" else "Celular"
    }

    fun read(context: Context): String {
        val setup = CableSetup(File(context.filesDir, CableSetup.FILE_NAME))
        return choose(
            setup.number(),
            Settings.Secure.getString(context.contentResolver, Settings.Secure.ANDROID_ID),
            Build.MODEL,
            setup.isTablet(),
        )
    }
}
