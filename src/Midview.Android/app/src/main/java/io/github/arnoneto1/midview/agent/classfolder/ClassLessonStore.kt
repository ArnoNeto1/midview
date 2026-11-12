package io.github.arnoneto1.midview.agent.classfolder

import io.github.arnoneto1.midview.agent.files.FileNames
import io.github.arnoneto1.midview.agent.identity.StoredText
import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.ClassLesson
import io.github.arnoneto1.midview.agent.protocol.LessonId
import java.io.File
import java.io.IOException
import java.time.LocalDate
import java.util.concurrent.TimeUnit
import java.util.concurrent.locks.ReentrantLock

// The phone's side of the teacher's lesson (SetClassLesson, GetClassLesson),
// as the PC Agent's ClassLessonHandler (src/Midview.Agent/ClassLessonHandler.cs)
// with its rules: the newest savedAt wins; an end ends only the lesson it is
// of (or none); a lesson that started on an earlier day ends by itself,
// keeping the files. Kept in the app's storage (in the phone's own format,
// encodeStored), so it holds after a restart.
//
// Unlike a PC Agent, a phone keeps no class folders' home ("PC das pastas":
// ClaimClassHome and GetClassHome get "Ainda não funciona em celular.",
// which the Master reads as an Agent from before two teacher PCs). It takes
// the newest lesson from any teacher it trusts: a school phone goes from room
// to room, and a home kept from one room would refuse the next room's
// lessons. The Master asks only its PCs which PC keeps the folders.
//
// On a PC the Agent makes the student's Desktop folder and points Chrome at
// it; here prepare makes Download/Midview/Pasta da turma – <turma> (and
// says what it couldn't do, for the teacher), and changed asks the class
// folder to follow at once (ClassFolderFollow) instead of at its next look:
// it only asks (it is called with the lesson's lock held).
class ClassLessonStore(
    private val file: File,
    private val today: () -> LocalDate = { LocalDate.now() },
    private val prepare: (ClassLesson) -> String? = { null },
    private val changed: () -> Unit = {},
    private val lockWaitMillis: Long = LOCK_WAIT_MILLIS,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    private val lock = ReentrantLock()

    @Volatile var current: ClassLesson = load()
        private set

    // The lesson on, or null.
    val lessonOn: ClassLesson? get() = current.takeIf { it.isOn }

    // From a teacher this phone trusts (the CommandHandler checked).
    fun handle(payload: ByteArray): Pair<Boolean, String> {
        val lesson = ClassLesson.decode(payload) ?: return false to INVALID
        if (lesson.isOn && !isFolderName(lesson.turma)) return false to "nome de turma inválido"
        return locked { apply(lesson) }
    }

    // Looked at every few seconds: a lesson from an earlier day ends, one
    // second newer (so the Master resending the old one doesn't start it again).
    fun endIfFromAnEarlierDay() {
        if (!lock.tryLock()) return
        val ended = try {
            val lesson = current
            if (!lesson.isOn || !lesson.startedBefore(today())) return
            val end = lesson.copy(savedAt = if (lesson.savedAt == Long.MAX_VALUE) Long.MAX_VALUE else lesson.savedAt + 1, ended = true)
            save(end).also { if (it) log("Ended the lesson of ${lesson.turma}, which started on an earlier day.", null) }
        } finally {
            lock.unlock()
        }
        if (ended) changed()
    }

    private fun apply(lesson: ClassLesson): Pair<Boolean, String> {
        val held = current
        if (lesson.savedAt < held.savedAt || (!lesson.isOn && held.isOn && lesson.lessonId != held.lessonId)) {
            return true to "${ClassLesson.KEPT_NEWER_TEXT}: ${describe(held)}"
        }
        if (lesson.savedAt == held.savedAt && lesson != held) {
            return false to "outra aula foi salva no mesmo segundo; envie de novo"
        }
        if (lesson != held && !save(lesson)) return false to "não foi possível gravar a aula no celular"
        val problem = if (current.isOn) {
            try {
                prepare(current)
            } catch (e: RuntimeException) {
                e.message ?: e.javaClass.simpleName
            }
        } else {
            null
        }
        changed()
        val detail = describe(current)
        return true to if (problem == null) detail else "$detail; não foi possível preparar a pasta agora: $problem"
    }

    private fun locked(work: () -> Pair<Boolean, String>): Pair<Boolean, String> {
        if (!lock.tryLock(lockWaitMillis, TimeUnit.MILLISECONDS)) return false to BUSY_TEXT
        try {
            return work()
        } finally {
            lock.unlock()
        }
    }

    private fun save(lesson: ClassLesson): Boolean = try {
        file.absoluteFile.parentFile?.mkdirs()
        writeAtomically(file, encodeStored(lesson))
        current = lesson
        true
    } catch (e: IOException) {
        log("Couldn't save the class lesson", e)
        false
    }

    // Unreadable means no lesson (the side the teacher can always fix); a
    // corrupt file is set aside.
    private fun load(): ClassLesson {
        if (!file.exists()) return ClassLesson.none(0)
        val bytes = try {
            file.readBytes()
        } catch (e: IOException) {
            log("Couldn't read the class lesson, following none", e)
            return ClassLesson.none(0)
        }
        return decodeStored(bytes) ?: run {
            log("The class lesson file is corrupt, following none; kept aside as .corrupt", null)
            file.renameTo(File(file.path + ".corrupt"))
            ClassLesson.none(0)
        }
    }

    companion object {
        const val INVALID = "pedido inválido"

        // ClassHomeClaim.BusyText: the Master asks again in a moment.
        const val BUSY_TEXT = "ocupado agora, tente de novo em instantes"

        // ClassHomeClaim.MaxLockWait: well under the Master's read timeout.
        const val LOCK_WAIT_MILLIS = 10_000L

        private const val KIND = "class-lesson"

        // StoredText "class-lesson": savedAt, startedAt (Unix seconds),
        // ended (0|1), lessonId (hex) and turma.
        fun encodeStored(lesson: ClassLesson): ByteArray =
            StoredText.encode(
                KIND,
                listOf(
                    "savedAt" to lesson.savedAt.toString(),
                    "startedAt" to lesson.startedAt.toString(),
                    "ended" to if (lesson.ended) "1" else "0",
                    "lessonId" to lesson.lessonId.hex,
                    "turma" to lesson.turma,
                ),
            )

        // Null for a file that is neither the phone's format nor the
        // network's of 0.0.70 and before. A lesson id it cannot read is no
        // lesson (its folder could not be found anyway).
        fun decodeStored(bytes: ByteArray): ClassLesson? {
            val stored = StoredText.decode(KIND, bytes) ?: return ClassLesson.decode(bytes)
            val savedAt = stored.first("savedAt")?.toLongOrNull() ?: 0L
            val id = stored.first("lessonId")?.let(LessonId::fromHex) ?: return ClassLesson.none(savedAt)
            return ClassLesson(
                savedAt, id, stored.first("turma").orEmpty(),
                stored.first("startedAt")?.toLongOrNull() ?: 0L,
                ended = stored.first("ended") != "0",
            )
        }

        fun describe(lesson: ClassLesson) = if (lesson.isOn) "aula do ${lesson.turma}" else "aula encerrada"

        // A plain folder name, as the Master's class list makes it.
        fun isFolderName(turma: String) =
            turma.isNotEmpty() && turma.length <= ClassLesson.MAX_TURMA_LENGTH && FileNames.safe(turma) == turma && "\${" !in turma
    }
}
