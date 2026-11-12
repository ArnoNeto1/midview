package io.github.arnoneto1.midview.agent.classfolder

import io.github.arnoneto1.midview.agent.identity.writeAtomically
import io.github.arnoneto1.midview.agent.protocol.ClassLesson
import io.github.arnoneto1.midview.agent.protocol.LessonId
import java.io.File
import java.io.IOException
import java.nio.file.Files
import java.util.concurrent.locks.ReentrantLock

// The phone following the lesson (ClassLessonStore), as a PC's Agent and
// Session do between them (src/Midview.Session/ClassFolder.Follow.cs and the
// Agent's protected folder):
// - a new lesson: its folder in Download/Midview, and the student's notice;
// - while it is on: the folder put back within one look if the student
//   removed it (a PC's folder can't be deleted; a phone's storage has no
//   such protection), its files coming back with the Master's next round;
// - a lesson over: the files a listing shows moved aside to
//   Download/Midview/.midview-turmas/<lesson> (the teacher's Master brings in
//   what it doesn't have, then clears them), and the folder removed with
//   whatever else is in it (a half-downloaded file, say). When the next
//   lesson is of the same turma the folder stays, as the new lesson's.
// What this phone last followed, and the lessons over whose folder isn't
// settled yet, are kept in the app's storage, so a restart repeats nothing.
// Never throws.
class ClassFolderFollow(
    private val folder: PhoneClassFolder,
    private val lesson: () -> ClassLesson,
    private val stateFile: File,
    private val canSee: () -> Boolean,
    // The student's notice of a new lesson; null when the lesson is over (its notice goes).
    private val notice: (ClassLesson?) -> Unit = {},
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    data class Followed(val lessonId: LessonId, val turma: String)

    private class State(var current: Followed, val pending: MutableList<Followed>)

    private val lock = ReentrantLock()
    private var state: State? = null
    private val problems = mutableSetOf<String>()

    fun follow() {
        if (!lock.tryLock()) return // the last look is still moving files; it sees the same
        try {
            followLesson()
        } catch (e: Exception) {
            log("Following the class lesson failed", e)
        } finally {
            lock.unlock()
        }
    }

    // The lesson's folder made now (ClassLessonStore.prepare): null, or what
    // stands in the way, for the teacher.
    fun prepare(lesson: ClassLesson): String? = if (!canSee()) PhoneClassFolder.NO_ACCESS else makeFolder(lesson)

    private fun followLesson() {
        val on = lesson().takeIf { it.isOn }
        val now = on?.let { Followed(it.lessonId, it.turma) } ?: NONE
        val state = state ?: read().also { state = it }
        var changed = false
        if (now != state.current) {
            if (state.current.lessonId != LessonId.EMPTY && state.current !in state.pending) state.pending += state.current
            try {
                notice(on)
            } catch (e: RuntimeException) {
                log("The class lesson's notice did not show", e)
            }
            state.current = now
            changed = true
        }
        try {
            if (!canSee()) return
            if (on != null) makeFolder(on)?.let { problem("The folder of ${on.turma} can't be made: $it") }
            val iterator = state.pending.iterator()
            while (iterator.hasNext()) {
                val finished = iterator.next()
                if (setAsideSafely(finished, on)) {
                    iterator.remove()
                    changed = true
                }
            }
            if (state.pending.isEmpty()) problems.clear()
        } finally {
            if (changed) save(state)
        }
    }

    private fun makeFolder(lesson: ClassLesson): String? {
        val target = folder.folderOf(lesson) ?: return "nome de turma inválido"
        synchronized(folder.gate) {
            if (PhoneClassFolder.isLink(target) || PhoneClassFolder.isLink(folder.root)) return PhoneClassFolder.LINKED
            if (target.isDirectory) return null
            if (target.exists()) return "há um arquivo com o nome da pasta em Download/Midview"
            return if (target.mkdirs()) null else "a pasta não pôde ser criada em Download/Midview"
        }
    }

    private fun setAsideSafely(finished: Followed, current: ClassLesson?): Boolean = try {
        synchronized(folder.gate) { setAside(finished, current) }
    } catch (e: Exception) {
        problem("Setting aside the files of ${finished.turma} failed: ${e.message}")
        false
    }

    // False when it has to be tried again.
    private fun setAside(finished: Followed, current: ClassLesson?): Boolean {
        val source = folder.folderOf(ClassLesson(0, finished.lessonId, finished.turma, 0, false))
        if (source == null || !source.isDirectory) return true
        val destination = folder.setAsideFolder(finished.lessonId) ?: return true
        if (PhoneClassFolder.isLink(source) || PhoneClassFolder.isLink(folder.setAsideRoot) || PhoneClassFolder.isLink(destination)) {
            problem("Leaving ${source.name} as it is: its files can't be set aside.")
            return true
        }
        val listed = folder.listedFiles(source)
        if (listed.isNotEmpty()) hideFromGallery()
        var moved = true
        for ((file, path) in listed) {
            if (!moveAside(file, path, finished.lessonId, destination)) moved = false
        }
        folder.removeEmptyFolders(destination)
        folder.tidySetAsideRoot()

        // The next lesson is of the same turma, in this very folder: it stays, as the new lesson's.
        if (current != null && folder.folderOf(current) == source) {
            folder.removeEmptySubfolders(source)
            return true
        }
        if (!moved) return false
        if (hasLink(source)) {
            problem("Leaving ${source.name}: there is a link in it, which isn't followed or removed.")
            return true
        }
        if (!deleteTree(source)) {
            problem("The folder ${source.name} can't be removed yet.")
            return false
        }
        return true
    }

    // Same path under the destination; a name taken there gets "(2)".
    private fun moveAside(file: File, path: String, lessonId: LessonId, destination: File): Boolean {
        var target = File(destination, path)
        var targetPath = path
        var copy = 2
        while (target.exists()) {
            val name = ClassFolderRules.copyName(file.name, copy++)
            targetPath = path.substringBeforeLast('/', "").let { if (it.isEmpty()) name else "$it/$name" }
            target = File(destination, targetPath)
        }
        return try {
            target.parentFile?.mkdirs()
            Files.move(file.toPath(), target.toPath())
            if (targetPath != path) folder.times.move(lessonId, path, lessonId, targetPath)
            true
        } catch (e: IOException) {
            problem("Could not set aside $path: ${e.message}")
            false
        }
    }

    // The set-aside files are the student's work, not photos to show in the gallery.
    private fun hideFromGallery() {
        val root = folder.setAsideRoot
        if (!root.isDirectory) root.mkdirs()
        val marker = File(root, PhoneClassFolder.NO_MEDIA)
        if (!marker.exists()) {
            try {
                marker.createNewFile()
            } catch (e: IOException) {
                // Only the gallery would show them.
            }
        }
    }

    private fun hasLink(directory: File): Boolean =
        (directory.listFiles() ?: emptyArray()).any { PhoneClassFolder.isLink(it) || (it.isDirectory && hasLink(it)) }

    // Never through a link (hasLink came first).
    private fun deleteTree(entry: File): Boolean {
        if (entry.isDirectory) entry.listFiles()?.forEach { deleteTree(it) }
        return entry.delete() || !entry.exists()
    }

    // Each problem logged once, until the lessons are settled.
    private fun problem(text: String) {
        if (problems.add(text)) log(text, null)
    }

    // One line per lesson: "current" or "pending", the lesson id (hex) and
    // the turma, tab-separated (a turma never holds a tab: FileNames.safe).
    private fun read(): State {
        val state = State(NONE, mutableListOf())
        if (!stateFile.exists()) return state
        try {
            for (line in stateFile.readLines(Charsets.UTF_8)) {
                val parts = line.split('\t', limit = 3)
                if (parts.size != 3) continue
                val id = LessonId.fromHex(parts[1]) ?: continue
                val followed = Followed(id, parts[2])
                when (parts[0]) {
                    "current" -> state.current = followed
                    "pending" -> if (!id.isEmpty && followed !in state.pending) state.pending += followed
                }
            }
        } catch (e: IOException) {
            log("The class folder's state could not be read", e)
        }
        return state
    }

    private fun save(state: State) {
        val lines = listOf("current\t${state.current.lessonId.hex}\t${state.current.turma}") +
            state.pending.map { "pending\t${it.lessonId.hex}\t${it.turma}" }
        try {
            stateFile.absoluteFile.parentFile?.mkdirs()
            writeAtomically(stateFile, lines.joinToString("\n", postfix = "\n").toByteArray(Charsets.UTF_8))
        } catch (e: IOException) {
            log("The class folder's state could not be saved", e)
        }
    }

    companion object {
        val NONE = Followed(LessonId.EMPTY, "")

        // What the student reads when a lesson starts. A phone's Chrome has no
        // download folder to point at the class folder (and the agent never
        // moves files outside Download/Midview), so the student is told how a
        // download gets there.
        fun lessonNotice(lesson: ClassLesson): String =
            "O que você salvar na ${lesson.folderName} (em Download/Midview, no app Arquivos) aparece em todos os " +
                "computadores e celulares da sala. Para dividir um download com a turma, mova-o para essa pasta no app Arquivos."
    }
}
