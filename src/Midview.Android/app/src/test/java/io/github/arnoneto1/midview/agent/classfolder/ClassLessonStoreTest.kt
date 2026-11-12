package io.github.arnoneto1.midview.agent.classfolder

import io.github.arnoneto1.midview.agent.protocol.ClassLesson
import io.github.arnoneto1.midview.agent.protocol.LessonId
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.time.LocalDate
import java.time.ZoneId

// The lesson on a phone, by the PC Agent's rules, with no class folders' home.
class ClassLessonStoreTest {
    @get:Rule val temp = TemporaryFolder()

    private var today = LocalDate.of(2026, 11, 12)
    private val prepared = mutableListOf<String>()
    private var changes = 0
    private var problem: String? = null

    private val dir by lazy { temp.newFolder() }

    private fun store() = ClassLessonStore(
        File(dir, "class-lesson.bin"), { today },
        prepare = { prepared += it.turma; problem }, changed = { changes++ },
    )

    private fun id(n: Int) = LessonId(ByteArray(16) { n.toByte() })

    private fun startedToday() = today.atTime(8, 0).atZone(ZoneId.systemDefault()).toEpochSecond()

    private fun lesson(n: Int, savedAt: Long, turma: String = "3º A", ended: Boolean = false) =
        ClassLesson(savedAt, id(n), turma, startedToday(), ended)

    @Test
    fun aStartIsKeptAndTheFolderPrepared() {
        val store = store()
        assertEquals(true to "aula do 3º A", store.handle(lesson(1, 100).encode()))
        assertEquals(listOf("3º A"), prepared)
        assertEquals(1, changes)
        // It holds after a restart.
        assertEquals(lesson(1, 100), store().current)
    }

    @Test
    fun anOlderLessonOrAnEndOfAnotherOneIsNotTaken() {
        val store = store()
        store.handle(lesson(1, 100).encode())
        assertEquals(true to "mantida a aula mais nova: aula do 3º A", store.handle(lesson(2, 90, "7º B").encode()))
        assertEquals(true to "mantida a aula mais nova: aula do 3º A", store.handle(lesson(2, 200, ended = true).encode()))
        assertEquals(lesson(1, 100), store.current)
        assertEquals(true to "aula encerrada", store.handle(lesson(1, 200, ended = true).encode()))
        assertFalse(store.current.isOn)
    }

    @Test
    fun anotherLessonInTheSameSecondIsRefused() {
        val store = store()
        store.handle(lesson(1, 100).encode())
        assertEquals(false to "outra aula foi salva no mesmo segundo; envie de novo", store.handle(lesson(2, 100, "7º B").encode()))
        // The same lesson again changes nothing.
        assertEquals(true to "aula do 3º A", store.handle(lesson(1, 100).encode()))
    }

    // A phone that goes from room to room follows each room's newest lesson:
    // no teacher PC is kept as the home that alone may start one.
    @Test
    fun theNewestLessonOfAnyTrustedTeacherIsTaken() {
        val store = store()
        store.handle(lesson(1, 100).encode())
        store.handle(lesson(1, 101, ended = true).encode())
        assertEquals(true to "aula do 7º B", store.handle(lesson(2, 200, "7º B").encode()))
        assertTrue(File(dir, "class-lesson.bin").exists())
        assertFalse(File(dir, "class-home.bin").exists())
    }

    @Test
    fun aLessonFromAnEarlierDayEndsByItself() {
        val store = store()
        store.handle(lesson(1, 100).encode())
        changes = 0
        store.endIfFromAnEarlierDay()
        assertTrue(store.current.isOn)
        assertEquals(0, changes)
        today = today.plusDays(1)
        store.endIfFromAnEarlierDay()
        assertFalse(store.current.isOn)
        assertEquals(101, store.current.savedAt)
        assertEquals(1, changes)
        // The Master sending yesterday's lesson again doesn't start it.
        assertEquals(true to "mantida a aula mais nova: aula encerrada", store.handle(lesson(1, 100).encode()))
    }

    @Test
    fun aBadTurmaOrPayloadIsRefused() {
        val store = store()
        assertEquals(false to "nome de turma inválido", store.handle(lesson(1, 100, "../fora").encode()))
        assertEquals(false to "nome de turma inválido", store.handle(lesson(1, 100, "\${x}").encode()))
        assertEquals(false to "pedido inválido", store.handle(byteArrayOf(1, 2)))
        assertFalse(store.current.isOn)
    }

    @Test
    fun aFolderThatCantBeMadeIsSaid() {
        problem = "sem acesso a todos os arquivos neste celular"
        assertEquals(
            true to "aula do 3º A; não foi possível preparar a pasta agora: sem acesso a todos os arquivos neste celular",
            store().handle(lesson(1, 100).encode()),
        )
    }

    @Test
    fun aCorruptFileMeansNoLesson() {
        File(dir, "class-lesson.bin").writeBytes(byteArrayOf(7, 7, 7))
        assertFalse(store().current.isOn)
        assertTrue(File(dir, "class-lesson.bin.corrupt").exists())
    }
}
