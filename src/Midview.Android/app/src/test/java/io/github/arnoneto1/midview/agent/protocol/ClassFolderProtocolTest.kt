package io.github.arnoneto1.midview.agent.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.LocalDate
import java.time.ZoneOffset

// The class folder's payloads, byte for byte as src/Midview.Protocol writes them.
class ClassFolderProtocolTest {
    private val id = LessonId(ByteArray(16) { (it + 1).toByte() })

    @Test
    fun aLessonIsWrittenAsTheMasterWritesIt() {
        val lesson = ClassLesson(0x0102030405060708, id, "3º A", 1_700_000_000, ended = false)
        val turma = "3º A".toByteArray(Charsets.UTF_8)
        val expected = byteArrayOf(1, 1, 2, 3, 4, 5, 6, 7, 8) +
            byteArrayOf(0, 0, 0, 0, 0x65, 0x53, 0xF1.toByte(), 0x00) + byteArrayOf(0) + id.bytes +
            byteArrayOf(0, turma.size.toByte()) + turma

        assertArrayEquals(expected, lesson.encode())
        assertEquals(lesson, ClassLesson.decode(expected))
        assertTrue(lesson.isOn)
        assertEquals("Pasta da turma – 3º A", lesson.folderName)
    }

    @Test
    fun aBadLessonIsRefusedWhole() {
        val good = ClassLesson(5, id, "7º B", 10, ended = false).encode()
        assertNull(ClassLesson.decode(good + byteArrayOf(0)))
        assertNull(ClassLesson.decode(good.copyOf(good.size - 1)))
        assertNull(ClassLesson.decode(good.clone().also { it[0] = 2 }))
        assertNull(ClassLesson.decode(good.clone().also { it[17] = 2 }))
        // A turma longer than a folder name takes.
        assertNull(ClassLesson.decode(byteArrayOf(1) + ByteArray(33) + byteArrayOf(0, 61) + ByteArray(61) { 'x'.code.toByte() }))
        // Half a character of UTF-8.
        val broken = byteArrayOf(1) + ByteArray(33) + byteArrayOf(0, 1, 0xC3.toByte())
        assertNull(ClassLesson.decode(broken))
    }

    @Test
    fun noLessonAndAnEndedOneAreNotOn() {
        assertFalse(ClassLesson.none(3).isOn)
        assertFalse(ClassLesson(5, id, "7º B", 10, ended = true).isOn)
        assertFalse(ClassLesson(5, LessonId.EMPTY, "7º B", 10, ended = false).isOn)
    }

    @Test
    fun aLessonFromYesterdayStartedBeforeToday() {
        val noon = LocalDate.of(2026, 11, 12).atTime(12, 0).toEpochSecond(ZoneOffset.UTC)
        val lesson = ClassLesson(1, id, "7º B", noon, ended = false)
        assertFalse(lesson.startedBefore(LocalDate.of(2026, 11, 12), ZoneOffset.UTC))
        assertTrue(lesson.startedBefore(LocalDate.of(2026, 11, 13), ZoneOffset.UTC))
    }

    @Test
    fun lessonIdsGoToHexAndBack() {
        assertEquals("0102030405060708090a0b0c0d0e0f10", id.hex)
        assertEquals(id, LessonId.fromHex(id.hex))
        assertNull(LessonId.fromHex("xyz"))
        assertTrue(LessonId.EMPTY.isEmpty)
    }

    @Test
    fun aListingGoesInPartsTheLastSayingSo() {
        val entries = (1..5).map { ClassFolderEntry(id, "f$it.txt", it.toLong(), 638_000_000_000_000_000L + it) }
        val parts = ClassFolderWire.encodeListingParts(id, null, entries, partSize = 2)
        assertEquals(3, parts.size)
        // version, lesson, no problem, more, count
        assertEquals(1, parts[0][0].toInt())
        assertEquals(0, parts[0][17].toInt())
        assertEquals(1, parts[0][18].toInt())
        assertEquals(0, parts[2][18].toInt())
        assertEquals(listOf(0, 0, 0, 1), parts[2].copyOfRange(19, 23).map { it.toInt() })

        val problem = ClassFolderWire.encodeListingParts(id, "sem pasta", emptyList())
        assertEquals(1, problem.size)
        assertEquals(1, problem[0][17].toInt())
    }

    @Test
    fun pushHeadersFileRefsRenamesAndClearingsGoBothWays() {
        val push = ClassPushHeader(id, "fotos/praia.jpg", 1234, 638_000_000_000_000_123, 99L to 638_000_000_000_000_000)
        assertEquals(push, ClassFolderWire.decodePushHeader(ClassFolderWire.encodePushHeader(push)))
        val fresh = push.copy(replacing = null)
        assertEquals(fresh, ClassFolderWire.decodePushHeader(ClassFolderWire.encodePushHeader(fresh)))
        assertNull(ClassFolderWire.decodePushHeader(ClassFolderWire.encodePushHeader(push) + byteArrayOf(0)))

        val ref = ClassFileRef(id, "trabalho.docx")
        assertEquals(ref, ClassFolderWire.decodeFileRef(ClassFolderWire.encodeFileRef(ref)))
        val rename = ClassFileRename(id, "a.jpg", "a (2).jpg")
        assertEquals(rename, ClassFolderWire.decodeRename(ClassFolderWire.encodeRename(rename)))

        val files = listOf(ClassFolderEntry(id, "a.txt", 3, 638_000_000_000_000_001))
        assertEquals(id to files, ClassFolderWire.decodeClearSetAside(ClassFolderWire.encodeClearSetAside(id, files)))
    }

    @Test
    fun aBlankOrTooLongPathIsRefused() {
        val blank = byteArrayOf(1) + id.bytes + byteArrayOf(0, 1, ' '.code.toByte())
        assertNull(ClassFolderWire.decodeFileRef(blank))
        val long = byteArrayOf(1) + id.bytes + byteArrayOf(0x04, 0x01) + ByteArray(1025) { 'a'.code.toByte() }
        assertNull(ClassFolderWire.decodeFileRef(long))
        val badSize = ClassFolderWire.encodePushHeader(ClassPushHeader(id, "a", 1, 1, null)).also {
            // size = -1
            for (i in 0 until 8) it[1 + 16 + 3 + i] = 0xFF.toByte()
        }
        assertNull(ClassFolderWire.decodePushHeader(badSize))
    }
}
