package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

// The rules of src/Midview.Protocol/TextMessage.cs, on the phone's side.
class TextMessageTest {
    @Test fun utf8BothWays() {
        val text = "Atenção, turma: façam a página 42 ✓"
        assertArrayEquals(text.toByteArray(Charsets.UTF_8), TextMessage.encode(text))
        assertEquals(text, TextMessage.decode(TextMessage.encode(text)))
    }

    @Test fun theSpacesAroundAreDropped() =
        assertEquals("oi", TextMessage.decode("  oi \r\n".toByteArray()))

    @Test fun nothingWorthShowingIsNull() {
        assertNull(TextMessage.decode(ByteArray(0)))
        assertNull(TextMessage.decode(" \n\t ".toByteArray()))
    }

    @Test fun invalidUtf8IsNull() {
        assertNull(TextMessage.decode(byteArrayOf(0x6F, 0xC3.toByte())))
        assertNull(TextMessage.decode(byteArrayOf(0xFF.toByte(), 0x41)))
    }

    // Counted as C# counts a string: UTF-16 units, an emoji being two.
    @Test fun atMostTwoThousandUnits() {
        assertEquals(2000, TextMessage.decode("a".repeat(2000).toByteArray())?.length)
        assertNull(TextMessage.decode("a".repeat(2001).toByteArray()))
        assertEquals(2000, TextMessage.decode("😀".repeat(1000).toByteArray())?.length)
        assertNull(TextMessage.decode(("😀".repeat(1000) + "a").toByteArray()))
    }

    @Test fun theLongestTextFitsInMaxBytes() {
        assertTrue(TextMessage.encode("€".repeat(TextMessage.MAX_LENGTH)).size <= TextMessage.MAX_BYTES)
        assertTrue(TextMessage.encode("😀".repeat(TextMessage.MAX_LENGTH / 2)).size <= TextMessage.MAX_BYTES)
    }

    @Test fun halfAnEmojiCannotBeSent() {
        assertTrue(TextMessage.isEncodable("tudo certo 😀"))
        assertFalse(TextMessage.isEncodable("metade \uD83D"))
        assertFalse(TextMessage.isEncodable("\uDE00 metade"))
        assertFalse(TextMessage.isEncodable("\uD83D😀"))
    }
}
