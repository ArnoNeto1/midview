package io.github.arnoneto1.midview.agent.policy

import java.io.File

// "Liberar Wi-Fi e modo avião": the Wi-Fi and airplane mode locks
// (Lockdown.networkRestrictions, given as restrictions) lifted for a while, by the teacher's
// command or the admin password typed on the phone (AdminUnlock), and put
// back when the time is up (TimedFree: the end kept through restarts, the
// watchdog and the agent's follow locking again). Locking again after a free
// time brings the phone back to the school's Wi-Fi (rejoinWifi, SchoolWifi):
// the student may have turned it off or joined another network meanwhile;
// while locked, every check keeps the Wi-Fi on (keepWifiOn: before Android
// 13 nothing else stops the student from turning it off). Both run before
// the locks, which from Android 13 would keep a Wi-Fi the student turned
// off, off.
class NetworkFree(
    file: File,
    restrictions: List<String>,
    isDeviceOwner: () -> Boolean,
    addRestriction: (String) -> Unit,
    clearRestriction: (String) -> Unit,
    now: () -> Long = System::currentTimeMillis,
    log: (String, Throwable?) -> Unit = { _, _ -> },
    keepWifiOn: () -> Unit = {},
    rejoinWifi: () -> Unit = {},
) : TimedFree(
    file, KIND, restrictions, isDeviceOwner, addRestriction, clearRestriction, TEXTS, now, log,
    whileLocked = keepWifiOn, afterFree = rejoinWifi,
) {
    companion object {
        const val MINUTE_MILLIS = TimedFree.MINUTE_MILLIS
        const val MAX_MILLIS = TimedFree.MAX_MILLIS
        private const val KIND = "network-free"

        const val LOCKED = "Wi-Fi e modo avião travados"
        const val NOT_FREED = "o Android não liberou o Wi-Fi e o modo avião"
        const val NOT_LOCKED = "o Android não travou o Wi-Fi e o modo avião"
        const val NOT_PREPARED = TimedFree.NOT_PREPARED
        const val INVALID_REQUEST = TimedFree.INVALID_REQUEST

        fun freedText(minutes: Int): String =
            if (minutes == 1) "Wi-Fi e modo avião liberados por 1 minuto" else "Wi-Fi e modo avião liberados por $minutes minutos"

        private val TEXTS = Texts(LOCKED, NOT_FREED, NOT_LOCKED, ::freedText, logName = "the Wi-Fi and airplane mode")
    }
}
