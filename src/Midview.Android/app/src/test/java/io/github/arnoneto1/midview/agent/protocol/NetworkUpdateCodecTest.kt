package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant
import java.util.Locale

class NetworkUpdateCodecTest {
    // Same literals as PhoneOptimizeReportTests.cs: the offer is what
    // NetworkUpdate.EncodeOffer writes, the status what UpdateStatusReport accepts.
    private val offerHex =
        "000102030405060708090a0b0c0d0e0f" +
            "101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f" +
            "0000000000a00000" +
            "303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f" +
            "0000000000a00000"

    private val statusJson =
        "{\"fingerprint\":\"000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f\",\"build\":\"0.0.51\"," +
            "\"ready\":true,\"attempt\":{\"id\":\"000102030405060708090a0b0c0d0e0f\",\"state\":\"committed\"," +
            "\"detail\":\"ok\",\"updatedAt\":\"2026-11-12T15:30:45+00:00\"}}"
    private val statusNullJson = "{\"fingerprint\":null,\"build\":null,\"ready\":false,\"attempt\":null}"

    private fun hex(text: String) = text.chunked(2).map { it.toInt(16).toByte() }.toByteArray()
    private fun range(from: Int, count: Int) = ByteArray(count) { (from + it).toByte() }

    @Test
    fun decodeOffer_readsTheGoldenBytes() {
        val offer = NetworkUpdateCodec.decodeOffer(hex(offerHex))
        assertNotNull(offer)
        assertArrayEquals(range(0, 16), offer!!.attemptId)
        assertArrayEquals(range(16, 32), offer.fingerprint)
        assertEquals(0xA00000L, offer.zipSize)
        assertArrayEquals(range(48, 32), offer.zipSha256)
        assertEquals(0xA00000L, offer.uncompressedSize)
    }

    @Test
    fun decodeOffer_refusesWrongLengthAndNonPositiveSizes() {
        val good = hex(offerHex)
        assertNull(NetworkUpdateCodec.decodeOffer(good.copyOf(95)))
        assertNull(NetworkUpdateCodec.decodeOffer(good + 0))
        assertNull(NetworkUpdateCodec.decodeOffer(ByteArray(0)))

        val zeroZip = good.copyOf().also { for (i in 48 until 56) it[i] = 0 }
        assertNull(NetworkUpdateCodec.decodeOffer(zeroZip))
        val negativeZip = good.copyOf().also { it[48] = 0x80.toByte() }
        assertNull(NetworkUpdateCodec.decodeOffer(negativeZip))
        val zeroUncompressed = good.copyOf().also { for (i in 88 until 96) it[i] = 0 }
        assertNull(NetworkUpdateCodec.decodeOffer(zeroUncompressed))
    }

    @Test
    fun encodeStatus_matchesTheGoldenJson() {
        val report = UpdateStatusReport(
            "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "0.0.51", true,
            UpdateAttemptReport("000102030405060708090a0b0c0d0e0f", "committed", "ok", Instant.parse("2026-11-12T15:30:45Z")),
        )
        assertEquals(statusJson, String(NetworkUpdateCodec.encodeStatus(report), Charsets.UTF_8))
        assertEquals(
            statusNullJson,
            String(NetworkUpdateCodec.encodeStatus(UpdateStatusReport(null, null, false, null)), Charsets.UTF_8),
        )
    }

    @Test
    fun encodeStatus_escapesAndKeepsFractionsLikeDotNet() {
        val report = UpdateStatusReport(
            null, "a\"b", false,
            UpdateAttemptReport("00", "failed", "falhou: \"x\"", Instant.parse("2026-11-12T15:30:45.120Z")),
        )
        val json = String(NetworkUpdateCodec.encodeStatus(report), Charsets.UTF_8)
        assertTrue(json.contains("\"updatedAt\":\"2026-11-12T15:30:45.12+00:00\""))
        assertTrue(json.contains("\"build\":\"a\\\"b\""))
    }

    @Test
    fun encodeStatus_usesAsciiDigitsInAnyLocale() {
        val old = Locale.getDefault()
        try {
            // Arabic-Indic digits asked for explicitly: plain "ar" may keep
            // Latin digits on some JDKs and would prove nothing.
            Locale.setDefault(Locale.forLanguageTag("ar-EG-u-nu-arab"))
            val report = UpdateStatusReport(
                null, null, false,
                UpdateAttemptReport("00", "failed", "x", Instant.parse("2026-11-12T15:30:45.5Z")),
            )
            val json = String(NetworkUpdateCodec.encodeStatus(report), Charsets.UTF_8)
            assertTrue(json, json.contains("\"updatedAt\":\"2026-11-12T15:30:45.5+00:00\""))
            val optimized = PhoneOptimizeReport(Instant.parse("2026-11-12T15:30:45Z"), emptyList(), emptyList(), "on", "")
            assertTrue(String(optimized.encode(), Charsets.UTF_8).contains("\"optimizedAt\":\"2026-11-12T15:30:45+00:00\""))
        } finally {
            Locale.setDefault(old)
        }
    }

    // Same literal as PhoneOptimizeReportTests.cs, which decodes it.
    @Test
    fun encodeStatus_escapesQuoteBackslashControlAndKeepsNonAscii() {
        val report = UpdateStatusReport(
            null, null, false,
            UpdateAttemptReport("00", "failed", "a\"b\\c\u0001\u00e7", Instant.parse("2026-11-12T15:30:45Z")),
        )
        assertEquals(
            "{\"fingerprint\":null,\"build\":null,\"ready\":false,\"attempt\":{\"id\":\"00\",\"state\":\"failed\"," +
                "\"detail\":\"a\\\"b\\\\c\\u0001\u00e7\",\"updatedAt\":\"2026-11-12T15:30:45+00:00\"}}",
            String(NetworkUpdateCodec.encodeStatus(report), Charsets.UTF_8),
        )
    }
}
