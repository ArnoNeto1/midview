package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Test

class CommandResultTest {
    private fun hex(text: String) = text.chunked(2).map { it.toInt(16).toByte() }.toByteArray()

    // The bytes .NET's CommandResult.Encode(false, "Ainda não funciona em
    // celular.") makes: 0, then the UTF-8 text ("ã" is C3 A3).
    @Test fun failureIsZeroThenUtf8AsDotNetWritesIt() {
        assertArrayEquals(
            hex("00" + "41696e6461206ec3a36f2066756e63696f6e612065" + "6d2063656c756c61722e"),
            CommandResult.encode(false, "Ainda não funciona em celular."),
        )
    }

    @Test fun successIsOneThenTheDetail() {
        assertArrayEquals(hex("01" + "6c696265726164612061" + "74c3a920" + "31343a3332"), CommandResult.encode(true, "liberada até 14:32"))
        assertArrayEquals(byteArrayOf(1), CommandResult.encode(true, ""))
    }

    @Test fun decodeReadsWhatEncodeWrites() {
        assertEquals(false to "Ainda não funciona em celular.", CommandResult.decode(CommandResult.encode(false, "Ainda não funciona em celular.")))
        assertEquals(true to "ok", CommandResult.decode(CommandResult.encode(true, "ok")))
        assertEquals(false to "", CommandResult.decode(ByteArray(0)))
    }

    @Test fun notOnPhoneTextIsTheTeachersOne() {
        assertEquals("Ainda não funciona em celular.", CommandResult.NOT_ON_PHONE)
    }
}
