package io.github.arnoneto1.midview.agent.classfolder

import io.github.arnoneto1.midview.agent.files.DiskReserve
import io.github.arnoneto1.midview.agent.protocol.ClassFolderEntry
import io.github.arnoneto1.midview.agent.protocol.ClassLesson
import io.github.arnoneto1.midview.agent.protocol.ClassPushHeader
import io.github.arnoneto1.midview.agent.protocol.LessonId
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Assume.assumeTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.nio.file.Files

// Download/Midview/Pasta da turma – <turma>, as the teacher's Master works it.
class PhoneClassFolderTest {
    @get:Rule val temp = TemporaryFolder()

    private val lessonId = LessonId(ByteArray(16) { 1 })
    private val oldLesson = LessonId(ByteArray(16) { 2 })
    private var lesson: ClassLesson? = ClassLesson(10, lessonId, "3º A", 1_700_000_000, ended = false)
    private var canSee = true
    private var free = Long.MAX_VALUE

    private val root by lazy { temp.newFolder("Midview") }
    private val times by lazy { ClassFileTimes(File(temp.root, "times.txt")) }
    private val folder by lazy { PhoneClassFolder(root, { lesson }, times, { canSee }, { free }) }
    private val classDir by lazy { File(root, "Pasta da turma – 3º A").apply { mkdirs() } }

    private fun file(dir: File, path: String, text: String = path) = File(dir, path).apply { parentFile!!.mkdirs(); writeText(text) }

    // A tick past a whole millisecond: what only the teacher's PC could store.
    private val teacherTicks = 638_600_000_000_001_234L

    private fun push(path: String, text: String, replacing: Pair<Long, Long>? = null, id: LessonId = lessonId) {
        val bytes = text.toByteArray()
        folder.beginPush(ClassPushHeader(id, path, bytes.size.toLong(), teacherTicks, replacing)).use {
            it.write(bytes)
            it.complete()
        }
    }

    @Test
    fun theListingFollowsThePcRules() {
        file(classDir, "b.txt")
        file(classDir, "A.pdf")
        file(classDir, "fotos/praia.jpg")
        file(classDir, "download.crdownload")
        file(classDir, "~\$trabalho.docx")
        file(classDir, ".pending-1-x.pdf")
        file(classDir, ".gitignore")
        file(classDir, ".midview-abc.parcial")
        file(File(root, ".midview-turmas/${oldLesson.hex}"), "antigo.txt")
        file(root, "do professor.pdf")

        val listing = folder.list()

        assertEquals(lessonId, listing.current)
        assertNull(listing.problem)
        assertEquals(
            listOf(".gitignore", "A.pdf", "b.txt", "fotos/praia.jpg", "antigo.txt"),
            listing.entries.map { it.path },
        )
        assertEquals(oldLesson, listing.entries.last().lessonId)
        assertEquals(ClassFolderRules.ticksOf(File(classDir, "b.txt").lastModified()), listing.entries[2].lastWriteTicks)
    }

    @Test
    fun noFolderNoAccessOrNoLessonIsSaid() {
        lesson = lesson!!.copy(turma = "7º B")
        assertEquals(PhoneClassFolder.NO_FOLDER, folder.list().problem)
        canSee = false
        assertEquals(PhoneClassFolder.NO_ACCESS, folder.list().problem)
        lesson = null
        val none = folder.list()
        assertEquals(LessonId.EMPTY, none.current)
        assertNull(none.problem)
        assertTrue(none.entries.isEmpty())
    }

    @Test
    fun aPushedFileIsListedWithTheTeachersTime() {
        classDir
        push("fotos/praia.jpg", "praia")
        val entry = folder.list().entries.single()
        assertEquals(ClassFolderEntry(lessonId, "fotos/praia.jpg", 5, teacherTicks), entry)
        assertEquals("praia", File(classDir, "fotos/praia.jpg").readText())
        // No temp left behind.
        assertEquals(listOf("praia.jpg"), File(classDir, "fotos").list()!!.toList())

        // The student saves it again: the phone's own time, so the Master sees an edit.
        File(classDir, "fotos/praia.jpg").writeText("praia editada")
        File(classDir, "fotos/praia.jpg").setLastModified(1_800_000_000_000)
        assertEquals(ClassFolderRules.ticksOf(1_800_000_000_000), folder.list().entries.single().lastWriteTicks)
    }

    // A teacher's file dated before 1970 (the wire takes any time from year 1;
    // Android none before 1970) is saved all the same, and listed with the
    // teacher's own time, so the Master never sends it again.
    @Test
    fun aFileFromBefore1970IsSavedAndListedWithTheTeachersTime() {
        classDir
        val year1601 = 504_911_232_000_000_000L
        val bytes = "antigo".toByteArray()
        folder.beginPush(ClassPushHeader(lessonId, "antigo.txt", bytes.size.toLong(), year1601, null)).use {
            it.write(bytes)
            it.complete()
        }
        assertEquals(ClassFolderEntry(lessonId, "antigo.txt", 6, year1601), folder.list().entries.single())
        assertEquals(0L, ClassFolderRules.millisOf(year1601))
        assertEquals(0L, ClassFolderRules.millisOf(0))
    }

    @Test
    fun aPushOntoAFileThatChangedIsRefused() {
        classDir
        push("a.txt", "um")
        try {
            push("a.txt", "dois")
            fail("a file there, none expected")
        } catch (e: ClassFolderRefusal) {
            assertEquals("mudou neste celular", e.message)
        }
        try {
            push("a.txt", "dois", replacing = 3L to 1L)
            fail("not the version expected")
        } catch (e: ClassFolderRefusal) {
            assertEquals("mudou neste celular", e.message)
        }
        push("a.txt", "dois", replacing = 2L to teacherTicks)
        assertEquals("dois", File(classDir, "a.txt").readText())
    }

