package io.github.arnoneto1.midview.agent.classfolder

import io.github.arnoneto1.midview.agent.protocol.ClassHome
import io.github.arnoneto1.midview.agent.protocol.ClassHomeClaim
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
import java.time.LocalDate
import java.time.ZoneId

// The lesson and the class folders' home on a phone, by the PC Agent's rules.
class ClassLessonStoreTest {
    @get:Rule val temp = TemporaryFolder()

    private val home = "A".repeat(40)
    private val colleague = "B".repeat(40)
    private val address = "192.168.0.10"
    private var today = LocalDate.of(2026, 11, 12)
    private val prepared = mutableListOf<String>()
    private var changes = 0
    private var problem: String? = null

    private val dir by lazy { temp.newFolder() }

    private fun store() = ClassLessonStore(
        File(dir, "class-lesson.bin"), ClassHomeStore(File(dir, "class-home.bin")), { today },
        prepare = { prepared += it.turma; problem }, changed = { changes++ },
    )

    private fun id(n: Int) = LessonId(ByteArray(16) { n.toByte() })

    private fun startedToday() = today.atTime(8, 0).atZone(ZoneId.systemDefault()).toEpochSecond()

    private fun lesson(n: Int, savedAt: Long, turma: String = "3º A", ended: Boolean = false) =
        ClassLesson(savedAt, id(n), turma, startedToday(), ended)

    @Test
    fun aStartIsKeptAndTheFolderPrepared() {
        val store = store()
        assertEquals(true to "aula do 3º A", store.handle(lesson(1, 100).encode(), home, address))
        assertEquals(listOf("3º A"), prepared)
        assertEquals(1, changes)
        // It holds after a restart.
        assertEquals(lesson(1, 100), store().current)
        // The first PC to start a lesson becomes the home, with no name.
        assertEquals(home, store.home.current?.thumbprint)
        assertEquals("", store.home.current?.name)
    }

    @Test
    fun anOlderLessonOrAnEndOfAnotherOneIsNotTaken() {
        val store = store()
        store.handle(lesson(1, 100).encode(), home, address)
        assertEquals(true to "mantida a aula mais nova: aula do 3º A", store.handle(lesson(2, 90, "7º B").encode(), home, address))
        assertEquals(true to "mantida a aula mais nova: aula do 3º A", store.handle(lesson(2, 200, ended = true).encode(), home, address))
        assertEquals(lesson(1, 100), store.current)
        assertEquals(true to "aula encerrada", store.handle(lesson(1, 200, ended = true).encode(), colleague, address))
        assertFalse(store.current.isOn)
    }

    @Test
    fun anotherLessonInTheSameSecondIsRefused() {
        val store = store()
        store.handle(lesson(1, 100).encode(), home, address)
        assertEquals(false to "outra aula foi salva no mesmo segundo; envie de novo", store.handle(lesson(2, 100, "7º B").encode(), home, address))
        // The same lesson again changes nothing.
        assertEquals(true to "aula do 3º A", store.handle(lesson(1, 100).encode(), home, address))
    }

    @Test
    fun onlyTheHomeStarts() {
        val store = store()
        assertEquals(true to "este PC guarda as pastas", store.claimHome(ClassHomeClaim("PROF-01", 28903, false, 5).encode(), home, address))
        assertEquals(
            false to "As pastas das turmas ficam no PC PROF-01",
            store.handle(lesson(1, 100).encode(), colleague, "192.168.0.11"),
        )
        assertFalse(store.current.isOn)
        assertEquals(true to "aula do 3º A", store.handle(lesson(1, 100).encode(), home, address))
        // A take-over waits for the lesson's end.
        assertEquals(
            false to "Encerre a aula antes de passar as pastas.",
            store.claimHome(ClassHomeClaim("PROF-02", 28903, true, 6).encode(), colleague, "192.168.0.11"),
        )
        store.handle(lesson(1, 101, ended = true).encode(), home, address)
        assertEquals(
            true to "as pastas passaram para este PC",
            store.claimHome(ClassHomeClaim("PROF-02", 28903, true, 6).encode(), colleague, "192.168.0.11"),
        )
        assertEquals("PROF-02", store.home.current?.name)
        assertEquals("192.168.0.11", store().home.current?.address)
    }

    @Test
    fun anOlderTakeOverOrABadClaimIsRefused() {
        val store = store()
        store.claimHome(ClassHomeClaim("PROF-01", 28903, false, 5).encode(), home, address)
        assertEquals(
            false to "As pastas das turmas ficam no PC PROF-01",
            store.claimHome(ClassHomeClaim("PROF-02", 28903, true, 5).encode(), colleague, address),
        )
        assertEquals(false to "pedido inválido", store.claimHome(byteArrayOf(9), home, address))
        assertEquals(false to "pedido inválido", store.claimHome(ClassHomeClaim("PROF-01", 28903, false, 5).encode(), home, "professor.local"))
        assertEquals(
            false to "pedido inválido: data da troca além do limite",
            store.claimHome(ClassHomeClaim("PROF-02", 28903, true, Long.MAX_VALUE).encode(), colleague, address),
        )
        // The home renamed keeps its generation and takes its new name.
        assertEquals(true to "este PC guarda as pastas", store.claimHome(ClassHomeClaim("PROF-NOVO", 28903, false, 5).encode(), home, address))
        assertEquals(ClassHome(home, "PROF-NOVO", address, 28903, 5), store.home.current)
    }

    @Test
    fun aLessonFromAnEarlierDayEndsByItself() {
        val store = store()
        store.handle(lesson(1, 100).encode(), home, address)
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
        assertEquals(true to "mantida a aula mais nova: aula encerrada", store.handle(lesson(1, 100).encode(), home, address))
    }

    @Test
    fun aBadTurmaOrPayloadIsRefused() {
        val store = store()
        assertEquals(false to "nome de turma inválido", store.handle(lesson(1, 100, "../fora").encode(), home, address))
        assertEquals(false to "nome de turma inválido", store.handle(lesson(1, 100, "\${x}").encode(), home, address))
        assertEquals(false to "pedido inválido", store.handle(byteArrayOf(1, 2), home, address))
        assertNull(store.home.current)
    }

    @Test
    fun aFolderThatCantBeMadeIsSaid() {
        problem = "sem acesso a todos os arquivos neste celular"
        assertEquals(
            true to "aula do 3º A; não foi possível preparar a pasta agora: sem acesso a todos os arquivos neste celular",
            store().handle(lesson(1, 100).encode(), home, address),
        )
    }

    @Test
    fun aCorruptFileMeansNoLesson() {
        File(dir, "class-lesson.bin").writeBytes(byteArrayOf(7, 7, 7))
        assertFalse(store().current.isOn)
        assertTrue(File(dir, "class-lesson.bin.corrupt").exists())
    }
}
