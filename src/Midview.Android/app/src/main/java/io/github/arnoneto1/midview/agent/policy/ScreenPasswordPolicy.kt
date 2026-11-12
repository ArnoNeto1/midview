package io.github.arnoneto1.midview.agent.policy

// The reset token (DevicePolicyManager.setResetPasswordToken) as Android
// has it: none the agent can use, given but waiting for the phone to be
// unlocked with its PIN once ("pendente"), or active (the agent can set or
// remove the PIN at any time, also while the phone is locked).
enum class ResetToken { NONE, PENDING, ACTIVE }

// What the agent does about the lock screen at a check.
enum class ScreenPasswordAction {
    NOTHING,

    // Give Android the reset token (active at once on a phone with no PIN).
    SET_TOKEN,

    // The token waits for the next unlock; nothing can be done before it.
    WAIT_FOR_UNLOCK,

    // No teacher's PIN, and the student put a lock on: remove it.
    CLEAR,

    // The teacher's PIN was changed or removed by someone else: put it back.
    RESTORE,
}

// The lock screen's rules, kept apart so they are tested on the JVM:
// - with a teacher's PIN, the phone has that PIN; any other change (another
//   PIN, a password, a pattern, no lock) is undone with the reset token;
// - with none, the phone has no lock: one the student sets is removed (the
//   user's first decision, "apagar sozinho", kept for when the teacher has
//   set no PIN, so a student can never lock the school phone for themself);
// - with the teacher's PIN unknown (its file was damaged), the lock is left
//   as it is: removing it could remove the teacher's PIN.
// Nothing is done on a phone where the app is not the Device Owner.
object ScreenPasswordPolicy {
    // The token's state from what the agent kept and what Android says. A
    // token given while the phone had a PIN waits for the next unlock, and
    // Android forgets a waiting token at a restart, so one given on an
    // earlier boot and still not active is given again.
    fun token(stored: Boolean, active: Boolean, givenThisBoot: Boolean, secure: Boolean): ResetToken = when {
        !stored -> ResetToken.NONE
        active -> ResetToken.ACTIVE
        secure && givenThisBoot -> ResetToken.PENDING
        else -> ResetToken.NONE
    }

    // secure: the phone has any lock (PIN, password, pattern);
    // changedByOther: the lock changed since the agent last set it, and not
    // by the agent.
    fun decide(
        deviceOwner: Boolean,
        secure: Boolean,
        token: ResetToken,
        teacherPin: String?,
        changedByOther: Boolean,
        pinUnknown: Boolean = false,
    ): ScreenPasswordAction = when {
        !deviceOwner -> ScreenPasswordAction.NOTHING
        token == ResetToken.NONE -> ScreenPasswordAction.SET_TOKEN
        token == ResetToken.PENDING -> ScreenPasswordAction.WAIT_FOR_UNLOCK
        teacherPin == null && pinUnknown -> ScreenPasswordAction.NOTHING
        teacherPin == null -> if (secure) ScreenPasswordAction.CLEAR else ScreenPasswordAction.NOTHING
        !secure || changedByOther -> ScreenPasswordAction.RESTORE
        else -> ScreenPasswordAction.NOTHING
    }
}

// The agent's own changes of the lock, so Android's "password changed" for
// them is not taken for the student's (and answered with another change,
// in a loop). Each change the agent made owes one report: a report while a
// change is under way, or the first one within WINDOW_MILLIS of a change
// that owes one, is the agent's, and so is the same report repeated within
// REPEAT_MILLIS (some phones report one change more than once). Any other
// report is someone else's, also one that comes a second or two after the
// agent's own: a student changing the PIN right after the agent put it back
// is not mistaken for the agent. A change Android refused owes nothing.
class OwnChanges(private val clock: () -> Long) {
    private var changing = 0
    private var reportedWhileChanging = false
    private var owed = 0
    private var owedUntil = Long.MIN_VALUE
    private var repeatUntil = Long.MIN_VALUE

    @Synchronized
    fun starting() {
        changing++
    }

    // changed: Android took the change (a report for it is to come).
    @Synchronized
    fun finished(changed: Boolean = true) {
        if (changing > 0) changing--
        if (!changed) return
        val now = clock()
        if (reportedWhileChanging) {
            // Its report came already: only repeats of it are left.
            reportedWhileChanging = false
            repeatUntil = now + REPEAT_MILLIS
        } else {
            owed++
            owedUntil = now + WINDOW_MILLIS
        }
    }

    // True when a "password changed" now is the agent's own; each call is
    // one report.
    @Synchronized
    fun isOwn(): Boolean {
        val now = clock()
        if (changing > 0) {
            reportedWhileChanging = true
            return true
        }
        if (owed > 0 && now <= owedUntil) {
            owed--
            repeatUntil = now + REPEAT_MILLIS
            return true
        }
        owed = 0
        return now <= repeatUntil
    }

    companion object {
        const val WINDOW_MILLIS = 5_000L

        // Far less than anyone needs to type the old PIN and a new one twice.
        const val REPEAT_MILLIS = 1_500L
    }
}