    @Test
    fun aPushForAnotherLessonOrWithNoAccessIsRefused() {
        classDir
        for ((id, see, reason) in listOf(Triple(oldLesson, true, "aula diferente"), Triple(lessonId, false, PhoneClassFolder.NO_ACCESS))) {
            canSee = see
            try {
                push("a.txt", "x", id = id)
                fail("refused")
            } catch (e: ClassFolderRefusal) {
                assertEquals(reason, e.message)
            }
        }
    }

    @Test
    fun aPathCanNotClimbOut() {
        classDir
        push("../../fora.txt", "x")
        assertTrue(File(classDir, "fora.txt").exists())
        assertFalse(File(root, "fora.txt").exists())
        assertFalse(File(temp.root, "fora.txt").exists())
    }

    @Test
    fun aLinkIsNeverFollowed() {
        val outside = temp.newFolder("fora")
        file(outside, "segredo.txt")
        val linked = try {
            Files.createSymbolicLink(File(classDir, "link").toPath(), outside.toPath())
            true
        } catch (e: Exception) {
            false // Windows without the right to make links
        }
        assumeTrue(linked)
        assertTrue(folder.list().entries.isEmpty())
        assertNull(folder.resolveExisting(lessonId, "link/segredo.txt"))
    }

    @Test
    fun deleteRenameAndClearSetAside() {
        file(classDir, "a.jpg")
        assertEquals(true to "", folder.rename(lessonId, "a.jpg", "a (2).jpg"))
        assertTrue(File(classDir, "a (2).jpg").exists())
        file(classDir, "b.jpg")
        assertEquals(false to "já existe um arquivo com esse nome", folder.rename(lessonId, "a (2).jpg", "b.jpg"))
        assertEquals(true to "", folder.delete(lessonId, "b.jpg"))
        assertFalse(File(classDir, "b.jpg").exists())
        assertEquals(false to "arquivo não encontrado", folder.delete(lessonId, "b.jpg"))

        val aside = File(root, ".midview-turmas/${oldLesson.hex}")
        file(aside, "x/um.txt")
        file(aside, "dois.txt")
        val listed = folder.list().entries.filter { it.lessonId == oldLesson }
        assertEquals(2, listed.size)
        // A file changed since the listing stays for the Master's next round.
        File(aside, "dois.txt").writeText("mudou")
        assertEquals(true to "", folder.clearSetAside(oldLesson, listed))
        assertFalse(File(aside, "x").exists())
        assertTrue(File(aside, "dois.txt").exists())
        assertEquals(true to "", folder.clearSetAside(oldLesson, listOf(ClassFolderEntry(oldLesson, "dois.txt", 5, ClassFolderRules.ticksOf(File(aside, "dois.txt").lastModified())))))
        assertFalse(aside.exists())
        // Nothing set aside any more: no hidden folder left in Download/Midview.
        assertFalse(File(root, ".midview-turmas").exists())
    }

    // The phone keeps 100 MB free.
    @Test
    fun aFullPhoneRefuses() {
        free = DiskReserve.SPARE_BYTES + 10
        assertEquals(DiskReserve.NO_SPACE, folder.refusalFor(11))
        assertNull(folder.refusalFor(10))
        free = DiskReserve.SPARE_BYTES - 1
        assertEquals(DiskReserve.NO_SPACE, folder.refusalFor(0))
    }

    // Lessons the teacher's Master never came for do not fill the phone.
    @Test
    fun setAsideLessonsOlderThanAMonthGo() {
        val now = 1_800_000_000_000L
        val old = setAside(LessonId(ByteArray(16) { 1 }), "velho.txt", 5, now - PhoneClassFolder.SET_ASIDE_MAX_AGE_MILLIS - 1)
        val recent = setAside(LessonId(ByteArray(16) { 2 }), "novo.txt", 5, now - 1_000)
        assertEquals(listOf(LessonId(ByteArray(16) { 1 })), folder.pruneSetAside(now))
        assertFalse(old.exists())
        assertTrue(recent.exists())
    }

    @Test
    fun overTheCapTheOldestSetAsideLessonsGoFirst() {
        val now = 1_800_000_000_000L
        val first = setAside(LessonId(ByteArray(16) { 1 }), "a.txt", 40, now - 3_000)
        val second = setAside(LessonId(ByteArray(16) { 2 }), "b.txt", 40, now - 2_000)
        val third = setAside(LessonId(ByteArray(16) { 3 }), "c.txt", 40, now - 1_000)
        assertEquals(listOf(LessonId(ByteArray(16) { 1 })), folder.pruneSetAside(now, maxBytes = 100))
        assertFalse(first.exists())
        assertTrue(second.exists() && third.exists())
        // Nothing over the cap any more: nothing else goes.
        assertEquals(emptyList<LessonId>(), folder.pruneSetAside(now, maxBytes = 100))
    }

    // A lesson's folder set aside at setAsideAt, with one file of size bytes.
    private fun setAside(id: LessonId, name: String, size: Int, setAsideAt: Long): File {
        val dir = File(File(root, PhoneClassFolder.SET_ASIDE_FOLDER), id.hex).apply { mkdirs() }
        File(dir, name).writeBytes(ByteArray(size))
        dir.setLastModified(setAsideAt)
        return dir
    }
}
