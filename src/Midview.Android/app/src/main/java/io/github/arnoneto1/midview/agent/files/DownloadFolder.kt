package io.github.arnoneto1.midview.agent.files

import android.Manifest
import android.content.ContentUris
import android.content.ContentValues
import android.content.Context
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.os.StatFs
import android.provider.MediaStore
import android.webkit.MimeTypeMap
import java.io.File
import java.io.IOException
import java.io.OutputStream

// Download/Midview on the phone's shared storage (PhoneFolder).
//
// The teacher's files go in through MediaStore.Downloads: no permission is
// needed for the app's own files, Android indexes them at once (the Files
// app shows them), and a file stays pending (hidden, IS_PENDING) until all
// of it has arrived.
//
// Reading what other apps saved there (a Chrome download, a document the
// student saved) needs more since Android 11: MediaStore shows an app only
// its own files in Download, and Android 13 has no read permission for
// documents at all. So the agent holds "acesso a todos os arquivos"
// (MANAGE_EXTERNAL_STORAGE), which a Device Owner cannot grant itself: the
// cable setup does (appops), as it turns on the screen reader. With it the
// folder is read straight from the storage (FolderWalk); without it, only
// the agent's own files are seen (seesEverything false), and the teacher is
// told so. On Android 10 the same is READ/WRITE_EXTERNAL_STORAGE with the
// legacy storage the manifest asks for.
class DownloadFolder(private val app: Context) : PhoneFolder {
    private val resolver = app.contentResolver

