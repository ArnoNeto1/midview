package io.github.arnoneto1.midview.agent.protocol

import org.json.JSONObject

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
}
