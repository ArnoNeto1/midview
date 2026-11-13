package io.github.arnoneto1.midview.agent.ui

import android.app.Activity
import android.app.AlertDialog
import android.content.Context
import android.content.Intent
import android.graphics.Typeface
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.text.InputFilter
import android.text.InputType
import android.util.Log
import android.util.TypedValue
import android.view.View
import android.view.WindowManager
import android.view.inputmethod.EditorInfo
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import io.github.arnoneto1.midview.agent.leave.LeaveReport
import io.github.arnoneto1.midview.agent.leave.PhoneLeaving
import io.github.arnoneto1.midview.agent.policy.AdminUnlock
import io.github.arnoneto1.midview.agent.policy.AndroidDevicePolicyGateway
import io.github.arnoneto1.midview.agent.policy.SchoolNetwork
import io.github.arnoneto1.midview.agent.protocol.PhoneAdminPassword

// "Tirar o Midview deste celular", from "Ajustes da escola": whoever knows
// the phones' admin password (AdminUnlock.checkOnly, with the same count of
// wrong ones and the same wait as freeing the Wi-Fi) chooses between
// "Desfazer e tirar o Midview" (PhoneLeave: the student's files kept) and
// "Restaurar de fábrica" (the Device Owner's wipe), each confirmed once
// more. The password holds for this screen only, for at most a minute
// (AdminSession): the screen going away (the phone locked, another app in
// front) or the minute running out asks for it again, and closes an open
// confirmation. A lock the leaving could not lift is said, and the choices
// stay (the factory reset among them).
// Kept out of the screenshots, as "Ajustes da escola".
class LeaveActivity : Activity() {
    private lateinit var status: TextView
    private lateinit var input: EditText
    private lateinit var continueButton: Button
    private lateinit var choices: LinearLayout
    private val session = AdminSession(SystemClock::elapsedRealtime)
    private val verified: Boolean get() = session.isOpen
    private var busy = false
    private var dialog: AlertDialog? = null
    private val main = Handler(Looper.getMainLooper())
    private val expire = Runnable { closeSession() }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        title = LeaveTexts.TITLE
        setContentView(ScrollView(this).apply { addView(layout()) })
        render(null)
    }

    // The phone locked, or anything else in front: the password goes.
    override fun onStop() {
        closeSession()
        super.onStop()
    }

    private fun closeSession() {
        main.removeCallbacks(expire)
        session.end()
        dialog?.dismiss()
        dialog = null
        render(null)
    }

    private fun render(note: String?) {
        val state = SchoolNetwork.admin(this).state()
        val asking = !verified && !busy && state is AdminUnlock.State.Ready
        status.text = when {
            busy -> note ?: LeaveTexts.WORKING
            verified -> note ?: LeaveTexts.CHOOSE
            state is AdminUnlock.State.NoPassword -> LeaveTexts.NEEDS_PASSWORD
            state is AdminUnlock.State.LockedOut ->
                SchoolSettingsTexts.lockedOut(SchoolSettingsTexts.clock(state.untilMillis))
            else -> note ?: LeaveTexts.ASK
        }
        input.visibility = if (asking) View.VISIBLE else View.GONE
        continueButton.visibility = input.visibility
        choices.visibility = if (verified && !busy) View.VISIBLE else View.GONE
    }

    // Off the main thread: the check takes about a second, on purpose.
    private fun submit() {
        if (busy) return
        val typed = input.text.toString()
        input.text.clear()
        if (typed.isEmpty()) return
        busy = true
        render(LeaveTexts.CHECKING)
        val admin = SchoolNetwork.admin(this)
        Thread({
            val attempt = admin.checkOnly(typed)
            runOnUiThread {
                busy = false
                if (attempt == AdminUnlock.Attempt.Verified) {
                    session.verified()
                    main.removeCallbacks(expire)
                    main.postDelayed(expire, session.remainingMillis())
                }
                render(LeaveTexts.after(attempt))
            }
        }, "Midview admin password").start()
    }

    private fun confirm(text: String, yes: String, action: () -> Unit) {
        dialog?.dismiss()
        dialog = AlertDialog.Builder(this)
            .setTitle(LeaveTexts.TITLE)
            .setMessage(text)
            // The minute may have run out while the question was up.
            .setPositiveButton(yes) { _, _ -> if (verified) action() else render(null) }
            .setNegativeButton(LeaveTexts.CANCEL, null)
            .show()
    }

    // The locks undone, then the teachers forgotten, the Device Owner given
    // up and Android's uninstall asked (it comes over this screen).
    private fun leave() {
        if (!verified || busy) return
        busy = true
        render(LeaveTexts.WORKING)
        Thread({
            val leaving = PhoneLeaving.get(this)
            val report = try {
                leaving.start()
            } catch (e: RuntimeException) {
                Log.e(TAG, "The Midview could not leave the phone", e)
                null
            }
            // A lock that stayed: said, and the choices come back (while
            // the minute lasts) for another try or the factory reset.
            if (report == null || !report.complete) {
                runOnUiThread {
                    busy = false
                    render(report?.text() ?: LeaveReport.STAYS)
                }
                return@Thread
            }
            runOnUiThread { render(report.text()) }
            try {
                leaving.finish()
            } catch (e: RuntimeException) {
                Log.e(TAG, "The Midview could not finish leaving", e)
            }
        }, "Midview leaving").start()
    }

    private fun wipe() {
        if (!verified || busy) return
        busy = true
        render(LeaveTexts.WIPING)
        val gateway = AndroidDevicePolicyGateway(this)
        Thread({
            try {
                gateway.wipe()
            } catch (e: RuntimeException) {
                Log.e(TAG, "The wipe asked on the phone failed", e)
                runOnUiThread {
                    busy = false
                    render(LeaveTexts.WIPE_FAILED)
                }
            }
        }, "Midview wipe").start()
    }

    private fun layout(): LinearLayout = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        val padding = dp(24f)
        setPadding(padding, padding, padding, padding)
        addView(text(LeaveTexts.TITLE, 24f).apply { typeface = Typeface.DEFAULT_BOLD })
        status = text("", 17f).also { addView(it) }
        input = EditText(this@LeaveActivity).apply {
            hint = "Senha de administrador"
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
            imeOptions = EditorInfo.IME_ACTION_DONE
            isSingleLine = true
            filters = arrayOf(InputFilter.LengthFilter(PhoneAdminPassword.MAX_LENGTH))
            setOnEditorActionListener { _, action, _ ->
                if (action == EditorInfo.IME_ACTION_DONE) submit()
                action == EditorInfo.IME_ACTION_DONE
            }
        }
        addView(input)
        continueButton = button("Continuar") { submit() }.also { addView(it) }
        choices = LinearLayout(this@LeaveActivity).apply {
            orientation = LinearLayout.VERTICAL
            addView(button(LeaveTexts.KEEP_BUTTON) {
                confirm(LeaveTexts.CONFIRM_KEEP, LeaveTexts.CONFIRM_KEEP_YES) { leave() }
            })
            addView(text(LeaveTexts.KEEP_TEXT, 15f))
            // Only the Device Owner can erase the phone.
            if (AndroidDevicePolicyGateway(this@LeaveActivity).isDeviceOwner()) {
                addView(button(LeaveTexts.WIPE_BUTTON) {
                    confirm(LeaveTexts.CONFIRM_WIPE, LeaveTexts.CONFIRM_WIPE_YES) { wipe() }
                })
                addView(text(LeaveTexts.WIPE_TEXT, 15f))
            }
        }
        addView(choices)
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
        private const val TAG = "Midview"

        fun intent(context: Context): Intent =
            Intent(context, LeaveActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
    }
}
