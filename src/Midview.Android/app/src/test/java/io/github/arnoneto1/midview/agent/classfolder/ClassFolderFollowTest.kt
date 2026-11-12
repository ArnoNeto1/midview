package io.github.arnoneto1.midview.agent.classfolder

import io.github.arnoneto1.midview.agent.protocol.ClassLesson
import io.github.arnoneto1.midview.agent.protocol.LessonId
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

// The phone following the lesson: the folder made, put back, set aside.
class ClassFolderFollowTest {
    @get:Rule val temp = TemporaryFolder()

    private val first = ClassLesson(10, LessonId(ByteArray(16) { 1 }), "3º A", 1_700_000_000, ended = false)
    private val second = ClassLesson(20, LessonId(ByteArray(16) { 2 }), "7º B", 1_700_000_100, ended = false)
    private var lesson: ClassLesson = ClassLesson.none(0)
    private var canSee = true
    private val notices = mutableListOf<String?>()
    private val reminders = mutableListOf<String>()

    private val root by lazy { temp.newFolder("Midview") }
    private val folder by lazy { PhoneClassFolder(root, { lesson.takeIf { it.isOn } }, ClassFileTimes(File(temp.root, "times.txt")), { canSee }, { Long.MAX_VALUE }) }
    private val stateFile by lazy { File(temp.root, "follow.txt") }

    private fun follow() =
        ClassFolderFollow(folder, { lesson }, stateFile, { canSee }, notice = { notices += it?.turma }, remind = { reminders += it.turma })

    private fun dir(l: ClassLesson) = File(root, l.folderName)

    private fun aside(l: ClassLesson) = File(root, ".midview-turmas/${l.lessonId.hex}")

    @Test
    fun aNewLessonGetsItsFolderAndOneNotice() {
        val follow = follow()
        lesson = first
        follow.follow()
        follow.follow()
        assertTrue(dir(first).isDirectory)
        assertEquals(listOf<String?>("3º A"), notices)
        // A restart repeats nothing.
        follow().follow()
        assertEquals(listOf<String?>("3º A"), notices)
    }

    @Test
    fun aFolderTheStudentRemovedComesBack() {
        val follow = follow()
        lesson = first
        follow.follow()
        dir(first).deleteRecursively()
        follow.follow()
        assertTrue(dir(first).isDirectory)
    }

    @Test
    fun anEndedLessonsFilesAreSetAsideAndItsFolderGoes() {
        val follow = follow()
        lesson = first
        follow.follow()
        File(dir(first), "trabalho.docx").writeText("meu trabalho")
        File(dir(first), "fotos").mkdirs()
        File(dir(first), "fotos/praia.jpg").writeText("praia")
        File(dir(first), "baixando.crdownload").writeText("metade")
        File(dir(first), ".pending-1700000000-x.pdf").writeText("chegando")
        File(dir(first), ".midview-0123abcd.parcial").writeText("do professor")

        lesson = first.copy(savedAt = 11, ended = true)
        follow.follow()

        assertFalse(dir(first).exists())
        assertEquals("meu trabalho", File(aside(first), "trabalho.docx").readText())
        assertEquals("praia", File(aside(first), "fotos/praia.jpg").readText())
        assertFalse(File(aside(first), "baixando.crdownload").exists())
        assertTrue(File(root, ".midview-turmas/.nomedia").exists())
        assertEquals(listOf("3º A", null), notices)
        // The Master sees them as that lesson's set-aside files.
        assertEquals(listOf("fotos/praia.jpg" to first.lessonId, "trabalho.docx" to first.lessonId).sortedBy { it.first },
            folder.list().entries.map { it.path to it.lessonId }.sortedBy { it.first })
    }

    @Test
    fun theNextLessonOfAnotherTurmaStartsEmpty() {
        val follow = follow()
        lesson = first
        follow.follow()
        File(dir(first), "a.txt").writeText("a")
        lesson = second
        follow.follow()
        assertFalse(dir(first).exists())
        assertTrue(dir(second).isDirectory)
        assertTrue(File(aside(first), "a.txt").exists())
        assertTrue(File(root, ".midview-turmas/.nomedia").exists())
        assertEquals(listOf<String?>("3º A", "7º B"), notices)
    }

    @Test
    fun theNextLessonOfTheSameTurmaKeepsTheFolder() {
        val follow = follow()
        lesson = first
        follow.follow()
        File(dir(first), "a.txt").writeText("a")
        lesson = second.copy(turma = "3º A")
        follow.follow()
        assertTrue(dir(first).isDirectory)
        assertTrue(dir(first).list()!!.isEmpty())
        assertTrue(File(aside(first), "a.txt").exists())
    }

