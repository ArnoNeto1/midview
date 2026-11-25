package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

// "Rede Wi-Fi nova da escola" on the wire, as the C# PhoneWifiNetworkTests.
class PhoneWifiNetworkTest {
    @Test fun aNetworkWithAPasswordRoundTrips() {
        val payload = PhoneWifiNetwork.encode("Escola Sala 2", "senha-da-escola")
        assertEquals(PhoneWifiNetwork.Network("Escola Sala 2", "senha-da-escola"), PhoneWifiNetwork.decode(payload))
    }

    @Test fun anOpenNetworkHasNoPassword() {
        assertArrayEquals(byteArrayOf(4, 'H'.code.toByte(), 'e'.code.toByte(), 'r'.code.toByte(), 'm'.code.toByte(), 0),
            PhoneWifiNetwork.encode("Herm", ""))
        assertEquals(PhoneWifiNetwork.Network("Herm", ""), PhoneWifiNetwork.decode(PhoneWifiNetwork.encode("Herm", "")))
    }

    @Test fun anAccentedNameIsUtf8() {
        val name = "Educação"
        assertEquals(name, PhoneWifiNetwork.decode(PhoneWifiNetwork.encode(name, ""))?.name)
    }

    // The same bytes the C# encoder writes (PhoneWifiNetworkTests).
    @Test fun theMastersBytesDecode() {
        val bytes = byteArrayOf(2, 'A'.code.toByte(), 'B'.code.toByte(), 8) + "12345678".toByteArray(Charsets.US_ASCII)
        assertEquals(PhoneWifiNetwork.Network("AB", "12345678"), PhoneWifiNetwork.decode(bytes))
    }

    @Test fun whatTheMasterNeverSendsIsRefused() {
        assertNull(PhoneWifiNetwork.decode(byteArrayOf()))
        assertNull(PhoneWifiNetwork.decode(byteArrayOf(0, 0)))
        // A password shorter than 8.
        assertNull(PhoneWifiNetwork.decode(byteArrayOf(1, 'A'.code.toByte(), 3, '1'.code.toByte(), '2'.code.toByte(), '3'.code.toByte())))
        // Lengths that don't add up.
        assertNull(PhoneWifiNetwork.decode(byteArrayOf(5, 'A'.code.toByte(), 0)))
        assertNull(PhoneWifiNetwork.decode(byteArrayOf(1, 'A'.code.toByte(), 0, 9)))
        // A name longer than 32 bytes.
        val long = "x".repeat(33).toByteArray()
        assertNull(PhoneWifiNetwork.decode(byteArrayOf(33) + long + byteArrayOf(0)))
    }
}
