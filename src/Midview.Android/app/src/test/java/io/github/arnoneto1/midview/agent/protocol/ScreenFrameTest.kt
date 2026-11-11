package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ScreenFrameTest {
    // A real baseline JPEG, 2x1 px. The same bytes are in the .NET test
    // (tests/Midview.Protocol.Tests/ScreenFrameTests.cs); keep them identical.
    private val jpegHex =
        "ffd8ffe000104a46494600010100000100010000ffdb004300100b0c0e0c0a100e0d0e1211101318281a181616183123251d" +
        "283a333d3c3933383740485c4e404457453738506d51575f626768673e4d71797064785c656763ffdb004301111212181518" +
        "2f1a1a2f63423842636363636363636363636363636363636363636363636363636363636363636363636363636363636363" +
        "6363636363636363ffc00011080001000203012200021101031101ffc4001f00000105010101010101000000000000000001" +
        "02030405060708090a0bffc400b5100002010303020403050504040000017d01020300041105122131410613516107227114" +
        "328191a1082342b1c11552d1f02433627282090a161718191a25262728292a3435363738393a434445464748494a53545556" +
        "5758595a636465666768696a737475767778797a838485868788898a92939495969798999aa2a3a4a5a6a7a8a9aab2b3b4b5" +
        "b6b7b8b9bac2c3c4c5c6c7c8c9cad2d3d4d5d6d7d8d9dae1e2e3e4e5e6e7e8e9eaf1f2f3f4f5f6f7f8f9faffc4001f010003" +
        "0101010101010101010000000000000102030405060708090a0bffc400b51100020102040403040705040400010277000102" +
        "031104052131061241510761711322328108144291a1b1c109233352f0156272d10a162434e125f11718191a262728292a35" +
        "363738393a434445464748494a535455565758595a636465666768696a737475767778797a82838485868788898a92939495" +
        "969798999aa2a3a4a5a6a7a8a9aab2b3b4b5b6b7b8b9bac2c3c4c5c6c7c8c9cad2d3d4d5d6d7d8d9dae2e3e4e5e6e7e8e9ea" +
        "f2f3f4f5f6f7f8f9faffda000c03010002110311003f00e426ff005cff00ef1fe7451457dbd0fe147d118d6fe24bd59fffd9"

    private fun hex(s: String) = ByteArray(s.length / 2) { s.substring(it * 2, it * 2 + 2).toInt(16).toByte() }

    @Test fun encodeMatchesDotNetLayout() {
        val jpeg = hex(jpegHex)
        val bytes = ScreenFrame.encode(1, 2, 1, listOf(ScreenRegion(0, 0, 2, 1, jpeg)))
        // frame id 1, screen 2x1, 1 region; then x 0, y 0, w 2, h 1, JPEG length; all int32 little-endian.
        val header = byteArrayOf(
            1, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0,
            (jpeg.size and 0xFF).toByte(), (jpeg.size shr 8).toByte(), 0, 0,
        )
        assertArrayEquals(header + jpeg, bytes)
        assertEquals(16 + 20 + jpeg.size, bytes.size)
    }

    @Test fun splitKeepsEachGroupUnderEightMiB() {
        val limit = 8 * 1024 * 1024
        fun region(size: Int) = ScreenRegion(0, 0, 64, 64, ByteArray(size))
        val big = limit / 2 + 1
        val groups = ScreenFrame.split(listOf(region(big), region(big), region(10), region(limit + 5), region(7)))
        // [big] | [big, 10] | [limit+5 alone] | [7]
        assertEquals(listOf(1, 2, 1, 1), groups.map { it.size })
        for (group in groups) {
            assertTrue(group.size == 1 || group.sumOf { it.jpeg.size.toLong() } <= limit)
        }
        assertEquals(emptyList<List<ScreenRegion>>(), ScreenFrame.split(emptyList()))
    }
}
