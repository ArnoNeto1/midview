package io.github.arnoneto1.midview.agent.protocol

import org.json.JSONException
import org.json.JSONObject
import org.json.JSONTokener

// Compact JSON objects with their fields in a fixed order, as .NET writes
// them. JSONObject doesn't keep the order, and JSONStringer's methods return
// different types on Android and in the org.json the JVM tests use; quote is
// the same on both.
internal object JsonText {
    // fields: names with values already in JSON (see string).
    fun obj(vararg fields: Pair<String, String>): String =
        fields.joinToString(separator = ",", prefix = "{", postfix = "}") { (name, value) ->
            JSONObject.quote(name) + ":" + value
        }

    fun string(value: String?): String = if (value == null) "null" else JSONObject.quote(value)

    // The object in text, or null when it is not exactly one JSON object
    // (JSONObject(String) alone would ignore what follows it).
    fun parseObject(text: String): JSONObject? =
        try {
            val tokener = JSONTokener(text)
            val value = tokener.nextValue()
            if (value is JSONObject && tokener.nextClean().code == 0) value else null
        } catch (_: JSONException) {
            null
        }
}
