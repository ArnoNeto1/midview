package io.github.arnoneto1.midview.agent.files

import io.github.arnoneto1.midview.agent.protocol.FileTransfer
import java.io.Closeable
import java.io.File
import java.io.FileInputStream
import java.io.IOException
import java.io.InputStream
import java.nio.file.Files

// One file "Recolher arquivos" sends: its '/'-separated path inside the
// folder, its size, and how to read it.
class PhoneFile(val relativePath: String, val size: Long, val open: () -> InputStream)

// A file from the teacher on its way in: written as it arrives, hidden from
// the student until complete, and gone if it never completes (close).
interface IncomingFile : Closeable {
    fun write(bytes: ByteArray)

    // The name it was saved under, and a note for the teacher when it is not
    // simply that (it replaced the old one, or could not).
    fun complete(overwrite: Boolean): Pair<String, String?>
}

// The phone's Download/Midview: what the teacher's "Enviar arquivo" puts in
// it and "Recolher arquivos" brings back, and what "Apagar as pastas dos
// alunos" empties. The student sees it in the Files app. A PC has two
// folders on its Desktop for this ("Arquivos do professor" and "Entregar ao
// professor"); the phone has this one.
interface PhoneFolder {
    // False when Android lets the agent see only the files it saved itself
    // (no "acesso a todos os arquivos"): the rest of the folder is invisible to it.
    val seesEverything: Boolean

    // Null when a file of size bytes can be taken, otherwise why not.
    fun refusalFor(size: Long): String?

    // safeName: FileNames.safe already.
    @Throws(IOException::class)
    fun begin(safeName: String): IncomingFile

    // In path order, subfolders down to FileTransfer.MAX_DEPTH, at most
    // maxFiles + 1 (enough to tell there were more).
    @Throws(IOException::class)
    fun list(maxFiles: Int): List<PhoneFile>

    // Everything in it removed, subfolders whole; what could not go, named
    // with why. How many entries went.
    fun clear(): Pair<Int, List<String>>

    companion object {
        // As the student and the teacher read it.
        const val NAME = "Download/Midview"
    }
}

// The folder's files read straight from the storage, as the PC Session's
// DeliveryFolder walks its folder: files first, then subfolders, each in
// name order ignoring case; hidden entries (a name starting with '.', such
// as Android's ".pending-" and ".trashed-" files) and linked folders skipped.
object FolderWalk {
    fun list(root: File, maxFiles: Int): List<PhoneFile> {
        val files = mutableListOf<PhoneFile>()
        if (root.isDirectory) walk(root, "", 1, maxFiles, files)
        return files
    }

    private fun walk(directory: File, prefix: String, depth: Int, maxFiles: Int, files: MutableList<PhoneFile>) {
        val entries = (directory.listFiles() ?: return)
            .filter { !it.name.startsWith(".") }
            .sortedWith(compareBy(String.CASE_INSENSITIVE_ORDER) { it.name })
        for (file in entries.filter { it.isFile }) {
            if (files.size > maxFiles) return
            files += PhoneFile(prefix + file.name, file.length()) { FileInputStream(file) }
        }
        if (depth >= FileTransfer.MAX_DEPTH) return
        for (subdirectory in entries.filter { it.isDirectory && !Files.isSymbolicLink(it.toPath()) }) {
            if (files.size > maxFiles) return
            walk(subdirectory, prefix + subdirectory.name + "/", depth + 1, maxFiles, files)
        }
    }

    // Everything in root removed (Android has no Recycle Bin the student
    // can open): files, and subfolders whole. A linked folder is left where
    // it is (it could point anywhere), and so is a file still on its way in
    // (".pending-"). How many entries went, and what could not.
    fun clear(root: File): Pair<Int, List<String>> {
        val problems = mutableListOf<String>()
        val entries = (root.listFiles() ?: return 0 to problems)
            .filter { !it.name.startsWith(".pending-") }
            .sortedWith(compareBy(String.CASE_INSENSITIVE_ORDER) { it.name })
        var removed = 0
        for (entry in entries) {
            if (Files.isSymbolicLink(entry.toPath())) {
                problems += "${entry.name} (pasta vinculada, não removida)"
                continue
            }
            if (deleteTree(entry)) removed++ else problems += "${entry.name} (não pôde ser apagado)"
        }
        return removed to problems
    }

    // A link inside is removed itself, never what it points to.
    private fun deleteTree(entry: File): Boolean {
        if (entry.isDirectory && !Files.isSymbolicLink(entry.toPath())) {
            entry.listFiles()?.forEach { deleteTree(it) }
        }
        return entry.delete()
    }
}
