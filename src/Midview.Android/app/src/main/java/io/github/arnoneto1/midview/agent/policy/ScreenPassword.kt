package io.github.arnoneto1.midview.agent.policy

import io.github.arnoneto1.midview.agent.protocol.PhoneScreenPassword

// What the lock screen's keeping needs from Android, behind an interface so
// it is tested on the JVM.
interface ScreenLockGateway {
    fun isDeviceOwner(): Boolean

    // The phone has a lock (KeyguardManager.isDeviceSecure).
    fun isDeviceSecure(): Boolean

    fun isResetTokenActive(): Boolean

    // False when Android would not take it.
    fun setResetToken(token: ByteArray): Boolean

    // The PIN (empty: none) set with the reset token; false when refused.
    fun resetPassword(pin: String, token: ByteArray): Boolean

    // Android's own rule for the lock (a numeric PIN of at least 4 digits,
    // or none), so Settings offers the student no lock but a PIN while the
    // teacher's is on. Throws when Android refuses.
    fun requirePin(required: Boolean)

    // Which start of the phone this is (Settings.Global.BOOT_COUNT).
    fun bootCount(): Int
}

// The teacher's PIN on the school phone, which the student can neither
// change nor remove (ScreenPasswordPolicy): the agent gives Android a reset
// token while it can (no lock, or after the next unlock), and with it puts
// the teacher's PIN back whenever the lock changes and the agent did not do
// it. Checked when Android says the lock changed (lockChanged), at every
// start of the agent and at every watchdog tick (check), and set by the
// teacher (setTeacherPin). The teacher's screen lock ("Bloquear telas", a
// lock task over the screen) and the screen's capture are separate and left
// alone; Android's lock screen is never disabled (setKeyguardDisabled only
// works without a PIN).
class ScreenPassword(
    private val gw: ScreenLockGateway,
    private val store: ScreenPasswordStore,
    private val newToken: () -> ByteArray,
    clock: () -> Long,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) {
    private val gate = Any()
    private val own = OwnChanges(clock)

    // Android refused the token: not asked again until the agent starts again.
    private var tokenRefused = false
    private var pinRequired: Boolean? = null
    private var lastToken: ResetToken? = null
    private var lastNote: String? = null

    // Whether Android's "password changed" (AdminReceiver.onPasswordChanged)
    // now is the agent's own change. Asked as it comes, before lockChanged.
    fun isOwnChange(): Boolean = own.isOwn()

    // Someone other than the agent changed the lock: kept, so it is undone
    // also if the agent stops before the check.
    fun lockChanged(): ScreenPasswordAction = synchronized(gate) {
        store.save(store.current.copy(changedByOther = true))
        checkLocked()
    }

    // starting: a start of the agent, which also asks Android again for a
    // token it refused before.
    fun check(starting: Boolean = false): ScreenPasswordAction = synchronized(gate) {
        if (starting) {
            tokenRefused = false
            pinRequired = null
        }
        checkLocked()
    }

    // The token's state now, for the log and the teacher: "ativa",
    // "pendente" or "sem chave".
    fun tokenState(): ResetToken = synchronized(gate) { tokenNow(gw.isDeviceSecure()) }

    // Whether the teacher's PIN is on the phone (for PhoneLeave).
    fun teacherPinSet(): Boolean = synchronized(gate) { store.current.teacherPin != null }

    // SET_PHONE_SCREEN_PASSWORD: the teacher's PIN (empty payload: none).
    fun setTeacherPin(payload: ByteArray): Pair<Boolean, String> {
        val pin = PhoneScreenPassword.decode(payload) ?: return false to PhoneScreenPassword.INVALID
        synchronized(gate) {
            if (!gw.isDeviceOwner()) return false to NOT_OWNER
            var secure = gw.isDeviceSecure()
            if (tokenNow(secure) == ResetToken.NONE) {
                tokenRefused = false
                giveToken(secure)
                secure = gw.isDeviceSecure()
            }
            when (tokenNow(secure)) {
                ResetToken.PENDING -> return false to TOKEN_PENDING
                ResetToken.NONE -> return false to TOKEN_REFUSED
                ResetToken.ACTIVE -> {}
            }
            val before = store.current
            val wanted = pin.ifEmpty { null }
            if (!store.save(before.copy(teacherPin = wanted, changedByOther = false, pinUnknown = false))) {
                store.save(before)
                return false to NOT_SAVED
            }
            if (wanted == null) requirePin(false)
            val done = (wanted == null && !secure) || reset(pin)
            if (!done) {
                store.save(before)
                if (before.teacherPin != null) requirePin(true)
                return false to RESET_REFUSED
            }
            if (wanted != null) requirePin(true)
            lastNote = null
            log(if (wanted == null) "Teacher's lock screen PIN removed." else "Teacher's lock screen PIN set.", null)
            return true to if (wanted == null) PIN_REMOVED else PIN_SET
        }
    }

    private fun checkLocked(): ScreenPasswordAction {
        if (!gw.isDeviceOwner()) return ScreenPasswordAction.NOTHING
        val state = store.current
        val secure = gw.isDeviceSecure()
        val token = tokenNow(secure)
        val action = ScreenPasswordPolicy.decide(true, secure, token, state.teacherPin, state.changedByOther, state.pinUnknown)
        if (state.pinUnknown && token == ResetToken.ACTIVE) {
            note("The teacher's lock screen PIN is unknown (its file was damaged): the lock is left as it is until the teacher sends the PIN again.")
        }
        when (action) {
            ScreenPasswordAction.SET_TOKEN -> giveToken(secure)
            ScreenPasswordAction.WAIT_FOR_UNLOCK ->
                note("The lock screen's reset token is pending: it becomes active the next time the phone is unlocked with its PIN.")
            ScreenPasswordAction.CLEAR -> {
                // Android would refuse "no lock" while its PIN rule is on.
                requirePin(false)
                if (reset(PhoneScreenPassword.NONE)) {
                    store.save(state.copy(changedByOther = false))
                    log("A lock screen lock the teacher did not set was removed.", null)
                }
            }
            ScreenPasswordAction.RESTORE ->
                if (reset(state.teacherPin!!)) {
                    store.save(state.copy(changedByOther = false))
                    log("The teacher's lock screen PIN was put back (it had been ${if (secure) "changed" else "removed"}).", null)
                }
            ScreenPasswordAction.NOTHING ->
                if (state.changedByOther) store.save(state.copy(changedByOther = false))
        }
        if (token == ResetToken.ACTIVE && !store.current.pinUnknown) requirePin(store.current.teacherPin != null && gw.isDeviceSecure())
        return action
    }

    private fun tokenNow(secure: Boolean): ResetToken {
        val state = store.current
        val token = ScreenPasswordPolicy.token(
            stored = state.token != null,
            active = state.token != null && gw.isResetTokenActive(),
            givenThisBoot = state.tokenBoot == gw.bootCount(),
            secure = secure,
        )
        if (token != lastToken) {
            lastToken = token
            log("Lock screen reset token: ${describe(token)}.", null)
        }
        return token
    }

    // The token kept (or a new one) given to Android.
    private fun giveToken(secure: Boolean) {
        if (tokenRefused) return
        val state = store.current
        val token = state.token?.let(::fromHex) ?: newToken()
        val given = try {
            gw.setResetToken(token)
        } catch (e: RuntimeException) {
            log("Android refused the lock screen's reset token", e)
            false
        }
        if (!given) {
            tokenRefused = true
            if (state.token == null) log("Android refused the lock screen's reset token.", null)
            return
        }
        store.save(state.copy(token = toHex(token), tokenBoot = gw.bootCount()))
        tokenNow(secure)
    }

    private fun reset(pin: String): Boolean {
        val token = store.current.token?.let(::fromHex) ?: return false
        own.starting()
        var done = false
        try {
            done = gw.resetPassword(pin, token)
        } catch (e: RuntimeException) {
            log("Android refused to change the lock screen", e)
        } finally {
            own.finished(changed = done)
        }
        if (!done) log("Android refused to change the lock screen.", null)
        return done
    }

    private fun requirePin(required: Boolean) {
        if (pinRequired == required) return
        pinRequired = required
        try {
            gw.requirePin(required)
        } catch (e: RuntimeException) {
            // Android takes the rule only once the app's admin declares it
            // (limit-password), which a phone made Device Owner by an older
            // version only reads at its next restart. The PIN is kept anyway.
            log("Android refused the lock screen's PIN rule", e)
        }
    }

    private fun note(text: String) {
        if (text == lastNote) return
        lastNote = text
        log(text, null)
    }

    companion object {
        const val PIN_SET = "PIN posto no celular"
        const val PIN_REMOVED = "PIN tirado do celular"
        const val NOT_OWNER = "o Midview não é o administrador deste celular"
        const val TOKEN_PENDING = "chave de redefinição ainda não ativa: desbloqueie o celular uma vez"
        const val TOKEN_REFUSED = "o Android não aceitou a chave de redefinição da senha"
        const val RESET_REFUSED = "o Android recusou o PIN"
        const val NOT_SAVED = "o celular não conseguiu guardar o PIN"

        fun describe(token: ResetToken): String = when (token) {
            ResetToken.ACTIVE -> "ativa"
            ResetToken.PENDING -> "pendente"
            ResetToken.NONE -> "sem chave"
        }

        private const val HEX = "0123456789ABCDEF"

        fun toHex(bytes: ByteArray): String = buildString {
            for (b in bytes) append(HEX[(b.toInt() shr 4) and 0xF]).append(HEX[b.toInt() and 0xF])
        }

        fun fromHex(hex: String): ByteArray = ByteArray(hex.length / 2) { hex.substring(it * 2, it * 2 + 2).toInt(16).toByte() }
    }
}
