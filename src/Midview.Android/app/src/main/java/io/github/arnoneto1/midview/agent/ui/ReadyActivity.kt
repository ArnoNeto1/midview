package io.github.arnoneto1.midview.agent.ui

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.graphics.Typeface
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.util.TypedValue
import android.view.Gravity
import android.view.WindowManager
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import io.github.arnoneto1.midview.agent.R
import io.github.arnoneto1.midview.agent.agent.DeviceName

// "Pronto. Este e o Celular 07 da escola.": shown once on the phone's screen
// when the cable preparation ends (the app becomes Device Owner). Closes with
// "OK", or by itself after five minutes.
class ReadyActivity : Activity() {
    private val handler = Handler(Looper.getMainLooper())

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setShowWhenLocked(true)
        setTurnScreenOn(true)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        setContentView(layout(DeviceName.read(this)))
        handler.postDelayed({ finish() }, CLOSE_AFTER_MILLIS)
    }

    override fun onDestroy() {
        handler.removeCallbacksAndMessages(null)
        super.onDestroy()
    }

    private fun layout(name: String): LinearLayout = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        gravity = Gravity.CENTER
        setBackgroundColor(BACKGROUND)
        val padding = dp(28f)
        setPadding(padding, padding, padding, padding)
        addView(TextView(this@ReadyActivity).apply {
            text = getString(R.string.ready_text, name)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 26f)
            setTextColor(INK)
            typeface = Typeface.DEFAULT_BOLD
            gravity = Gravity.CENTER
            setPadding(0, 0, 0, dp(32f))
        })
        addView(Button(this@ReadyActivity).apply {
            text = getString(R.string.ready_ok)
            setOnClickListener { finish() }
        })
    }

    private fun dp(value: Float) =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, value, resources.displayMetrics).toInt()

    companion object {
        private const val BACKGROUND = 0xFF404040.toInt()
        private const val INK = 0xFFEBEBEB.toInt()
        private const val CLOSE_AFTER_MILLIS = 5 * 60 * 1000L

        fun intent(context: Context): Intent =
            Intent(context, ReadyActivity::class.java)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_NO_HISTORY)

        // From the background, a Device Owner may start screens.
        fun show(context: Context) {
            try {
                context.startActivity(intent(context))
            } catch (e: RuntimeException) {
                // Refused: the phone is prepared all the same.
            }
        }
    }
}