    @Test
    fun aNameTakenAmongTheSetAsideFilesGetsACopyName() {
        val follow = follow()
        lesson = first
        follow.follow()
        File(dir(first), "a.txt").writeText("novo")
        aside(first).mkdirs()
        File(aside(first), "a.txt").writeText("antigo")
        lesson = ClassLesson.none(30)
        follow.follow()
        assertEquals("antigo", File(aside(first), "a.txt").readText())
        assertEquals("novo", File(aside(first), "a (2).txt").readText())
    }

    @Test
    fun withoutAccessNothingIsTouchedUntilItComes() {
        canSee = false
        val follow = follow()
        lesson = first
        follow.follow()
        assertFalse(dir(first).exists())
        assertEquals(PhoneClassFolder.NO_ACCESS, follow.prepare(first))
        canSee = true
        assertNull(follow.prepare(first))
        assertTrue(dir(first).isDirectory)
    }

    // An empty lesson leaves nothing behind, not even the hidden folder.
    @Test
    fun anEmptyLessonLeavesNothingBehind() {
        val follow = follow()
        lesson = first
        follow.follow()
        lesson = ClassLesson.none(30)
        follow.follow()
        assertTrue(root.list()!!.isEmpty())
    }

    // What a listing can't carry is the student's: the folder stays with it
    // (a PC would send it to the Recycle Bin; a phone has none).
    @Test
    fun anUnlistedFileKeepsTheFolder() {
        val follow = follow()
        lesson = first
        follow.follow()
        File(dir(first), "trabalho.docx").writeText("listado")
        File(dir(first), "~${'$'}trabalho.docx").writeText("do Word")
        val deep = File(dir(first), "1/2/3/4/5/6/7/8").apply { mkdirs() }
        File(deep, "fundo.txt").writeText("fundo demais")
        File(dir(first), "vazia").mkdirs()
        lesson = ClassLesson.none(30)
        follow.follow()
        assertTrue(File(aside(first), "trabalho.docx").exists())
        assertTrue(File(dir(first), "~${'$'}trabalho.docx").exists())
        assertTrue(File(deep, "fundo.txt").exists())
        assertFalse(File(dir(first), "trabalho.docx").exists())
        // An empty subfolder goes; the student's files stay.
        assertFalse(File(dir(first), "vazia").exists())
    }

    // After a restart the notice of the lesson still on comes back, once.
    @Test
    fun aRestartRemindsTheStudentOfTheLessonOn() {
        lesson = first
        follow().follow()
        assertEquals(listOf<String?>("3º A"), notices)
        assertTrue(reminders.isEmpty())
        val restarted = follow()
        restarted.follow()
        restarted.follow()
        assertEquals(listOf("3º A"), reminders)
        assertEquals(listOf<String?>("3º A"), notices)
    }

    // With no lesson and nothing left to set aside, the agent looks rarely.
    @Test
    fun withoutALessonThereIsNothingToLookAtSoon() {
        val follow = follow()
        assertTrue(follow.needsLooking())
        follow.follow()
        assertFalse(follow.needsLooking())
        lesson = first
        assertTrue(follow.needsLooking())
        follow.follow()
        lesson = first.copy(savedAt = 11, ended = true)
        assertTrue(follow.needsLooking())
        follow.follow()
        assertFalse(follow.needsLooking())
    }

    // A look lets go of set-aside lessons a month old, once an hour at most.
    @Test
    fun aLookLetsGoOfLessonsKeptAMonth() {
        var now = 1_800_000_000_000L
        val follow = ClassFolderFollow(folder, { lesson }, stateFile, { canSee }, clock = { now })
        val old = aside(first).apply { mkdirs() }
        File(old, "x.txt").writeText("x")
        old.setLastModified(now - PhoneClassFolder.SET_ASIDE_MAX_AGE_MILLIS - 1)
        follow.follow()
        assertFalse(old.exists())

        val older = aside(second).apply { mkdirs() }
        File(older, "y.txt").writeText("y")
        older.setLastModified(now - PhoneClassFolder.SET_ASIDE_MAX_AGE_MILLIS - 1)
        now += 60_000
        follow.follow()
        assertTrue(older.exists())
        now += ClassFolderFollow.PRUNE_EVERY_MILLIS
        follow.follow()
        assertFalse(older.exists())
    }
}
