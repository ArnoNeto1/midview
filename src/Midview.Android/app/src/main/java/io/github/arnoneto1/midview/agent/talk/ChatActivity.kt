package io.github.arnoneto1.midview.agent.talk

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.text.InputFilter
import android.text.InputType
import android.text.SpannableStringBuilder
import android.text.Spanned
import android.text.style.ForegroundColorSpan
import android.text.style.StyleSpan
import android.util.TypedValue
import android.view.Gravity
import android.view.KeyEvent
import android.view.ViewGroup
import android.view.inputmethod.EditorInfo
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import io.github.arnoneto1.midview.agent.protocol.TextMessage

// "Conversa com o professor", as the PC's chat window (WinFormsChatWindows):
// what was said, in order ("Professor: ...", "Você: ..."), a box to answer
// and "Enviar" (or the keyboard's send). Back or home only leave it: the
// conversation goes on, its notification brings the screen back, and so does
// the teacher's next line. When the teacher ends the conversation the screen
// says so, stops taking text, and closes a few seconds later. One screen for
// every conversation: the intent says which one it shows.
class ChatActivity : Activity() {
    private val main = Handler(Looper.getMainLooper())
    private lateinit var transcript: TextView
    private lateinit var scroller: ScrollView
    private lateinit var input: EditText
    private lateinit var send: Button
    private var conversationId = 0
    private val closeLater = Runnable { finishAndRemoveTask() }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(layout())
        title = TeacherChats.TITLE
        current = this
        conversationId = intent?.getIntExtra(EXTRA_CONVERSATION, 0) ?: 0
    }

    override fun onNewIntent(intent: Intent?) {
        super.onNewIntent(intent)
        val id = intent?.getIntExtra(EXTRA_CONVERSATION, 0) ?: 0
        if (id != 0 && id != conversationId) {
            conversationId = id
            input.text.clear()
        }
    }

    override fun onResume() {
        super.onResume()
        render()
        input.requestFocus()
    }

    override fun onDestroy() {
        main.removeCallbacks(closeLater)
        if (current === this) current = null
        super.onDestroy()
    }

    private fun conversation() = TeacherTalk.chats?.conversation(conversationId)

    private fun render() {
        val conversation = conversation()
        if (conversation == null) {
            // Over before the screen came back (or the agent restarted):
            // nothing left to show.
            if (!main.hasCallbacks(closeLater)) finishAndRemoveTask()
            return
        }
        transcript.text = shown(conversation.lines)
        val on = !conversation.ended
        input.isEnabled = on
        send.isEnabled = on
        scroller.post { scroller.fullScroll(ScrollView.FOCUS_DOWN) }
    }

    // The teacher ended it: the last lines, the note, then the screen goes.
    private fun showEnded(lines: List<ChatLine>) {
        transcript.text = shown(lines)
        input.isEnabled = false
        send.isEnabled = false
        scroller.post { scroller.fullScroll(ScrollView.FOCUS_DOWN) }
        main.removeCallbacks(closeLater)
        main.postDelayed(closeLater, ENDED_LINGER_MILLIS)
    }

    private fun onSend() {
        val conversation = conversation() ?: return
        if (conversation.reply(input.text.toString())) input.text.clear()
    }

    private fun shown(lines: List<ChatLine>): CharSequence {
        val text = SpannableStringBuilder()
        for (line in lines) {
            if (text.isNotEmpty()) text.append('\n')
            val start = text.length
            text.append(line.shown)
            when (line.who) {
                ChatLine.Who.TEACHER -> text.setSpan(StyleSpan(Typeface.BOLD), start, start + "Professor:".length, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
                ChatLine.Who.STUDENT -> text.setSpan(ForegroundColorSpan(STUDENT_INK), start, text.length, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
                ChatLine.Who.NOTE -> text.setSpan(ForegroundColorSpan(NOTE_INK), start, text.length, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
            }
        }
        return text
    }

    private fun layout(): LinearLayout {
        val pad = dp(16f)
        val heading = TextView(this).apply {
            text = TeacherChats.TITLE
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 20f)
            typeface = Typeface.DEFAULT_BOLD
            setTextColor(INK)
            setPadding(pad, dp(18f), pad, dp(12f))
        }
        transcript = TextView(this).apply {
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 17f)
            setTextColor(INK)
            setLineSpacing(0f, 1.15f)
            setTextIsSelectable(true)
            setPadding(pad, dp(8f), pad, dp(8f))
        }
        scroller = ScrollView(this).apply {
            setBackgroundColor(TRANSCRIPT_BACKGROUND)
            addView(transcript)
        }
        input = EditText(this).apply {
            hint = HINT
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 17f)
            setTextColor(INK)
            setHintTextColor(NOTE_INK)
            filters = arrayOf(InputFilter.LengthFilter(TextMessage.MAX_LENGTH))
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_CAP_SENTENCES
            imeOptions = EditorInfo.IME_ACTION_SEND
            setOnEditorActionListener { _, action, event ->
                val enter = event?.keyCode == KeyEvent.KEYCODE_ENTER && event.action == KeyEvent.ACTION_DOWN
                if (action == EditorInfo.IME_ACTION_SEND || enter) {
                    onSend()
                    true
                } else {
                    false
                }
            }
        }
        send = Button(this).apply {
            text = TeacherChats.SEND
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 16f)
            typeface = Typeface.DEFAULT_BOLD
            setTextColor(Color.WHITE)
            background = GradientDrawable().apply {
                setColor(ACCENT)
                cornerRadius = dp(6f).toFloat()
            }
            setPadding(dp(18f), dp(10f), dp(18f), dp(10f))
            setOnClickListener { onSend() }
        }
        val row = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(pad, dp(8f), pad, dp(12f))
            addView(input, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            addView(send, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                marginStart = dp(10f)
            })
        }
        return LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(BACKGROUND)
            fitsSystemWindows = true
            addView(heading, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
            addView(scroller, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
            addView(row, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        }
    }

    private fun dp(value: Float) =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, value, resources.displayMetrics).toInt()

    companion object {
        private const val EXTRA_CONVERSATION = "conversation"
        const val HINT = "Escreva sua resposta"

        // How long "O professor encerrou a conversa" stays before the screen closes.
        const val ENDED_LINGER_MILLIS = 4_000L

        // The dark look of the teacher's Painel.
        private const val BACKGROUND = 0xFF121A26.toInt()
        private const val TRANSCRIPT_BACKGROUND = 0xFF1A2433.toInt()
        private const val INK = 0xFFF2F5F8.toInt()
        private const val STUDENT_INK = 0xFF9FD3FF.toInt()
        private const val NOTE_INK = 0xFF9AA5B4.toInt()
        private const val ACCENT = 0xFF2F6FED.toInt()

        // The screen, shown or left behind, if any (main thread only).
        private var current: ChatActivity? = null

        fun intent(context: Context, conversationId: Int): Intent =
            Intent(context, ChatActivity::class.java)
                .putExtra(EXTRA_CONVERSATION, conversationId)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP)

        // Main thread: the conversation has something new.
        fun refresh(conversationId: Int) {
            current?.takeIf { it.conversationId == conversationId }?.render()
        }

        // Main thread: the conversation is over (TeacherChats already
        // dropped it; lines end with the note): the screen showing it says
        // so and closes.
        fun ended(conversationId: Int, lines: List<ChatLine>) {
            current?.takeIf { it.conversationId == conversationId }?.showEnded(lines)
        }
    }
}
