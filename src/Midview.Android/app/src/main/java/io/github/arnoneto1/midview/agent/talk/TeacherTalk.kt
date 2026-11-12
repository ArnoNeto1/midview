package io.github.arnoneto1.midview.agent.talk

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.os.Handler
import android.os.Looper
import android.util.Log
import java.util.concurrent.Executors

// The teacher's "Mensagem" and "Bate-papo" on this phone: the messages
// (TeacherMessages) and conversations (TeacherChats) the agent keeps, their
// screens (TeacherMessageActivity, ChatActivity) and their notifications,
// which bring a screen back after the student left it. Started once by the
// agent (AgentService); the screens find it here.
object TeacherTalk {
    @Volatile var messages: TeacherMessages? = null
        private set

    @Volatile var chats: TeacherChats? = null
        private set

    private val main = Handler(Looper.getMainLooper())

    @Volatile private var context: Context? = null

    @Volatile private var isLocked: () -> Boolean = { false }

    // The teacher's lock screen is up: nothing of these may come over it.
    val locked: Boolean get() = isLocked()

    // A conversation that wanted the screen while a message was up.
    @Volatile private var behindMessage: Int? = null

    // The student's lines go out on this thread, in the order typed.
    private val sending = Executors.newSingleThreadExecutor { Thread(it, "Midview chat send").apply { isDaemon = true } }

    fun start(context: Context, isLocked: () -> Boolean) {
        val app = context.applicationContext
        this.context = app
        this.isLocked = isLocked
        val notifications = app.getSystemService(NotificationManager::class.java)
        notifications.createNotificationChannel(
            NotificationChannel(CHANNEL, "Mensagens do professor", NotificationManager.IMPORTANCE_LOW).apply {
                description = "Mensagens e conversas do professor"
                setShowBadge(false)
            },
        )
        messages = TeacherMessages(isLocked, AndroidMessageScreen(app, notifications))
        chats = TeacherChats(isLocked, AndroidChatScreen(app, notifications)) { task -> sending.execute(task) }
    }

    // The teacher's lock screen went. A moment later, so the padlock's lock
    // task is over before anything comes up. A waiting message comes up
    // last, over a conversation: it is the one to be read first.
    fun unlocked() {
        main.postDelayed({
            chats?.unlocked()
            messages?.unlocked()
        }, AFTER_UNLOCK_MILLIS)
    }

    // The student read the last message ("OK, ENTENDI"): a conversation that
    // wanted the screen meanwhile comes up, or waits for the teacher's lock
    // screen to go (TeacherChats.comeUp).
    fun messagesRead() {
        val id = behindMessage ?: return
        behindMessage = null
        chats?.comeUp(id)
    }

    // "Fechar programas" closes the teacher's messages too, as on a PC.
    fun closeMessages() {
        messages?.closeAll()
    }

    private class AndroidMessageScreen(private val app: Context, private val notifications: NotificationManager) : MessageScreen {
        override fun present() {
            postNotification()
            app.startActivity(TeacherMessageActivity.intent(app))
        }

        override fun dismiss() {
            notifications.cancel(MESSAGE_NOTIFICATION)
            main.post { TeacherMessageActivity.close() }
        }

        private fun postNotification() {
            val text = messages?.newest ?: return
            val open = PendingIntent.getActivity(
                app, MESSAGE_NOTIFICATION, TeacherMessageActivity.intent(app),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
            try {
                notifications.notify(
                    MESSAGE_NOTIFICATION,
                    Notification.Builder(app, CHANNEL)
                        .setSmallIcon(android.R.drawable.ic_dialog_email)
                        .setContentTitle(TeacherMessages.HEADER)
                        .setContentText(text)
                        .setStyle(Notification.BigTextStyle().bigText(text))
                        .setCategory(Notification.CATEGORY_MESSAGE)
                        .setOngoing(true)
                        .setContentIntent(open)
                        .build(),
                )
            } catch (e: RuntimeException) {
                Log.w(TAG, "The message notification did not show", e)
            }
        }
    }

    private class AndroidChatScreen(private val app: Context, private val notifications: NotificationManager) : ChatScreen {
        override fun changed(conversation: TeacherChats.Conversation) {
            main.post {
                if (!conversation.ended) notify(conversation)
                ChatActivity.refresh(conversation.id)
            }
        }

        // Not over a teacher's message still open (on a PC the message keeps
        // itself in front): the conversation comes up once it is read.
        override fun show(conversation: TeacherChats.Conversation) {
            if ((messages?.count ?: 0) > 0) {
                behindMessage = conversation.id
                return
            }
            app.startActivity(ChatActivity.intent(app, conversation.id))
        }

        override fun ended(conversation: TeacherChats.Conversation) {
            main.post {
                notifications.cancel(CHAT_NOTIFICATION + conversation.id)
                ChatActivity.ended(conversation.id, conversation.lines)
            }
        }

        private fun notify(conversation: TeacherChats.Conversation) {
            val open = PendingIntent.getActivity(
                app, CHAT_NOTIFICATION + conversation.id, ChatActivity.intent(app, conversation.id),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
            val text = conversation.lastFromTeacher?.let { "Professor: $it" } ?: OPEN_CHAT
            try {
                notifications.notify(
                    CHAT_NOTIFICATION + conversation.id,
                    Notification.Builder(app, CHANNEL)
                        .setSmallIcon(android.R.drawable.ic_dialog_email)
                        .setContentTitle(TeacherChats.TITLE)
                        .setContentText(text)
                        .setStyle(Notification.BigTextStyle().bigText(text))
                        .setCategory(Notification.CATEGORY_MESSAGE)
                        .setOngoing(true)
                        .setOnlyAlertOnce(true)
                        .setContentIntent(open)
                        .build(),
                )
            } catch (e: RuntimeException) {
                Log.w(TAG, "The chat notification did not show", e)
            }
        }
    }

    const val OPEN_CHAT = "Toque para abrir a conversa."

    private const val TAG = "Midview"
    // Quiet: the screen itself comes up over the app; a heads-up would only
    // cover its top. The notification stays in the shade to go back to it.
    private const val CHANNEL = "teacher-talk-quiet"

    // AgentService has 1 and 2, AndroidCloseNotice 3; a conversation's is
    // CHAT_NOTIFICATION plus its number.
    private const val MESSAGE_NOTIFICATION = 4
    private const val CHAT_NOTIFICATION = 1000

    private const val AFTER_UNLOCK_MILLIS = 700L
}
