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
import android.widget.ScrollView
import android.widget.TextView
import io.github.arnoneto1.midview.agent.agent.DeviceWord
import io.github.arnoneto1.midview.agent.policy.AdminUnlock
import io.github.arnoneto1.midview.agent.policy.SchoolNetwork
import io.github.arnoneto1.midview.agent.protocol.PhoneAdminPassword

// "Ajustes da escola", from the agent's notification: whoever knows the
// phones' admin password unlocks this screen for a moment (AdminUnlock's
// Grant, kept only by this screen) and frees for 10 minutes the Wi-Fi and
// airplane mode, USB debugging or the device's accounts, the same timed locks
// as the teacher's commands; then may open the Wi-Fi settings from here (while
// the network is free) or lock all again at once. With a password set, "Tirar
// o Midview deste celular..." opens LeaveActivity, which asks for it again. It
// opens nothing else: the only way out to the system is Android's own Wi-Fi
// screen. Without a password set by the teacher it says so; after five wrong
// ones it waits. Kept out of the screenshots (the teacher's view of the
// screen too). "celular" becomes "tablet" on a tablet (DeviceWord).
class SchoolSettingsActivity : Activity() {
    private val main = Handler(Looper.getMainLooper())
    private lateinit var status: TextView
    private lateinit var freedText: TextView
    private lateinit var note: TextView
    private lateinit var input: EditText
    private lateinit var unlockButton: Button
    private lateinit var choiceButtons: Map<AdminUnlock.Freeable, Button>
    private lateinit var wifiButton: Button
    private lateinit var lockButton: Button
    private lateinit var leaveButton: Button
    private lateinit var leaveNote: TextView
    private var checking = false
    private var afterAttempt: String? = null

    // The right password's unlock; only in this screen's memory.
    private var grant: AdminUnlock.Grant? = null
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
        setContentView(ScrollView(this).apply { addView(layout()) })
    }

    override fun onResume() {
        super.onResume()
        main.post(refresh)
    }

    override fun onPause() {
        main.removeCallbacks(refresh)
        super.onPause()
    }

    override fun onDestroy() {
        grant = null
        super.onDestroy()
    }

    private fun render() {
        // The check holds the password's lock for a moment: nothing asks it meanwhile.
        if (checking) {
            note.text = SchoolSettingsTexts.CHECKING
            note.visibility = View.VISIBLE
            input.isEnabled = false
            unlockButton.isEnabled = false
            return
        }
        val admin = SchoolNetwork.admin(this)
        val unlocked = admin.isValid(grant)
        // An unlock that ran out: the password is asked again, afresh.
        if (!unlocked && grant != null) {
            grant = null
            afterAttempt = null
        }
        val state = admin.state()
        status.text = say(
            when {
                unlocked -> SchoolSettingsTexts.CHOOSE
                state is AdminUnlock.State.NoPassword -> SchoolSettingsTexts.NO_PASSWORD
                state is AdminUnlock.State.LockedOut -> SchoolSettingsTexts.lockedOut(SchoolSettingsTexts.clock(state.untilMillis))
                else -> SchoolSettingsTexts.ASK
            },
        )

        val network = SchoolNetwork.network(this).freeUntil()
        val anyFree = network != null || SchoolNetwork.debugging(this).freeUntil() != null ||
            SchoolNetwork.accounts(this).freeUntil() != null
        freedText.text = say(
            SchoolSettingsTexts.freed(
                network?.let(SchoolSettingsTexts::clock),
                SchoolNetwork.debugging(this).freeUntil()?.let(SchoolSettingsTexts::clock),
                SchoolNetwork.accounts(this).freeUntil()?.let(SchoolSettingsTexts::clock),
            ),
        )
        freedText.visibility = if (anyFree) View.VISIBLE else View.GONE

        val asking = !unlocked && state is AdminUnlock.State.Ready
        input.visibility = if (asking) View.VISIBLE else View.GONE
        unlockButton.visibility = input.visibility
        unlockButton.isEnabled = true
        input.isEnabled = true
        choiceButtons.values.forEach { it.visibility = if (unlocked) View.VISIBLE else View.GONE }
        note.text = afterAttempt.takeIf { asking || unlocked }.orEmpty()
        note.visibility = if (note.text.isEmpty()) View.GONE else View.VISIBLE
        wifiButton.visibility = if (network != null) View.VISIBLE else View.GONE
        lockButton.visibility = if (anyFree) View.VISIBLE else View.GONE
        val hasPassword = state !is AdminUnlock.State.NoPassword
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
                if (attempt is AdminUnlock.Attempt.Unlocked) grant = attempt.grant
                afterAttempt = SchoolSettingsTexts.after(attempt)
                render()
            }
        }, "Midview admin password").start()
    }

    // One of the choices, with the unlock this screen holds.
    private fun free(what: AdminUnlock.Freeable) {
        val held = grant ?: return render()
        val admin = SchoolNetwork.admin(this)
        Thread({
            val result = admin.free(held, what)
            runOnUiThread {
                afterAttempt = SchoolSettingsTexts.afterFree(result)
                render()
            }
        }, "Midview admin free").start()
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

    // Everything free now locked again; locking needs no password.
    private fun lockNow() {
        val app = applicationContext
        Thread({
            if (SchoolNetwork.network(app).freeUntil() != null) SchoolNetwork.network(app).lockNow()
            if (SchoolNetwork.debugging(app).freeUntil() != null) SchoolNetwork.debugging(app).lockNow()
            if (SchoolNetwork.accounts(app).freeUntil() != null) SchoolNetwork.accounts(app).lockNow()
            runOnUiThread { render() }
        }, "Midview lock now").start()
    }

    private fun layout(): LinearLayout = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        val padding = dp(24f)
        setPadding(padding, padding, padding, padding)
        addView(text(SchoolSettingsTexts.TITLE, 24f).apply { typeface = Typeface.DEFAULT_BOLD })
        status = text("", 17f).also { addView(it) }
        freedText = text("", 15f).also { addView(it) }
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
        unlockButton = button(SchoolSettingsTexts.UNLOCK) { submit() }.also { addView(it) }
        choiceButtons = AdminUnlock.Freeable.entries.associateWith { what ->
            button(SchoolSettingsTexts.choice(what)) { free(what) }.also { addView(it) }
        }
        note = text("", 15f).also { addView(it) }
        wifiButton = button("Abrir Wi-Fi") { openWifi() }.also { addView(it) }
        lockButton = button(SchoolSettingsTexts.LOCK_NOW) { lockNow() }.also { addView(it) }
        leaveButton = button(say(LeaveTexts.OPEN)) { startActivity(LeaveActivity.intent(this@SchoolSettingsActivity)) }.also { addView(it) }
        leaveNote = text(say(LeaveTexts.NEEDS_PASSWORD), 15f).also { addView(it) }
        addView(button("Fechar") { finish() })
    }

    private fun say(text: String) = DeviceWord.say(text)

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
