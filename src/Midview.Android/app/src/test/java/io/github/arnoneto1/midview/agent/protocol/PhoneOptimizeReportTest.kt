package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import java.time.Instant

class PhoneOptimizeReportTest {
    // Same literals as PhoneOptimizeReportTests.cs (the .NET side).
    private val fullJson =
        "{\"optimizedAt\":\"2026-11-12T15:30:45+00:00\",\"hidden\":[\"com.miui.msa.global\",\"com.miui.analytics\"]," +
            "\"failed\":[\"com.xiaomi.mipicks\"],\"dns\":\"on\",\"dnsHost\":\"dns.adguard-dns.com\"," +
            "\"uninstalled\":[\"com.king.candycrushsaga\",\"com.zhiliaoapp.musically\"]}"
    private val emptyJson =
        "{\"optimizedAt\":null,\"hidden\":[],\"failed\":[],\"dns\":\"offInternetBlocked\",\"dnsHost\":\"\",\"uninstalled\":[]}"

    // What an agent before 0.0.74 sends: no "uninstalled".
    private val olderJson =
        "{\"optimizedAt\":\"2026-11-12T15:30:45+00:00\",\"hidden\":[\"com.miui.msa.global\",\"com.miui.analytics\"]," +
            "\"failed\":[\"com.xiaomi.mipicks\"],\"dns\":\"on\",\"dnsHost\":\"dns.adguard-dns.com\"}"

    private val at = Instant.parse("2026-11-12T15:30:45Z")

    private val full = PhoneOptimizeReport(
        at, listOf("com.miui.msa.global", "com.miui.analytics"), listOf("com.xiaomi.mipicks"),
        PhoneDnsStates.ON, "dns.adguard-dns.com", listOf("com.king.candycrushsaga", "com.zhiliaoapp.musically"),
    )
    private val empty = PhoneOptimizeReport(null, emptyList(), emptyList(), PhoneDnsStates.OFF_INTERNET_BLOCKED, "")

    @Test
    fun encode_matchesTheGoldenJson() {
        assertEquals(fullJson, String(full.encode(), Charsets.UTF_8))
        assertEquals(emptyJson, String(empty.encode(), Charsets.UTF_8))
    }

    @Test
    fun decode_readsTheGoldenJson() {
        assertEquals(full, PhoneOptimizeReport.decode(fullJson.toByteArray()))
        assertEquals(empty, PhoneOptimizeReport.decode(emptyJson.toByteArray()))
    }

    @Test
    fun decode_readsAReportWithoutUninstalledAsNone() {
        assertEquals(full.copy(uninstalled = emptyList()), PhoneOptimizeReport.decode(olderJson.toByteArray()))
        val withNull = olderJson.dropLast(1) + ",\"uninstalled\":null}"
        assertEquals(full.copy(uninstalled = emptyList()), PhoneOptimizeReport.decode(withNull.toByteArray()))
    }

    @Test
    fun decode_refusesBadJson() {
        val bad = listOf(
            "not json",
            "",
            "null",
            "{\"optimizedAt\":null,\"failed\":[],\"dns\":\"on\",\"dnsHost\":\"\"}",
            "{\"optimizedAt\":null,\"hidden\":[],\"dns\":\"on\",\"dnsHost\":\"\"}",
            "{\"optimizedAt\":null,\"hidden\":[],\"failed\":[],\"dns\":\"maybe\",\"dnsHost\":\"\"}",
            "{\"optimizedAt\":null,\"hidden\":[],\"failed\":[],\"dnsHost\":\"\"}",
            "{\"optimizedAt\":\"yesterday\",\"hidden\":[],\"failed\":[],\"dns\":\"on\",\"dnsHost\":\"\"}",
            "{\"optimizedAt\":null,\"hidden\":[1],\"failed\":[],\"dns\":\"on\",\"dnsHost\":\"\"}",
            "{\"optimizedAt\":null,\"hidden\":[],\"failed\":[],\"dns\":\"on\",\"dnsHost\":\"\",\"uninstalled\":[1]}",
            "{\"optimizedAt\":null,\"hidden\":[],\"failed\":[],\"dns\":\"on\",\"dnsHost\":\"\",\"uninstalled\":\"x\"}",
        )
        for (json in bad) {
            assertNull(json, PhoneOptimizeReport.decode(json.toByteArray()))
        }
    }

    @Test
    fun decode_refusesInvalidUtf8() {
        assertNull(PhoneOptimizeReport.decode(byteArrayOf(0x7b, 0xff.toByte(), 0xfe.toByte(), 0x7d)))
    }

    @Test
    fun encode_refusesAnUnknownDnsState() {
        try {
            PhoneOptimizeReport(null, emptyList(), emptyList(), "maybe", "").encode()
            fail("expected IllegalArgumentException")
        } catch (_: IllegalArgumentException) {
        }
    }

    @Test
    fun constantsAreTheWireValues() {
        assertEquals(98, MessageType.OPTIMIZE_PHONE)
        assertEquals(99, MessageType.PHONE_OPTIMIZE_REPORT)
        assertEquals(18, MessageType.FILE_CHUNK)
        assertEquals(25, MessageType.UPDATE_OFFER)
        assertEquals(26, MessageType.UPDATE_ACCEPTED)
        assertEquals(27, MessageType.GET_UPDATE_STATUS)
        assertEquals(28, MessageType.UPDATE_STATUS)
        assertEquals(64L * 1024 * 1024, PhoneUpdate.MAX_APK_SIZE)
        assertEquals(
            listOf("on", "offNoInternet", "offNetworkBlocks", "offInternetBlocked", "offError"),
            listOf(
                PhoneDnsStates.ON, PhoneDnsStates.OFF_NO_INTERNET, PhoneDnsStates.OFF_NETWORK_BLOCKS,
                PhoneDnsStates.OFF_INTERNET_BLOCKED, PhoneDnsStates.OFF_ERROR,
            ),
        )
        assertFalse(PhoneDnsStates.isKnown("x"))
        assertTrue(PhoneDnsStates.isKnown("on"))
    }
}
