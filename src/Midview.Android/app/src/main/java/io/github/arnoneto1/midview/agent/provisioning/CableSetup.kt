package io.github.arnoneto1.midview.agent.provisioning

import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.JsonText
import java.io.File
import org.json.JSONObject

// What the Master hands the phone through the cable while it prepares it:
// the phone's number, the Master's certificate thumbprint and the Wi-Fi.
// tablet: the Master prepared it from "Configurar tablet", so it is named
// "Tablet 07" instead of "Celular 07". optimize: "Midview otimizado" (true)
// or "Só o Midview" (false), which leaves the device's apps as they came.
data class CableSetupData(
    val number: String,
    val master: String,
    val ssid: String,
    val password: String?,
    val tablet: Boolean = false,
    val optimize: Boolean = true,
)

// The package of the "prepare" call: Base64 URL without "=" of the UTF-8
// JSON {"number","master","ssid","password"} (the Master's CableSetupPayload),
// with "kind":"tablet" after them only for a tablet and "optimize":false
// only for "Só o Midview". A Master that never sends "kind", or a kind this
// app does not know, means a phone; one that never sends "optimize", the
// optimized device it always prepared.
object CableSetupPayload {
    private val NUMBER = Regex("[0-9]{1,3}")
    private val THUMBPRINT = Regex("[0-9A-Fa-f]{40,64}")
    const val KIND_TABLET = "tablet"

    // Null for anything that is not a good package.
    fun decode(text: String?): CableSetupData? {
        if (text.isNullOrBlank()) return null
        val json = try {
            String(java.util.Base64.getUrlDecoder().decode(text.trim()), Charsets.UTF_8)
        } catch (_: IllegalArgumentException) {
            return null
        }
        val obj = JsonText.parseObject(json) ?: return null
        val number = obj.optString("number", "")
        val master = obj.optString("master", "")
        val ssid = obj.optString("ssid", "")
        val password = if (obj.isNull("password")) "" else obj.optString("password", "")
        if (!NUMBER.matches(number) || !THUMBPRINT.matches(master) || ssid.isEmpty()) return null
        val tablet = obj.optString("kind", "") == KIND_TABLET
        val optimize = obj.optBoolean("optimize", true)
        return CableSetupData(number, master.uppercase(), ssid, password.ifEmpty { null }, tablet, optimize)
    }
}

data class SchoolWifiName(val ssid: String, val open: Boolean)

// The setup file (filesDir/cable-setup.json): the number and the kind, which
// name the phone for good, whether the agent tidies it up (optimize), and the
// Wi-Fi, whose password is erased once used.
class CableSetup(private val file: File) {
    fun save(data: CableSetupData) {
        val text = JSONObject()
            .put("number", data.number)
            .put("kind", if (data.tablet) CableSetupPayload.KIND_TABLET else KIND_PHONE)
            .put("optimize", data.optimize)
            .put("ssid", data.ssid)
            .put("password", data.password ?: JSONObject.NULL)
            .put("open", data.password == null)
            .put("readyShown", false)
            .toString()
        synchronized(lock) {
            file.absoluteFile.parentFile?.mkdirs()
            writeAtomically(file, text.toByteArray(Charsets.UTF_8))
        }
    }

    fun number(): String? = synchronized(lock) { load()?.optString("number", "")?.ifEmpty { null } }

    // Whether the Master prepared this device as a tablet. A file saved
    // before the kind existed has none: a phone, as it was prepared.
    fun isTablet(): Boolean = synchronized(lock) { load()?.optString("kind", "") == CableSetupPayload.KIND_TABLET }

    // Whether the agent tidies the device up by itself (Optimizer.teacherSeen):
    // only "Só o Midview" says no. A file saved before the choice existed, or
    // a device not prepared over the cable, says yes, as it always did.
    fun optimizes(): Boolean = synchronized(lock) { load()?.optBoolean("optimize", true) ?: true }

    // Forgets the setup (a preparation that failed midway).
    fun clear() = synchronized(lock) { file.delete(); Unit }

    // The Wi-Fi to join, given once: the password is removed and the network
    // marked taken, so a second call is null (never an open network under
    // the school name). Null when there is no setup.
    fun takeWifi(): Pair<String, String?>? = synchronized(lock) {
        val obj = load() ?: return null
        val ssid = obj.optString("ssid", "")
        if (ssid.isEmpty() || obj.optBoolean("wifiTaken", false)) return null
        val password = if (obj.isNull("password")) null else obj.optString("password", "").ifEmpty { null }
        obj.put("password", JSONObject.NULL)
        obj.put("wifiTaken", true)
        store(obj)
        ssid to password
    }

    // The school's Wi-Fi as the cable setup gave it, for NetworkFree to join
    // it again: its name, and whether it is open. A file from before "open"
    // was kept says secured, so an open network is never made up under the
    // school's name. Null without a setup.
    fun schoolWifi(): SchoolWifiName? = synchronized(lock) {
        val obj = load() ?: return null
        val ssid = obj.optString("ssid", "").ifEmpty { return null }
        SchoolWifiName(ssid, obj.optBoolean("open", false))
    }

    // True only the first time, so the "Pronto" screen shows once.
    fun markReadyShown(): Boolean = synchronized(lock) {
        val obj = load() ?: return false
        if (obj.optBoolean("readyShown", false)) return false
        obj.put("readyShown", true)
        store(obj)
        true
    }

    private fun store(obj: JSONObject) = writeAtomically(file, obj.toString().toByteArray(Charsets.UTF_8))

    private fun load(): JSONObject? {
        if (!file.isFile) return null
        return try {
            JsonText.parseObject(file.readText(Charsets.UTF_8))
        } catch (_: java.io.IOException) {
            null
        }
    }

    companion object {
        const val FILE_NAME = "cable-setup.json"
        private const val KIND_PHONE = "phone"

        // One lock for every instance (the provider and the receiver run in one process).
        private val lock = Any()
    }
}
