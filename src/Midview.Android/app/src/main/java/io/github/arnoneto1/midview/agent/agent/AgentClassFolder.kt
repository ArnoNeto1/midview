package io.github.arnoneto1.midview.agent.agent

import android.content.Context
import android.os.Environment
import android.os.StatFs
import io.github.arnoneto1.midview.agent.classfolder.ClassFileTimes
import io.github.arnoneto1.midview.agent.classfolder.ClassFolderFollow
import io.github.arnoneto1.midview.agent.classfolder.ClassLessonNotice
import io.github.arnoneto1.midview.agent.classfolder.ClassLessonStore
import io.github.arnoneto1.midview.agent.classfolder.PhoneClassFolder
import io.github.arnoneto1.midview.agent.files.DownloadFolder
import java.io.File
import java.util.concurrent.Executors
import java.util.concurrent.RejectedExecutionException
import java.util.concurrent.TimeUnit

private const val CLASS_LESSON_FILE = "class-lesson.bin"
private const val CLASS_FOLLOW_FILE = "class-folder-follow.txt"
private const val CLASS_TIMES_FILE = "class-folder-times.txt"

// As often as a PC's Session follows the lesson; with no lesson, only
// to let go of old set-aside files (a lesson's start looks at once).
private const val CLASS_FOLDER_LOOK_SECONDS = 5L
private const val CLASS_FOLDER_IDLE_LOOK_SECONDS = 60L

// "Pasta da turma": the lesson and the class folders' home (a PC
// Agent's part, on the commands port) and the lesson's folder in
// Download/Midview (a PC Session's part, on the screen port),
// followed every few seconds and at once when the lesson changes.
// Its timer is ended with the agent (own).
internal class AgentClassFolder(
    context: Context,
    private val log: (String, Throwable?) -> Unit,
    own: (() -> Unit) -> Unit,
) {
    private val filesDir = context.filesDir
    private val downloads = DownloadFolder(context)

    lateinit var lessons: ClassLessonStore
        private set

    val folder = PhoneClassFolder(
        downloads.directory, { lessons.lessonOn }, ClassFileTimes(File(filesDir, CLASS_TIMES_FILE), log),
        canSee = { downloads.seesEverything },
        freeBytes = { StatFs(Environment.getExternalStorageDirectory().path).availableBytes },
    )
    private val lessonNotice = ClassLessonNotice(context)
    private val follow = ClassFolderFollow(
        folder, { lessons.current }, File(filesDir, CLASS_FOLLOW_FILE), canSee = { downloads.seesEverything },
        notice = { if (it == null) lessonNotice.hide() else lessonNotice.show(it) },
        remind = lessonNotice::showIfMissing,
        log = log,
    )
    private val classTimer = Executors.newSingleThreadScheduledExecutor { Thread(it, "Midview class folder").apply { isDaemon = true } }

    init {
        own { classTimer.shutdownNow() }
        lessons = ClassLessonStore(
            File(filesDir, CLASS_LESSON_FILE),
            prepare = follow::prepare,
            changed = {
                try {
                    classTimer.execute { follow.follow() }
                } catch (e: RejectedExecutionException) {
                    // The agent is stopping.
                }
            },
            log = log,
        )
    }

    fun startLooking() {
        classTimer.execute { look() }
    }

    // The class folder looked at every few seconds while a lesson is on
    // (or just ended), far less often otherwise.
    private fun look() {
        try {
            lessons.endIfFromAnEarlierDay()
            follow.follow()
        } catch (e: RuntimeException) {
            log("The class folder's look failed", e)
        }
        val next = if (follow.needsLooking()) CLASS_FOLDER_LOOK_SECONDS else CLASS_FOLDER_IDLE_LOOK_SECONDS
        try {
            classTimer.schedule(Runnable { look() }, next, TimeUnit.SECONDS)
        } catch (e: RejectedExecutionException) {
            // The agent is stopping.
        }
    }
}
