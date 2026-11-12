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

    private val root by lazy { temp.newFolder("Midview") }
    private val folder by lazy { PhoneClassFolder(root, { lesson.takeIf { it.isOn } }, ClassFileTimes(File(temp.root, "times.txt")), { canSee }, { Long.MAX_VALUE }) }
    private val stateFile by lazy { File(temp.root, "follow.txt") }

    private fun follow() = ClassFolderFollow(folder, { lesson }, stateFile, { canSee }, notice = { notices += it?.turma })

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
}
