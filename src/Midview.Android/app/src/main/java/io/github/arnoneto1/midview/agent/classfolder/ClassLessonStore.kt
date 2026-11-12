package io.github.arnoneto1.midview.agent.classfolder

import io.github.arnoneto1.midview.agent.files.FileNames
import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.ClassHome
import io.github.arnoneto1.midview.agent.protocol.ClassHomeClaim
import io.github.arnoneto1.midview.agent.protocol.ClassLesson
import java.io.File
import java.io.IOException
import java.time.LocalDate
import java.util.concurrent.TimeUnit
import java.util.concurrent.locks.ReentrantLock

// The phone's side of the teacher's lesson (SetClassLesson, GetClassLesson,
// ClaimClassHome, GetClassHome), as the PC Agent's ClassLessonHandler
// (src/Midview.Agent/ClassLessonHandler.cs) with its rules: the newest
// savedAt wins; an end ends only the lesson it is of (or none); only the
// class folders' home (ClassHomeStore) starts a lesson or changes the
// class; a lesson that started on an earlier day ends by itself, keeping
// the files. Kept in the app's storage (the lesson's payload), so it holds
// after a restart.
//
// On a PC the Agent makes the student's Desktop folder and points Chrome at
// it; here prepare makes Download/Midview/Pasta da turma – <turma> (and
// says what it couldn't do, for the teacher), and changed asks the class
// folder to follow at once (ClassFolderFollow) instead of at its next look:
// it only asks (it is called with the lesson's lock held).
class ClassLessonStore(
    private val file: File,
    val home: ClassHomeStore,
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

    fun handle(payload: ByteArray, senderThumbprint: String, senderAddress: String): Pair<Boolean, String> {
        val lesson = ClassLesson.decode(payload) ?: return false to INVALID
        if (lesson.isOn && !isFolderName(lesson.turma)) return false to "nome de turma inválido"
        return locked { apply(lesson, senderThumbprint, senderAddress) }
    }

    fun claimHome(payload: ByteArray, senderThumbprint: String, senderAddress: String): Pair<Boolean, String> {
        val claim = ClassHomeClaim.decode(payload) ?: return false to INVALID
        return locked { home.claim(senderThumbprint, senderAddress, claim, lessonOn = current.isOn) }
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

    private fun apply(lesson: ClassLesson, senderThumbprint: String, senderAddress: String): Pair<Boolean, String> {
        if (lesson.isOn) home.refusalToStart(senderThumbprint, senderAddress)?.let { return false to it }
        val held = current
        val homesStart = homesStartOverAnotherEnd(lesson, held, senderThumbprint)
        if ((lesson.savedAt < held.savedAt && !homesStart) || (!lesson.isOn && held.isOn && lesson.lessonId != held.lessonId)) {
            return true to "${ClassLesson.KEPT_NEWER_TEXT}: ${describe(held)}"
        }
        if (lesson.savedAt == held.savedAt && lesson != held && !homesStart) {
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

    // A start from the home taken over an end of another lesson saved in the
    // same second or the one before (ClassLessonHandler.HomesStartOverAnotherEnd).
    private fun homesStartOverAnotherEnd(lesson: ClassLesson, held: ClassLesson, senderThumbprint: String): Boolean =
        lesson.isOn && !held.isOn && lesson.lessonId != held.lessonId &&
            (lesson.savedAt == held.savedAt || (held.savedAt > Long.MIN_VALUE && lesson.savedAt == held.savedAt - 1)) &&
            home.current?.thumbprint?.equals(senderThumbprint, ignoreCase = true) == true

    private fun locked(work: () -> Pair<Boolean, String>): Pair<Boolean, String> {
        if (!lock.tryLock(lockWaitMillis, TimeUnit.MILLISECONDS)) return false to ClassHome.BUSY_TEXT
        try {
            return work()
        } finally {
            lock.unlock()
        }
    }

    private fun save(lesson: ClassLesson): Boolean = try {
        file.absoluteFile.parentFile?.mkdirs()
        writeAtomically(file, lesson.encode())
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
        return ClassLesson.decode(bytes) ?: run {
            log("The class lesson file is corrupt, following none; kept aside as .corrupt", null)
            file.renameTo(File(file.path + ".corrupt"))
            ClassLesson.none(0)
        }
    }

    companion object {
        const val INVALID = "pedido inválido"

        // ClassHomeClaim.MaxLockWait: well under the Master's read timeout.
        const val LOCK_WAIT_MILLIS = 10_000L

        fun describe(lesson: ClassLesson) = if (lesson.isOn) "aula do ${lesson.turma}" else "aula encerrada"

        // A plain folder name, as the Master's class list makes it.
        fun isFolderName(turma: String) =
            turma.isNotEmpty() && turma.length <= ClassLesson.MAX_TURMA_LENGTH && FileNames.safe(turma) == turma && "\${" !in turma
    }
}
