package io.github.arnoneto1.midview.agent.ui

import android.app.Activity
import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.graphics.Typeface
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.text.InputFilter
import android.text.InputType
import android.util.TypedValue
import android.view.KeyEvent
import android.view.View
import android.view.WindowManager
import android.view.inputmethod.EditorInfo
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.TextView
import io.github.arnoneto1.midview.agent.policy.AdminUnlock
import io.github.arnoneto1.midview.agent.policy.SchoolNetwork
import io.github.arnoneto1.midview.agent.protocol.PhoneAdminPassword

// "Ajustes da escola", from the agent's notification: whoever knows the
// phones' admin password frees the Wi-Fi and airplane mode for 10 minutes
// (AdminUnlock), and then may open the Wi-Fi settings from here or lock them
// again at once. With a password set, "Tirar o Midview deste celular..."
// opens LeaveActivity, which asks for it again. It opens nothing else: the only way out to the system is
// Android's own Wi-Fi screen, and only while the network is free. Without a
// password set by the teacher it says so; after five wrong ones it waits.
// Kept out of the screenshots (the teacher's view of the screen too).
class SchoolSettingsActivity : Activity() {
    private val main = Handler(Looper.getMainLooper())
    private lateinit var status: TextView
    private lateinit var note: TextView
    private lateinit var input: EditText
    private lateinit var freeButton: Button
    private lateinit var wifiButton: Button
    private lateinit var lockButton: Button
    private lateinit var leaveButton: Button
    private lateinit var leaveNote: TextView
    private var checking = false
    private var afterAttempt: String? = null
    private val refresh = object : Runnable {
        override fun run() {
            render()
            main.postDelayed(this, REFRESH_MILLIS)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        title = SchoolSettingsTexts.TITLE
        setContentView(layout())
    }

    override fun onResume() {
        super.onResume()
        main.post(refresh)
    }

    override fun onPause() {
        main.removeCallbacks(refresh)
        super.onPause()
    }

    private fun render() {
        // The check holds the password's lock for a moment: nothing asks it meanwhile.
        if (checking) {
            note.text = SchoolSettingsTexts.CHECKING
            note.visibility = View.VISIBLE
            input.isEnabled = false
            freeButton.isEnabled = false
            return
        }
        val until = SchoolNetwork.network(this).freeUntil()
        val state = if (until == null) SchoolNetwork.admin(this).state() else null
        status.text = when {
            until != null -> SchoolSettingsTexts.freeUntil(SchoolSettingsTexts.clock(until))
            state is AdminUnlock.State.NoPassword -> SchoolSettingsTexts.NO_PASSWORD
            state is AdminUnlock.State.LockedOut -> SchoolSettingsTexts.lockedOut(SchoolSettingsTexts.clock(state.untilMillis))
            else -> SchoolSettingsTexts.ASK
        }
        val asking = state is AdminUnlock.State.Ready
        input.visibility = if (asking) View.VISIBLE else View.GONE
        freeButton.visibility = input.visibility
        freeButton.isEnabled = true
        input.isEnabled = true
        note.text = afterAttempt.takeIf { asking }.orEmpty()
        note.visibility = if (note.text.isEmpty()) View.GONE else View.VISIBLE
        wifiButton.visibility = if (until != null) View.VISIBLE else View.GONE
        lockButton.visibility = wifiButton.visibility
        val hasPassword = SchoolNetwork.admin(this).state() !is AdminUnlock.State.NoPassword
        leaveButton.visibility = if (hasPassword) View.VISIBLE else View.GONE
        leaveNote.visibility = if (hasPassword) View.GONE else View.VISIBLE
    }

    // Off the main thread: the check takes about a second, on purpose.
    private fun submit() {
        if (checking) return
        val typed = input.text.toString()
        input.text.clear()
        if (typed.isEmpty()) return
        checking = true
        render()
        val admin = SchoolNetwork.admin(this)
        Thread({
            val attempt = admin.tryPassword(typed)
            runOnUiThread {
                checking = false
                afterAttempt = SchoolSettingsTexts.after(attempt)
                render()
            }
        }, "Midview admin password").start()
    }

    private fun openWifi() {
        if (SchoolNetwork.network(this).freeUntil() == null) {
            render()
            return
        }
        try {
            startActivity(Intent(Settings.ACTION_WIFI_SETTINGS))
        } catch (e: ActivityNotFoundException) {
            // No Wi-Fi screen on this phone: the quick settings still work.
        }
    }

    private fun lockNow() {
        val network = SchoolNetwork.network(this)
        Thread({
            network.lockNow()
            runOnUiThread { render() }
        }, "Midview lock network").start()
    }

    private fun layout(): LinearLayout = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        val padding = dp(24f)
        setPadding(padding, padding, padding, padding)
        addView(text(SchoolSettingsTexts.TITLE, 24f).apply { typeface = Typeface.DEFAULT_BOLD })
        status = text("", 17f).also { addView(it) }
        input = EditText(this@SchoolSettingsActivity).apply {
            hint = "Senha de administrador"
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
            imeOptions = EditorInfo.IME_ACTION_DONE
            isSingleLine = true
            filters = arrayOf(
                InputFilter.LengthFilter(PhoneAdminPassword.MAX_LENGTH),
                InputFilter { source, start, end, _, _, _ ->
                    val kept = source.subSequence(start, end).filter(PhoneAdminPassword::isAllowed)
                    if (kept.length == end - start) null else kept
                },
            )
            // The keyboard's "done", or a hardware Enter.
            setOnEditorActionListener { _, action, event ->
                val enter = event?.keyCode == KeyEvent.KEYCODE_ENTER
                if (action == EditorInfo.IME_ACTION_DONE || (enter && event?.action == KeyEvent.ACTION_DOWN)) submit()
                action == EditorInfo.IME_ACTION_DONE || enter
            }
        }
        addView(input)
        note = text("", 15f).also { addView(it) }
        freeButton = button("Liberar") { submit() }.also { addView(it) }
        wifiButton = button("Abrir Wi-Fi") { openWifi() }.also { addView(it) }
        lockButton = button("Travar agora") { lockNow() }.also { addView(it) }
        leaveButton = button(LeaveTexts.OPEN) { startActivity(LeaveActivity.intent(this@SchoolSettingsActivity)) }.also { addView(it) }
        leaveNote = text(LeaveTexts.NEEDS_PASSWORD, 15f).also { addView(it) }
        addView(button("Fechar") { finish() })
    }

    private fun text(value: String, size: Float) = TextView(this).apply {
        text = value
        setTextSize(TypedValue.COMPLEX_UNIT_SP, size)
        setPadding(0, dp(8f), 0, dp(8f))
    }

    private fun button(label: String, onClick: () -> Unit) = Button(this).apply {
        text = label
        setOnClickListener { onClick() }
    }

    private fun dp(value: Float) =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, value, resources.displayMetrics).toInt()

    companion object {
        const val TITLE = SchoolSettingsTexts.TITLE
        private const val REFRESH_MILLIS = 15_000L

        fun intent(context: Context): Intent =
            Intent(context, SchoolSettingsActivity::class.java)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
    }
}
