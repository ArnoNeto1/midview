package io.github.arnoneto1.midview.agent.protocol

// A CommandAck payload that says more than yes/no, as
// src/Midview.Protocol/CommandResult.cs: 1 or 0, then UTF-8 detail for the
// teacher.
object CommandResult {
    // What the teacher reads for a command the phone doesn't do yet.
    const val NOT_ON_PHONE = "Ainda não funciona em celular."

    fun encode(succeeded: Boolean, detail: String): ByteArray {
        val text = detail.toByteArray(Charsets.UTF_8)
        val result = ByteArray(1 + text.size)
        result[0] = if (succeeded) 1 else 0
        text.copyInto(result, 1)
        return result
    }

    fun decode(payload: ByteArray): Pair<Boolean, String> =
        if (payload.isEmpty()) {
            false to ""
        } else {
            (payload[0].toInt() == 1) to String(payload, 1, payload.size - 1, Charsets.UTF_8)
        }
}