    @Suppress("DEPRECATION")
    val directory: File
        get() = File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS), FOLDER)

    override val seesEverything: Boolean
        get() = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            Environment.isExternalStorageManager()
        } else {
            granted(Manifest.permission.READ_EXTERNAL_STORAGE) && granted(Manifest.permission.WRITE_EXTERNAL_STORAGE)
        }

    override fun refusalFor(size: Long): String? = try {
        if (StatFs(Environment.getExternalStorageDirectory().path).availableBytes < size) {
            "não há espaço livre suficiente no celular"
        } else {
            null
        }
    } catch (e: IllegalArgumentException) {
        "a pasta ${PhoneFolder.NAME} não pôde ser usada"
    }

    override fun begin(safeName: String): IncomingFile {
        val values = ContentValues().apply {
            put(MediaStore.MediaColumns.DISPLAY_NAME, safeName)
            put(MediaStore.MediaColumns.RELATIVE_PATH, RELATIVE_PATH)
            put(MediaStore.MediaColumns.MIME_TYPE, mimeOf(safeName))
            put(MediaStore.MediaColumns.IS_PENDING, 1)
        }
        val uri = try {
            resolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values)
        } catch (e: RuntimeException) {
            throw IOException("MediaStore refused the file: ${e.message}", e)
        } ?: throw IOException("MediaStore refused the file")
        val out = try {
            resolver.openOutputStream(uri, "w")
        } catch (e: Exception) {
            resolver.delete(uri, null, null)
            throw IOException("The file could not be opened: ${e.message}", e)
        } ?: run {
            resolver.delete(uri, null, null)
            throw IOException("The file could not be opened")
        }
        return Incoming(uri, safeName, out)
    }

    override fun list(maxFiles: Int): List<PhoneFile> =
        if (seesEverything) FolderWalk.list(directory, maxFiles) else ownFiles().take(maxFiles + 1)

    override fun clear(): Pair<Int, List<String>> {
        if (seesEverything) return FolderWalk.clear(directory)
        var removed = 0
        val problems = mutableListOf<String>()
        for (file in ownRows()) {
            try {
                if (resolver.delete(file.uri, null, null) > 0) removed++ else problems += "${file.path} (não pôde ser apagado)"
            } catch (e: RuntimeException) {
                problems += "${file.path} (não pôde ser apagado)"
            }
        }
        return removed to problems
    }

    // The saved file, for "Abrir ou executar o arquivo": its address to hand
    // to another app, and its type. Null if it is not there any more.
    fun find(name: String): Pair<Uri, String>? =
        ownRows().firstOrNull { it.path == name }?.let { it.uri to mimeOf(name) }

    private inner class Incoming(private val uri: Uri, private val safeName: String, private val out: OutputStream) : IncomingFile {
        private var completed = false

        override fun write(bytes: ByteArray) = out.write(bytes)

        // "notas.pdf" becomes "notas (2).pdf" unless the teacher chose to
        // replace it: the old one goes once the new one is whole, and only
        // then does the new one show, under its name.
        override fun complete(overwrite: Boolean): Pair<String, String?> {
            out.close()
            val names = visibleNames()
            var note: String? = null
            val target = when {
                safeName.lowercase() !in names -> safeName
                overwrite && removeOld(safeName) -> {
                    note = "substituiu o anterior"
                    safeName
                }
                else -> {
                    if (overwrite) note = "o anterior não pôde ser substituído"
                    FileNames.unique(safeName) { it.lowercase() in names }
                }
            }
            val published = ContentValues().apply {
                put(MediaStore.MediaColumns.DISPLAY_NAME, target)
                put(MediaStore.MediaColumns.IS_PENDING, 0)
            }
            try {
                resolver.update(uri, published, null, null)
            } catch (e: RuntimeException) {
                // The name taken after all (by a file the agent cannot see):
                // published under the name Android chose.
                resolver.update(uri, ContentValues().apply { put(MediaStore.MediaColumns.IS_PENDING, 0) }, null, null)
            }
            completed = true
            return (savedName(uri) ?: target) to note
        }

        override fun close() {
            if (completed) return
            try {
                out.close()
            } catch (e: IOException) {
                // Thrown away anyway.
            }
            try {
                resolver.delete(uri, null, null)
            } catch (e: RuntimeException) {
                // Left pending: Android deletes a pending file a week later.
            }
        }
    }

    // The names in the folder itself (not its subfolders), in lower case.
    private fun visibleNames(): Set<String> =
        if (seesEverything) {
            directory.list()?.map { it.lowercase() }?.toSet() ?: emptySet()
        } else {
            ownRows().filter { '/' !in it.path }.map { it.path.lowercase() }.toSet()
        }

    private fun removeOld(name: String): Boolean =
        if (seesEverything) {
            File(directory, name).let { it.isFile && it.delete() }
        } else {
            ownRows().firstOrNull { it.path.equals(name, ignoreCase = true) }?.let {
                try {
                    resolver.delete(it.uri, null, null) > 0
                } catch (e: RuntimeException) {
                    false
                }
            } ?: false
        }

    private fun savedName(uri: Uri): String? = try {
        resolver.query(uri, arrayOf(MediaStore.MediaColumns.DISPLAY_NAME), null, null, null)?.use {
            if (it.moveToFirst()) it.getString(0) else null
        }
    } catch (e: RuntimeException) {
        null
    }

    private class Row(val uri: Uri, val path: String, val size: Long)

    // What MediaStore shows the agent of the folder: its own published files
    // (and, with all-files access, everyone's), the path inside the folder.
    private fun ownRows(): List<Row> {
        val rows = mutableListOf<Row>()
        val collection = MediaStore.Downloads.EXTERNAL_CONTENT_URI
        val projection = arrayOf(
            MediaStore.MediaColumns._ID, MediaStore.MediaColumns.RELATIVE_PATH,
            MediaStore.MediaColumns.DISPLAY_NAME, MediaStore.MediaColumns.SIZE,
        )
        try {
            resolver.query(
                collection, projection, "${MediaStore.MediaColumns.RELATIVE_PATH} LIKE ?", arrayOf("$RELATIVE_PATH%"), null,
            )?.use {
                while (it.moveToNext()) {
                    val inside = it.getString(1).orEmpty().removePrefix(RELATIVE_PATH)
                    val name = it.getString(2) ?: continue
                    if (name.startsWith(".")) continue
                    val path = inside + name
                    if (path.split('/').size > io.github.arnoneto1.midview.agent.protocol.FileTransfer.MAX_DEPTH) continue
                    rows += Row(ContentUris.withAppendedId(collection, it.getLong(0)), path, it.getLong(3))
                }
            }
        } catch (e: RuntimeException) {
            throw IOException("MediaStore could not list the folder: ${e.message}", e)
        }
        return rows.sortedWith(compareBy(String.CASE_INSENSITIVE_ORDER) { it.path })
    }

    private fun ownFiles(): List<PhoneFile> = ownRows().map { row ->
        PhoneFile(row.path, row.size) {
            resolver.openInputStream(row.uri) ?: throw IOException("${row.path} could not be opened")
        }
    }

    private fun granted(permission: String) = app.checkSelfPermission(permission) == PackageManager.PERMISSION_GRANTED

    companion object {
        const val FOLDER = "Midview"

        // MediaStore's way of naming the folder (with the trailing slash).
        const val RELATIVE_PATH = "Download/Midview/"

        // By the extension: a PDF opens in a PDF reader. Anything unknown
        // is plain bytes, which MediaStore keeps under the name given.
        fun mimeOf(name: String): String {
            val extension = FileNames.extensionOf(name).removePrefix(".").lowercase()
            return MimeTypeMap.getSingleton().getMimeTypeFromExtension(extension) ?: "application/octet-stream"
        }
    }
}
