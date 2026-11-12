package io.github.arnoneto1.midview.agent.classfolder

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.provider.DocumentsContract
import android.util.Log
import io.github.arnoneto1.midview.agent.files.DownloadFolder
import io.github.arnoneto1.midview.agent.protocol.ClassLesson

// The student's notice when a lesson starts, as a PC's corner notice ("Aula
// do 3º A — O que você baixar ou salvar na Pasta da turma..."): a quiet
// notification that opens the class folder in the Files app when tapped.
class ClassLessonNotice(context: Context) {
    private val app = context.applicationContext
    private val notifications = app.getSystemService(NotificationManager::class.java)

    init {
        notifications.createNotificationChannel(
            NotificationChannel(CHANNEL, "Pasta da turma", NotificationManager.IMPORTANCE_DEFAULT).apply {
                description = "A pasta da turma de cada aula"
                setShowBadge(false)
                setSound(null, null)
            },
        )
    }

    fun show(lesson: ClassLesson) {
        val text = ClassFolderFollow.lessonNotice(lesson)
        val open = PendingIntent.getActivity(
            app, NOTIFICATION, folderIntent(lesson), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        try {
            notifications.notify(
                NOTIFICATION,
                Notification.Builder(app, CHANNEL)
                    .setSmallIcon(android.R.drawable.ic_menu_agenda)
                    .setContentTitle("Aula do ${lesson.turma}")
                    .setContentText(text)
                    .setStyle(Notification.BigTextStyle().bigText(text))
                    .setAutoCancel(true)
                    .setContentIntent(open)
                    .build(),
            )
        } catch (e: RuntimeException) {
            Log.w(TAG, "The class lesson notice did not show", e)
        }
    }

    // The lesson is over: its notice would point at a folder that is gone.
    fun hide() {
        try {
            notifications.cancel(NOTIFICATION)
        } catch (e: RuntimeException) {
            Log.w(TAG, "The class lesson notice could not be taken away", e)
        }
    }

    companion object {
        private const val TAG = "Midview"
        private const val CHANNEL = "class-folder"
        private const val EXTERNAL_STORAGE = "com.android.externalstorage.documents"

        // AgentService has 1 and 2, AndroidCloseNotice 3, TeacherTalk 4 and
        // 1000 on, TeacherFiles 5.
        private const val NOTIFICATION = 6

        // The lesson's folder in the Files app (DocumentsUI).
        fun folderIntent(lesson: ClassLesson): Intent =
            Intent(Intent.ACTION_VIEW)
                .setDataAndType(
                    DocumentsContract.buildDocumentUri(EXTERNAL_STORAGE, "primary:Download/${DownloadFolder.FOLDER}/${lesson.folderName}"),
                    DocumentsContract.Document.MIME_TYPE_DIR,
                )
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
    }
}
