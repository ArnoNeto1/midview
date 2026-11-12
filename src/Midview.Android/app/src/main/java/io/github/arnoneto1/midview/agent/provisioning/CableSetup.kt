package io.github.arnoneto1.midview.agent.provisioning

import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.JsonText
import java.io.File
import org.json.JSONObject

// What the Master hands the phone through the cable while it prepares it:
// the phone's number, the Master's certificate thumbprint and the Wi-Fi.
data class CableSetupData(
    val number: String,
    val master: String,
    val ssid: String,
    val password: String?,
)

// The package of the "prepare" call: Base64 URL without "=" of the UTF-8
// JSON {"number","master","ssid","password"} (the Master's CableSetupPayload).
object CableSetupPayload {
    private val NUMBER = Regex("[0-9]{1,3}")
    private val THUMBPRINT = Regex("[0-9A-Fa-f]{40,64}")

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
        return CableSetupData(number, master.uppercase(), ssid, password.ifEmpty { null })
    }
}

// The setup file (filesDir/cable-setup.json): the number, which names the
// phone for good, and the Wi-Fi, whose password is erased once used.
class CableSetup(private val file: File) {
    fun save(data: CableSetupData) {
        val text = JSONObject()
            .put("number", data.number)
            .put("ssid", data.ssid)
            .put("password", data.password ?: JSONObject.NULL)
            .put("readyShown", false)
            .toString()
        synchronized(lock) {
            file.absoluteFile.parentFile?.mkdirs()
            writeAtomically(file, text.toByteArray(Charsets.UTF_8))
        }
    }

    fun number(): String? = synchronized(lock) { load()?.optString("number", "")?.ifEmpty { null } }

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

        // One lock for every instance (the provider and the receiver run in one process).
        private val lock = Any()
    }
}
