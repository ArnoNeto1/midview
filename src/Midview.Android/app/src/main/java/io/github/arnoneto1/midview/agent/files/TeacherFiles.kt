package io.github.arnoneto1.midview.agent.files

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.provider.DocumentsContract
import android.util.Log
import io.github.arnoneto1.midview.agent.net.FileHandler
import io.github.arnoneto1.midview.agent.protocol.FileTransfer

// The teacher's files on this phone: the FileHandler the commands port uses,
// with Download/Midview (DownloadFolder), the student's notice (a
// notification that opens the folder, as the PC's notice tells where the
// file went) and "Depois de enviar" (the folder or the file opened). Nothing
// opens over the teacher's lock screen: an app started there would break
// its lock task.
object TeacherFiles {
    fun handler(context: Context, isLocked: () -> Boolean): FileHandler {
        val app = context.applicationContext
        val folder = DownloadFolder(app)
        val notifications = app.getSystemService(NotificationManager::class.java)
        notifications.createNotificationChannel(
            NotificationChannel(CHANNEL, "Arquivos do professor", NotificationManager.IMPORTANCE_DEFAULT).apply {
                description = "Arquivos que o professor enviou ou recolheu"
                setShowBadge(false)
                setSound(null, null)
            },
        )
        return FileHandler(
            folder,
            notify = { text -> notify(app, notifications, text) },
            after = { afterSend, savedName -> open(app, folder, isLocked(), afterSend, savedName) },
        )
    }

    // Download/Midview in the Files app (DocumentsUI, which every Android has).
    fun folderIntent(): Intent =
        Intent(Intent.ACTION_VIEW)
            .setDataAndType(
                DocumentsContract.buildDocumentUri(EXTERNAL_STORAGE, "primary:Download/${DownloadFolder.FOLDER}"),
                DocumentsContract.Document.MIME_TYPE_DIR,
            )
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)

    private fun open(app: Context, folder: DownloadFolder, locked: Boolean, afterSend: Int, savedName: String): String? {
        if (afterSend == FileTransfer.AFTER_NOTHING) return null
        if (locked) return NOT_OPENED_LOCKED
        return try {
            if (afterSend == FileTransfer.AFTER_OPEN_FOLDER) {
                app.startActivity(folderIntent())
                "pasta aberta no aluno"
            } else {
                val (uri, type) = folder.find(savedName) ?: return "não abriu: o arquivo não foi encontrado"
                app.startActivity(
                    Intent(Intent.ACTION_VIEW)
                        .setDataAndType(uri, type)
                        .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_GRANT_READ_URI_PERMISSION),
                )
                "aberto no aluno"
            }
        } catch (e: ActivityNotFoundException) {
            NO_APP
        } catch (e: RuntimeException) {
            Log.w(TAG, "Opening for the student failed", e)
            "não abriu: ${e.message ?: e.javaClass.simpleName}"
        }
    }

    private fun notify(app: Context, notifications: NotificationManager, text: String) {
        val open = PendingIntent.getActivity(
            app, NOTIFICATION, folderIntent(), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        try {
            notifications.notify(
                NOTIFICATION,
                Notification.Builder(app, CHANNEL)
                    .setSmallIcon(android.R.drawable.stat_sys_download_done)
                    .setContentTitle("Arquivos do professor")
                    .setContentText(text)
                    .setStyle(Notification.BigTextStyle().bigText(text))
                    .setAutoCancel(true)
                    .setContentIntent(open)
                    .build(),
            )
        } catch (e: RuntimeException) {
            Log.w(TAG, "The files notification did not show", e)
        }
    }

    const val NOT_OPENED_LOCKED = "a tela está bloqueada: não foi aberto"
    const val NO_APP = "não abriu: nenhum app do celular abre este tipo de arquivo"

    private const val EXTERNAL_STORAGE = "com.android.externalstorage.documents"
    private const val TAG = "Midview"
    private const val CHANNEL = "teacher-files"

    // AgentService has 1 and 2, AndroidCloseNotice 3, TeacherTalk 4 and 1000 on.
    private const val NOTIFICATION = 5
}
