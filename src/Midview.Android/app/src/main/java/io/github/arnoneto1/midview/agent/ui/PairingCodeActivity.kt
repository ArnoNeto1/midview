package io.github.arnoneto1.midview.agent.ui

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.graphics.Typeface
import android.os.Bundle
import android.util.TypedValue
import android.view.Gravity
import android.view.WindowManager
import android.widget.LinearLayout
import android.widget.TextView

// The pairing code on the whole phone screen while a teacher pairs with it,
// as PairingCodeWindow on a PC. Nothing to tap: the teacher compares the
// code with the one on their own screen. The agent shows it (show) when the
// code is ready and takes it down (hide) however the pairing ends.
class PairingCodeActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setShowWhenLocked(true)
        setTurnScreenOn(true)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
    }

    override fun onResume() {
        super.onResume()
        val shown = current
        if (shown == null) {
            // The pairing ended before the screen came up.
            finish()
            return
        }
        open = this
        setContentView(layout(shown))
    }

    override fun onPause() {
        super.onPause()
        if (open === this) open = null
    }

    private fun layout(shown: Shown): LinearLayout {
        val (title, detail, code, footer) = texts(shown.code, shown.teacher)
        return LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            setBackgroundColor(BACKGROUND)
            val padding = dp(28f)
            setPadding(padding, padding, padding, padding)
            addView(label(title, 24f, INK, bold = true))
            addView(label(detail, 16f, MUTED_INK))
            addView(label(code, 56f, INK, bold = true).apply { setPadding(0, dp(32f), 0, dp(32f)) })
            addView(label(footer, 15f, MUTED_INK))
        }
    }

    private fun label(text: String, size: Float, color: Int, bold: Boolean = false) = TextView(this).apply {
        this.text = text
        setTextSize(TypedValue.COMPLEX_UNIT_SP, size)
        setTextColor(color)
        gravity = Gravity.CENTER
        if (bold) typeface = Typeface.DEFAULT_BOLD
        setPadding(0, dp(6f), 0, dp(6f))
    }

    private fun dp(value: Float) =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, value, resources.displayMetrics).toInt()

    private data class Shown(val code: String, val teacher: String)

    companion object {
        private const val BACKGROUND = 0xFF404040.toInt()
        private const val INK = 0xFFEBEBEB.toInt()
        private const val MUTED_INK = 0xFFBEBEBE.toInt()

        @Volatile private var current: Shown? = null
        @Volatile private var open: PairingCodeActivity? = null

        // What the student reads, in the PC's words (PairingCodeWindow.Texts).
        fun texts(code: String, teacher: String) = listOf(
            "Pareamento com o professor",
            "O computador $teacher está conectando este celular ao Midview.",
            code,
            "O professor confere se o mesmo código aparece na tela dele.",
        )

        // An intent that opens the screen with the current code.
        fun intent(context: Context): Intent =
            Intent(context, PairingCodeActivity::class.java)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_NO_HISTORY)

        fun show(context: Context, code: String, teacher: String) {
            current = Shown(code, teacher)
            open?.let { activity -> activity.runOnUiThread { activity.setContentView(activity.layout(Shown(code, teacher))) } }
            try {
                context.startActivity(intent(context))
            } catch (e: RuntimeException) {
                // Not allowed from the background on this phone: the
                // notification (AgentService) shows the code instead.
            }
        }

        fun hide() {
            current = null
            open?.let { activity -> activity.runOnUiThread { activity.finish() } }
        }
    }
}
