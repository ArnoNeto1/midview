package io.github.arnoneto1.midview.agent.protocol

import java.nio.ByteBuffer
import java.nio.charset.CharacterCodingException
import java.nio.charset.CodingErrorAction

// The payload of ShowMessage and ChatText, as src/Midview.Protocol/
// TextMessage.cs: plain UTF-8 text. The same rules on both ends -- the side
// typing limits what can be entered, the side receiving refuses anything else.
object TextMessage {
    // Plenty for a classroom notice or a chat line (UTF-16 units, as C#
    // counts a string's Length).
    const val MAX_LENGTH = 2000

    // The most bytes a valid text can take: 3 per UTF-16 unit at worst.
    const val MAX_BYTES = 3 * MAX_LENGTH

    // Only text isEncodable says yes to: anything else would go out with a
    // '?' where the PC refuses to send it.
    fun encode(text: String): ByteArray = text.toByteArray(Charsets.UTF_8)

    // Null when there's nothing worth showing: invalid UTF-8, only
    // whitespace, or longer than any sender lets through.
    fun decode(payload: ByteArray): String? {
        val text = try {
            Charsets.UTF_8.newDecoder()
                .onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT)
                .decode(ByteBuffer.wrap(payload))
                .toString()
                .trim()
        } catch (e: CharacterCodingException) {
            return null
        }
        return if (text.length in 1..MAX_LENGTH) text else null
    }

    // False for text with half a surrogate pair (half an emoji pasted),
    // which UTF-8 cannot carry: the PC's TypedText refuses it too.
    fun isEncodable(text: String): Boolean {
        var i = 0
        while (i < text.length) {
            val c = text[i]
            if (Character.isHighSurrogate(c)) {
                if (i + 1 >= text.length || !Character.isLowSurrogate(text[i + 1])) return false
                i += 2
                continue
            }
            if (Character.isLowSurrogate(c)) return false
            i++
        }
        return true
    }
}
