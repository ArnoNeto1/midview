package io.github.arnoneto1.midview.agent.talk

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Bundle
import android.text.method.ScrollingMovementMethod
import android.util.TypedValue
import android.view.Gravity
import android.view.ViewGroup
import android.view.WindowManager
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView

// The teacher's message over the student's app, as the PC's
// TeacherMessageForm: dark red, the message in big bold white letters (the
// biggest size at which it fits; a long one scrolls), and no way out but "OK,
// ENTENDI" -- back does nothing. It comes up over the phone's own lock screen
// too, turning the screen on, but never over the teacher's padlock
// (TeacherMessages waits for it to go). With several open, OK brings the one
// before back. If the student leaves it with home, its notification brings it
// back. A task of its own, out of the recents.
class TeacherMessageActivity : Activity() {
    private lateinit var header: TextView
    private lateinit var message: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setShowWhenLocked(true)
        setTurnScreenOn(true)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        setContentView(layout())
        current = this
    }

    override fun onResume() {
        super.onResume()
        render()
    }

    override fun onNewIntent(intent: Intent?) {
        super.onNewIntent(intent)
        render()
    }

    override fun onDestroy() {
        if (current === this) current = null
        super.onDestroy()
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        // As on a PC, only "OK, ENTENDI" closes it.
    }

    private fun render() {
        val messages = TeacherTalk.messages
        val text = messages?.newest
        if (messages == null || text == null) {
            finishAndRemoveTask()
            return
        }
        header.text = TeacherMessages.header(messages.count)
        message.text = text
        message.scrollTo(0, 0)
    }

    private fun onOk() {
        val messages = TeacherTalk.messages
        if (messages == null) {
            finishAndRemoveTask()
            return
        }
        messages.ok()
        if (messages.count > 0) render() else TeacherTalk.messagesRead()
    }

    private fun layout(): LinearLayout {
        val pad = dp(24f)
        header = TextView(this).apply {
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 16f)
            setTextColor(HEADER_INK)
            gravity = Gravity.CENTER
            setPadding(0, dp(8f), 0, dp(8f))
        }
        message = TextView(this).apply {
            setTextColor(Color.WHITE)
            typeface = Typeface.DEFAULT_BOLD
            gravity = Gravity.CENTER
            // From 34 sp down to 16: a short "ATENÇÃO TURMA!" is big, a
            // paragraph still fits, and anything longer scrolls.
            setAutoSizeTextTypeUniformWithConfiguration(16, 34, 1, TypedValue.COMPLEX_UNIT_SP)
            movementMethod = ScrollingMovementMethod()
            isVerticalScrollBarEnabled = true
        }
        val ok = Button(this).apply {
            text = TeacherMessages.OK_BUTTON
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 20f)
            typeface = Typeface.DEFAULT_BOLD
            setTextColor(BACKGROUND)
            background = GradientDrawable().apply {
                setColor(Color.WHITE)
                cornerRadius = dp(6f).toFloat()
            }
            setPadding(dp(32f), dp(14f), dp(32f), dp(14f))
            setOnClickListener { onOk() }
        }
        return LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(BACKGROUND)
            setPadding(pad, dp(36f), pad, dp(32f))
            addView(header, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
            addView(message, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f).apply {
                setMargins(0, dp(12f), 0, dp(24f))
            })
            addView(ok, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                gravity = Gravity.CENTER_HORIZONTAL
            })
        }
    }

    private fun dp(value: Float) =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, value, resources.displayMetrics).toInt()

    companion object {
        // The PC's colors: Color.DarkRed, and the header's light pink.
        private val BACKGROUND = Color.rgb(139, 0, 0)
        private val HEADER_INK = Color.rgb(255, 205, 205)

        // The screen, shown or left behind, if any (main thread only).
        private var current: TeacherMessageActivity? = null

        fun intent(context: Context): Intent =
            Intent(context, TeacherMessageActivity::class.java)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP)

        // Main thread: the last message went, or "Fechar programas".
        fun close() {
            current?.finishAndRemoveTask()
        }
    }
}
