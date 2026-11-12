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
//   set no PIN, so a student can never lock the school phone for themself).
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
    ): ScreenPasswordAction = when {
        !deviceOwner -> ScreenPasswordAction.NOTHING
        token == ResetToken.NONE -> ScreenPasswordAction.SET_TOKEN
        token == ResetToken.PENDING -> ScreenPasswordAction.WAIT_FOR_UNLOCK
        teacherPin == null -> if (secure) ScreenPasswordAction.CLEAR else ScreenPasswordAction.NOTHING
        !secure || changedByOther -> ScreenPasswordAction.RESTORE
        else -> ScreenPasswordAction.NOTHING
    }
}

// The agent's own changes of the lock, so Android's "password changed" for
// them is not taken for the student's (and answered with another change,
// in a loop). Every report while the agent changes the lock, or up to
// WINDOW_MILLIS after, is the agent's: some phones report one change more
// than once.
class OwnChanges(private val clock: () -> Long) {
    private var changing = 0
    private var until = Long.MIN_VALUE

    @Synchronized
    fun starting() {
        changing++
    }

    @Synchronized
    fun finished() {
        if (changing > 0) changing--
        until = clock() + WINDOW_MILLIS
    }

    // True when a "password changed" now is the agent's own.
    @Synchronized
    fun isOwn(): Boolean = changing > 0 || clock() <= until

    companion object {
        const val WINDOW_MILLIS = 5_000L
    }
}
