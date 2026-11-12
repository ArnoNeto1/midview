package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test

class PhoneAppPolicyTest {
    // Same bytes as PhoneAppPolicyTests.cs (the .NET side).
    private val blockListHex =
        "01" + "000000006553f100" + "01" + "0002" +
            "1a636f6d2e676f6f676c652e616e64726f69642e796f7574756265" +
            "0c636f6d2e7768617473617070"
    private val liftedHex = "01" + "000000006553f100" + "00" + "0000"
    private val allowOnlyHex = "01" + "000000006553f101" + "02" + "0001" + "11636f6d2e6578616d706c652e6170705f31"
    private val header = "01" + "000000006553f100"

    private fun hex(text: String) = text.chunked(2).map { it.toInt(16).toByte() }.toByteArray()

    private fun assertRefused(block: () -> Unit) {
        try {
            block()
            fail("expected IllegalArgumentException")
        } catch (_: IllegalArgumentException) {
        }
    }

    @Test fun messageNumbersMatchDotNet() {
        assertEquals(93, MessageType.SET_PHONE_APP_BLOCK)
        assertEquals(94, MessageType.GET_PHONE_APP_BLOCK)
        assertEquals(95, MessageType.PHONE_APP_BLOCK_STATUS)
        assertEquals(96, MessageType.GET_PHONE_APPS)
        assertEquals(97, MessageType.PHONE_APPS)
    }

    @Test fun modesMatchDotNet() {
        assertEquals(0, AppBlockMode.NONE.code)
        assertEquals(1, AppBlockMode.BLOCK_LIST.code)
        assertEquals(2, AppBlockMode.ALLOW_ONLY.code)
    }

    @Test fun encodeMatchesTheGoldenBytes() {
        val policy = PhoneAppPolicy(1_700_000_000L, AppBlockMode.BLOCK_LIST, listOf("com.google.android.youtube", "com.whatsapp"))
        assertArrayEquals(hex(blockListHex), policy.encode())
        assertArrayEquals(hex(liftedHex), PhoneAppPolicy.lifted(1_700_000_000L).encode())
        assertArrayEquals(hex(allowOnlyHex), PhoneAppPolicy(1_700_000_001L, AppBlockMode.ALLOW_ONLY, listOf("com.example.app_1")).encode())
    }

    @Test fun decodeReadsTheGoldenBytes() {
        val policy = PhoneAppPolicy.decode(hex(blockListHex))
        assertNotNull(policy)
        assertEquals(1_700_000_000L, policy!!.savedAt)
        assertEquals(AppBlockMode.BLOCK_LIST, policy.mode)
        assertEquals(listOf("com.google.android.youtube", "com.whatsapp"), policy.packages)
        assertFalse(PhoneAppPolicy.decode(hex(liftedHex))!!.isActive)
    }

    @Test fun packageNamesFollowTheAndroidPattern() {
        for (ok in listOf("com.whatsapp", "com.example.app_1", "A.b", "br.gov.Escola2")) {
            assertTrue(ok, PhoneAppPolicy.isValidPackageName(ok))
        }
        for (bad in listOf("whatsapp", "com.", ".com.x", "1com.x", "com..x", "com.x-y", "com.x\n", "com.açai", "chrome.exe ", "")) {
            assertFalse(bad, PhoneAppPolicy.isValidPackageName(bad))
        }
        assertFalse(PhoneAppPolicy.isValidPackageName(null))
        assertFalse(PhoneAppPolicy.isValidPackageName("a." + "b".repeat(254)))
        assertTrue(PhoneAppPolicy.isValidPackageName("a." + "b".repeat(253)))
    }

    @Test fun encodeRefusesInvalidPolicies() {
        assertRefused { PhoneAppPolicy(1, AppBlockMode.BLOCK_LIST, listOf("chrome.exe ")).encode() }
        assertRefused { PhoneAppPolicy(1, AppBlockMode.BLOCK_LIST, listOf("com.a", "com.a")).encode() }
        assertRefused { PhoneAppPolicy(1, AppBlockMode.NONE, listOf("com.a")).encode() }
        assertRefused { PhoneAppPolicy(1, AppBlockMode.BLOCK_LIST, (0..200).map { "com.app$it" }).encode() }
    }

    @Test fun twoHundredPackagesRoundTrip() {
        val names = (0 until 200).map { "com.app$it" }
        assertEquals(names, PhoneAppPolicy.decode(PhoneAppPolicy(5, AppBlockMode.ALLOW_ONLY, names).encode())!!.packages)
    }

    @Test fun decodeRejectsTrailingBytesAndTruncation() {
        assertNull(PhoneAppPolicy.decode(hex(blockListHex + "00")))
        assertNull(PhoneAppPolicy.decode(hex(blockListHex.dropLast(2))))
    }

    @Test fun decodeRejectsABadVersionOrShortHeader() {
        assertNull(PhoneAppPolicy.decode(hex("02" + blockListHex.drop(2))))
        assertNull(PhoneAppPolicy.decode(ByteArray(0)))
        assertNull(PhoneAppPolicy.decode(hex("01000000006553f10001")))
    }

    @Test fun decodeRejectsZeroLengthNamesBadUtf8AndDuplicates() {
        assertNull(PhoneAppPolicy.decode(hex(header + "01" + "0001" + "00")))
        assertNull(PhoneAppPolicy.decode(hex(header + "01" + "0001" + "03" + "c328c3")))
        assertNull(PhoneAppPolicy.decode(hex(header + "01" + "0002" + "05636f6d2e61" + "05636f6d2e61")))
    }

    @Test fun decodeRejectsAnInvalidPackageNameAndAnUnknownMode() {
        assertNull(PhoneAppPolicy.decode(hex(header + "01" + "0001" + "08" + "7768617473617070")))
        assertNull(PhoneAppPolicy.decode(hex(header + "07" + "0000")))
    }

    @Test fun decodeRejectsNoneWithNamesAndTooManyNames() {
        assertNull(PhoneAppPolicy.decode(hex(header + "00" + "0001" + "05636f6d2e61")))
        assertNull(PhoneAppPolicy.decode(hex(header + "01" + "00c9")))
    }

    @Test fun sameRulesIgnoresOrderAndSavedAt() {
        val a = PhoneAppPolicy(1, AppBlockMode.BLOCK_LIST, listOf("com.a", "com.b"))
        assertTrue(a.sameRulesAs(PhoneAppPolicy(9, AppBlockMode.BLOCK_LIST, listOf("com.b", "com.a"))))
        assertFalse(a.sameRulesAs(PhoneAppPolicy(1, AppBlockMode.ALLOW_ONLY, listOf("com.a", "com.b"))))
        assertFalse(a.sameRulesAs(PhoneAppPolicy(1, AppBlockMode.BLOCK_LIST, listOf("com.a", "com.c"))))
        assertFalse(a.sameRulesAs(PhoneAppPolicy(1, AppBlockMode.BLOCK_LIST, listOf("com.a"))))
        assertTrue(PhoneAppPolicy.lifted(1).sameRulesAs(PhoneAppPolicy.lifted(2)))
    }
}
