package io.github.arnoneto1.midview.agent.classfolder

import io.github.arnoneto1.midview.agent.files.DiskReserve
import io.github.arnoneto1.midview.agent.files.FileNames
import io.github.arnoneto1.midview.agent.protocol.ClassFolderEntry
import io.github.arnoneto1.midview.agent.protocol.ClassLesson
import io.github.arnoneto1.midview.agent.protocol.ClassPushHeader
import io.github.arnoneto1.midview.agent.protocol.LessonId
import java.io.Closeable
import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.nio.file.FileAlreadyExistsException
import java.nio.file.Files
import java.nio.file.StandardCopyOption

// A refusal the teacher reads as it is ("aula diferente", "mudou neste celular").
class ClassFolderRefusal(message: String) : Exception(message)

// The class folder ("Pasta da turma") on a phone, as a PC's Session works it
// for the teacher's Master (src/Midview.Session/ClassFolder.cs): while a
// lesson is on, Download/Midview/Pasta da turma – <turma>, which the student
// opens in the Files app; and, for lessons that are over, their files set
// aside in Download/Midview/.midview-turmas/<lesson> until the teacher's
// Master has them. Nothing outside Download/Midview is ever touched.
//
// Every path the Master sends is untrusted: it goes through
// ClassFolderRules.safeRelativePath, is joined to the lesson's folder, and
// never passes through a link. A phone has no Recycle Bin the student can
// open: what the Master deletes here goes for good (the copy that counts is
// on the teacher's PC). Reading other apps' files there (a document the
// student saved) needs "acesso a todos os arquivos", which the cable setup
// grants; without it (canSee false) the folder is not used at all and the
// teacher reads why.
//
// lesson: the lesson on, null for none (ClassLessonStore). times: the ticks
// the teacher's PC gave each file (ClassFileTimes). freeBytes: the storage's.
class PhoneClassFolder(
    val root: File,
    private val lesson: () -> ClassLesson?,
    val times: ClassFileTimes,
    private val canSee: () -> Boolean,
    private val freeBytes: () -> Long,
    val listingPartSize: Int = io.github.arnoneto1.midview.agent.protocol.ClassFolderWire.MAX_ENTRIES,
) {
    // One change at a time: a push going into place, a rename, a lesson's
    // files set aside (ClassFolderFollow) never cross.
    val gate = Any()

    val setAsideRoot: File get() = File(root, SET_ASIDE_FOLDER)

    data class Listing(val current: LessonId, val problem: String?, val entries: List<ClassFolderEntry>)

    // The lesson's folder in Download/Midview; null when its name (from the
    // teacher) is not one plain folder name.
    fun folderOf(lesson: ClassLesson): File? {
        val name = lesson.folderName
        return if (FileNames.safe(name) == name) File(root, name) else null
    }

    fun setAsideFolder(lessonId: LessonId): File? = if (lessonId.isEmpty) null else File(setAsideRoot, lessonId.hex)

    // The current lesson's folder's files (lesson = the current one) and
    // those set aside for other lessons, files before subfolders, in name
    // order. A problem (no access, no folder yet, a link) means no entries.
    fun list(): Listing = synchronized(gate) { listLocked() }

    private fun listLocked(): Listing {
        val on = lesson()
        val current = on?.lessonId ?: LessonId.EMPTY
        if (on != null && !canSee()) return Listing(current, NO_ACCESS, emptyList())
        val entries = mutableListOf<ClassFolderEntry>()
        val seen = mutableSetOf<Pair<LessonId, String>>()
        if (on != null) {
            val (folder, problem) = find(on, on.lessonId, onlyCurrent = true)
            if (folder == null) return Listing(current, problem, emptyList())
            walk(folder, on.lessonId, entries, seen)
        }
        if (canSee()) {
            for ((lessonId, directory) in setAsideFolders()) {
                if (lessonId != on?.lessonId) walk(directory, lessonId, entries, seen)
            }
            times.keepOnly(seen)
        }
        return Listing(current, null, entries)
    }

    // An existing file of that lesson's folder (the current one's, or a
    // set-aside one), or null.
    fun resolveExisting(lessonId: LessonId, path: String): File? {
        if (!canSee()) return null
        val (folder, _) = find(lesson(), lessonId, onlyCurrent = false)
        return folder?.let { existingFile(it, path) }
    }

    // Null when a file of that size fits and leaves the phone its spare
    // room (DiskReserve), otherwise why not.
    fun refusalFor(size: Long): String? = try {
        DiskReserve.refusal(freeBytes(), size)
    } catch (e: RuntimeException) {
        "a pasta Download/Midview não pôde ser usada"
    }

    // A file from the teacher's store going into the current lesson's folder,
    // in subfolders made as needed; nothing shows under its name until
    // Incoming.complete. Throws ClassFolderRefusal with the reason.
    fun beginPush(header: ClassPushHeader): Incoming {
        if (!canSee()) throw ClassFolderRefusal(NO_ACCESS)
        val (folder, reason) = find(lesson(), header.lessonId, onlyCurrent = true)
        folder ?: throw ClassFolderRefusal(reason)
        val segments = ClassFolderRules.safeRelativePath(header.path)
        val target = inside(folder, segments) ?: throw ClassFolderRefusal("caminho não permitido")
        if (target.isDirectory) throw ClassFolderRefusal("já existe uma pasta com esse nome")
        val path = segments.joinToString("/")
        if (!isAsExpected(target, header.lessonId, path, header.replacing)) throw ClassFolderRefusal(CHANGED)
        val directory = target.parentFile!!
        if (!directory.isDirectory && !directory.mkdirs()) throw IOException("a pasta ${directory.name} não pôde ser criada")
        return Incoming(target, File(directory, ClassFolderRules.newTempName()), header.lessonId, path, header)
    }

    inner class Incoming internal constructor(
        private val target: File,
        private val temp: File,
        private val lessonId: LessonId,
        private val path: String,
        private val header: ClassPushHeader,
    ) : Closeable {
        private val out = FileOutputStream(temp)
        private var completed = false

        fun write(bytes: ByteArray) = out.write(bytes)

        // Into place with the teacher's time, only onto what the Master
        // expected there (nothing, or the version its listing showed);
        // otherwise ClassFolderRefusal(CHANGED) and the student's file stays.
        fun complete() {
            out.fd.sync()
            out.close()
            // Before it shows under its name: the Master must never see its own copy with another time.
            // A time the storage refuses is no failure: the ticks are remembered below either way,
            // and the Master sees its own version (a failure would resend the file every round).
            try {
                temp.setLastModified(ClassFolderRules.millisOf(header.lastWriteTicks))
            } catch (e: IllegalArgumentException) {
                // Kept with the time it was written at.
            }
            synchronized(gate) {
                // The lesson ended meanwhile: its folder is being set aside.
                if (lesson()?.lessonId != lessonId) throw ClassFolderRefusal(OTHER_LESSON)
                if (!isAsExpected(target, lessonId, path, header.replacing)) throw ClassFolderRefusal(CHANGED)
                try {
                    if (header.replacing == null) {
                        Files.move(temp.toPath(), target.toPath())
                    } else {
                        Files.move(temp.toPath(), target.toPath(), StandardCopyOption.REPLACE_EXISTING)
                    }
                } catch (e: FileAlreadyExistsException) {
                    throw ClassFolderRefusal(CHANGED)
                }
                times.remember(lessonId, path, target.length(), target.lastModified(), header.lastWriteTicks)
                completed = true
            }
        }

        override fun close() {
            if (completed) return
            try {
                out.close()
            } catch (e: IOException) {
                // Thrown away anyway.
            }
            temp.delete()
        }
    }

    // What the teacher deleted on the Master (or a set-aside file it has):
    // gone for good, as the phone has no Recycle Bin.
    fun delete(lessonId: LessonId, path: String): Pair<Boolean, String> = synchronized(gate) {
        if (!canSee()) return false to NO_ACCESS
        val (folder, reason) = find(lesson(), lessonId, onlyCurrent = false)
        folder ?: return false to reason
        val file = existingFile(folder, path) ?: return false to NOT_FOUND
        if (!file.delete()) return false to "não pôde ser apagado"
        times.forget(lessonId, path)
        true to ""
    }

    // Within the lesson's folder; nothing changes if the new name is taken.
    fun rename(lessonId: LessonId, path: String, newPath: String): Pair<Boolean, String> = synchronized(gate) {
        if (!canSee()) return false to NO_ACCESS
        val (folder, reason) = find(lesson(), lessonId, onlyCurrent = false)
        folder ?: return false to reason
        val file = existingFile(folder, path) ?: return false to NOT_FOUND
        val segments = ClassFolderRules.safeRelativePath(newPath)
        val target = inside(folder, segments) ?: return false to "caminho não permitido"
        if (target.exists()) return false to "já existe um arquivo com esse nome"
        target.parentFile?.mkdirs()
        try {
            Files.move(file.toPath(), target.toPath())
        } catch (e: IOException) {
            return false to "não pôde ser renomeado"
        }
        times.move(lessonId, path, lessonId, segments.joinToString("/"))
        true to ""
    }

    // The Master has these set-aside files of that lesson: each goes, if it
    // is still that version; the lesson's folder goes once empty.
    fun clearSetAside(lessonId: LessonId, files: List<ClassFolderEntry>): Pair<Boolean, String> = synchronized(gate) {
        val folder = setAsideFolder(lessonId)
        if (folder == null || !folder.isDirectory) return true to ""
        if (isLink(folder)) return false to "pasta vinculada, não removida"
        val failures = mutableListOf<String>()
        for (entry in files) {
            val file = existingFile(folder, entry.path) ?: continue
            if (!hasVersion(file, lessonId, entry.path, entry.size, entry.lastWriteTicks)) continue
            if (file.delete()) times.forget(lessonId, entry.path) else failures += entry.path
        }
        removeEmptyFolders(folder)
        tidySetAsideRoot()
        if (failures.isEmpty()) true to "" else false to (failures[0] + if (failures.size > 1) " e mais ${failures.size - 1}" else "")
    }

    // The files a listing shows of a folder, with their paths ('/' between
    // segments): files first, then subfolders, each in name order ignoring
    // case; links and what ClassFolderRules leaves out skipped; down to
    // FileTransfer.MAX_DEPTH.
    fun listedFiles(folder: File): List<Pair<File, String>> {
        val listed = mutableListOf<Pair<File, String>>()
        collect(folder, "", 1, listed)
        return listed
    }

    private fun collect(directory: File, prefix: String, depth: Int, listed: MutableList<Pair<File, String>>) {
        val entries = (directory.listFiles() ?: return).sortedWith(compareBy(String.CASE_INSENSITIVE_ORDER) { it.name })
        for (file in entries) {
            if (!file.isFile || isLink(file)) continue
            val path = prefix + file.name
            if (ClassFolderRules.isListedFile(file.name, file.length()) && ClassFolderRules.fitsTheWire(path)) listed += file to path
        }
        if (!ClassFolderRules.canDescendFrom(depth)) return
        for (subdirectory in entries) {
            if (!subdirectory.isDirectory || isLink(subdirectory) || !ClassFolderRules.isAddressable(subdirectory.name)) continue
            if (subdirectory.name.startsWith(".pending-") || subdirectory.name.startsWith(ClassFolderRules.TEMP_PREFIX)) continue
            collect(subdirectory, prefix + subdirectory.name + "/", depth + 1, listed)
        }
    }

    private fun walk(folder: File, lessonId: LessonId, entries: MutableList<ClassFolderEntry>, seen: MutableSet<Pair<LessonId, String>>) {
        for ((file, path) in listedFiles(folder)) {
            val size = file.length()
            entries += ClassFolderEntry(lessonId, path, size, times.ticksOf(lessonId, path, size, file.lastModified()))
            seen += lessonId to path
        }
    }

    // Where that lesson's files are: the current lesson's folder, or (unless
    // onlyCurrent) its set-aside folder. Null with the reason otherwise.
    private fun find(current: ClassLesson?, lessonId: LessonId, onlyCurrent: Boolean): Pair<File?, String> {
        val folder = if (current != null && current.lessonId == lessonId) {
            folderOf(current)?.takeIf { it.isDirectory } ?: return null to NO_FOLDER
        } else {
            (if (onlyCurrent) null else setAsideFolder(lessonId))?.takeIf { it.isDirectory } ?: return null to OTHER_LESSON
        }
        return if (isLink(folder) || isLink(root)) null to LINKED else folder to ""
    }

    private fun existingFile(folder: File, path: String): File? =
        inside(folder, ClassFolderRules.safeRelativePath(path))?.takeIf { it.isFile }

    // folder plus the segments, or null through a link on the way.
    private fun inside(folder: File, segments: List<String>): File? {
        var file = folder
        for (segment in segments) {
            file = File(file, segment)
            if (isLink(file)) return null
        }
        val base = folder.absoluteFile.toPath().normalize()
        return file.takeIf { it.absoluteFile.toPath().normalize().startsWith(base) && it != folder }
    }

    private fun isAsExpected(file: File, lessonId: LessonId, path: String, replacing: Pair<Long, Long>?): Boolean =
        if (replacing == null) !file.exists() else hasVersion(file, lessonId, path, replacing.first, replacing.second)

    private fun hasVersion(file: File, lessonId: LessonId, path: String, size: Long, ticks: Long): Boolean =
        file.isFile && file.length() == size && times.ticksOf(lessonId, path, size, file.lastModified()) == ticks

    // The set-aside folders named by a lesson, in name order (no links).
    fun setAsideFolders(): List<Pair<LessonId, File>> =
        (setAsideRoot.takeIf { it.isDirectory && !isLink(it) }?.listFiles() ?: emptyArray())
            .filter { it.isDirectory && !isLink(it) }
            .mapNotNull { directory -> LessonId.fromHex(directory.name)?.takeIf { !it.isEmpty }?.let { it to directory } }
            .sortedBy { it.second.name }

    // The set-aside lessons whose teacher's Master never came for them do
    // not fill the phone: one set aside more than maxAgeMillis ago goes,
    // and while all of them take more than maxBytes, the oldest go first.
    // Their age is when they were set aside (the newest change of a folder
    // in them). Returns the lessons removed.
    fun pruneSetAside(
        nowMillis: Long,
        maxAgeMillis: Long = SET_ASIDE_MAX_AGE_MILLIS,
        maxBytes: Long = SET_ASIDE_MAX_BYTES,
    ): List<LessonId> = synchronized(gate) {
        val lessons = setAsideFolders().map { (id, dir) -> Triple(id, dir, newestFolderChange(dir)) }.sortedBy { it.third }
        var total = lessons.sumOf { sizeOf(it.second) }
        val removed = mutableListOf<LessonId>()
        for ((id, dir, setAsideAt) in lessons) {
            if (nowMillis - setAsideAt <= maxAgeMillis && total <= maxBytes) continue
            val size = sizeOf(dir)
            if (deleteTree(dir)) {
                total -= size
                removed += id
            }
        }
        if (removed.isNotEmpty()) tidySetAsideRoot()
        removed
    }

    private fun newestFolderChange(dir: File): Long =
        (dir.listFiles() ?: emptyArray()).filter { it.isDirectory && !isLink(it) }
            .maxOfOrNull { newestFolderChange(it) }?.let { maxOf(it, dir.lastModified()) } ?: dir.lastModified()

    private fun sizeOf(file: File): Long =
        if (isLink(file)) 0 else if (file.isDirectory) (file.listFiles() ?: emptyArray()).sumOf { sizeOf(it) } else file.length()

    // Never through a link: only what is really inside goes.
    private fun deleteTree(file: File): Boolean {
        if (file.isDirectory && !isLink(file)) (file.listFiles() ?: emptyArray()).forEach { deleteTree(it) }
        return file.delete() || !file.exists()
    }

    // Nothing set aside any more: the folder (and its .nomedia) goes too,
    // so a phone with no lesson leaves Download/Midview as it found it.
    fun tidySetAsideRoot() {
        val root = setAsideRoot
        if (!root.isDirectory || isLink(root)) return
        val left = root.listFiles() ?: return
        if (left.all { it.isFile && it.name == NO_MEDIA }) {
            left.forEach { it.delete() }
            root.delete()
        }
    }

    // Empty subfolders go, deepest first, and the folder itself if empty.
    fun removeEmptyFolders(folder: File) {
        removeEmptySubfolders(folder)
        if (folder.listFiles()?.isEmpty() == true) folder.delete()
    }

    fun removeEmptySubfolders(folder: File) {
        for (sub in folder.listFiles() ?: return) {
            if (sub.isDirectory && !isLink(sub)) {
                removeEmptySubfolders(sub)
                if (sub.listFiles()?.isEmpty() == true) sub.delete()
            }
        }
    }

    companion object {
        // Inside Download/Midview, hidden from the Files app, never listed or
        // collected ("Recolher arquivos") nor emptied by "Apagar as pastas dos alunos".
        const val SET_ASIDE_FOLDER = ".midview-turmas"

        // Keeps the gallery out of the set-aside files.
        const val NO_MEDIA = ".nomedia"

        // A lesson's files wait this long for the teacher's Master.
        const val SET_ASIDE_MAX_AGE_MILLIS = 30L * 24 * 60 * 60 * 1000

        // And all of them take no more than this.
        const val SET_ASIDE_MAX_BYTES = 2L * 1024 * 1024 * 1024

        const val NO_ACCESS = "sem acesso a todos os arquivos neste celular"
        const val NO_FOLDER = "sem pasta"
        const val OTHER_LESSON = "aula diferente"
        const val LINKED = "pasta vinculada"
        const val NOT_FOUND = "arquivo não encontrado"

        // The PC Session's "mudou neste PC", in a phone's words: the
        // Master only shows it, and plans again with the next listing.
        const val CHANGED = "mudou neste celular"

        fun isLink(file: File): Boolean = Files.isSymbolicLink(file.toPath())
    }
}
