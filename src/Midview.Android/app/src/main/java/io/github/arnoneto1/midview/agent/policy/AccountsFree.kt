package io.github.arnoneto1.midview.agent.policy

import java.io.File

// "Contas do aparelho": the device's accounts stay locked
// (Lockdown.ACCOUNT_RESTRICTIONS, given as restrictions: no account added nor
// removed, so no Google account of the student's opens the Play Store or
// ties the device to them after a reset); the teacher's command
// (FREE_PHONE_ACCOUNTS) lifts the lock for a while, for the school's own
// Google account to go on the device after it was prepared (the cable's
// set-device-owner refuses a device with an account), and it comes back when
// the time is up or at "Travar agora" (TimedFree: the end kept through
// restarts, the watchdog and the agent's follow locking again).
class AccountsFree(
    file: File,
    restrictions: List<String>,
    isDeviceOwner: () -> Boolean,
    addRestriction: (String) -> Unit,
    clearRestriction: (String) -> Unit,
    now: () -> Long = System::currentTimeMillis,
    log: (String, Throwable?) -> Unit = { _, _ -> },
) : TimedFree(file, KIND, restrictions, isDeviceOwner, addRestriction, clearRestriction, TEXTS, now, log) {
    companion object {
        private const val KIND = "accounts-free"

        const val LOCKED = "contas do aparelho travadas"
        const val NOT_FREED = "o Android não liberou as contas do aparelho"
        const val NOT_LOCKED = "o Android não travou as contas do aparelho"

        fun freedText(minutes: Int): String =
            if (minutes == 1) "contas do aparelho liberadas por 1 minuto" else "contas do aparelho liberadas por $minutes minutos"

        private val TEXTS = Texts(LOCKED, NOT_FREED, NOT_LOCKED, ::freedText, logName = "the device accounts")
    }
}
