package io.github.arnoneto1.midview.agent.find

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Bundle
import android.util.TypedValue
import android.view.Gravity
import android.view.ViewGroup
import android.view.WindowManager
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import io.github.arnoneto1.midview.agent.agent.DeviceWord

// "Tocar para encontrar" on the screen while the device rings: over the lock
// screen, turning the screen on, the teacher's words and one big "Parar",
// the only thing the student can do with it (FindRinger stops everything and
// gives the alarm volume back). Back does nothing. A task of its own, out of
// the recents.
class FindActivity : Activity() {
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
        // Stopped meanwhile (the teacher, the time): nothing to show.
        if (Finder.ringer?.isRinging != true) finishAndRemoveTask()
    }

    override fun onDestroy() {
        if (current === this) current = null
        super.onDestroy()
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        // Only "Parar" stops the sound.
    }

    private fun layout(): LinearLayout {
        val title = TextView(this).apply {
            text = DeviceWord.say(TEXT)
            setTextColor(Color.WHITE)
            typeface = Typeface.DEFAULT_BOLD
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 30f)
            gravity = Gravity.CENTER
        }
        val stop = Button(this).apply {
            text = STOP_BUTTON
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 28f)
            typeface = Typeface.DEFAULT_BOLD
            setTextColor(BACKGROUND)
            background = GradientDrawable().apply {
                setColor(Color.WHITE)
                cornerRadius = dp(10f).toFloat()
            }
            setPadding(dp(56f), dp(20f), dp(56f), dp(20f))
            setOnClickListener {
                Finder.ringer?.stop(FindRinger.Stop.STUDENT)
                finishAndRemoveTask()
            }
        }
        return LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            setBackgroundColor(BACKGROUND)
            val pad = dp(32f)
            setPadding(pad, pad, pad, pad)
            addView(title, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
            addView(stop, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                gravity = Gravity.CENTER_HORIZONTAL
                topMargin = dp(48f)
            })
        }
    }

    private fun dp(value: Float) =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, value, resources.displayMetrics).toInt()

    companion object {
        const val TEXT = "O professor está procurando este celular"
        const val STOP_BUTTON = "Parar"

        // A deep blue: not the teacher's red message.
        private val BACKGROUND = Color.rgb(13, 71, 161)

        // The screen shown, if any (main thread only).
        private var current: FindActivity? = null

        fun intent(context: Context): Intent =
            Intent(context, FindActivity::class.java)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP)

        // Main thread: the ringing stopped. A ring started again meanwhile
        // keeps its screen (a close queued from before must not take it).
        fun close() {
            if (Finder.ringer?.isRinging == true) return
            current?.finishAndRemoveTask()
        }
    }
}

// The device's one FindRinger, set by the agent (AgentService), for the
// screen's "Parar".
object Finder {
    @Volatile var ringer: FindRinger? = null
}
