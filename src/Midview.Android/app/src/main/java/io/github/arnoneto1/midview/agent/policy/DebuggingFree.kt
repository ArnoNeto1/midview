package io.github.arnoneto1.midview.agent.policy

import java.io.File

// "Depuração USB": USB debugging stays locked (Lockdown.DEBUGGING_RESTRICTIONS,
// given as restrictions: no developer options nor adb for the student); the
// teacher's command (FREE_PHONE_DEBUGGING) lifts the lock for a while and
// turns USB debugging on itself (usbDebugging(true), Settings.Global's
// adb_enabled as the Device Owner), so the teacher checks the device over the
// cable; when the time is up or at "Travar agora" it is turned off first and
// then locked again (TimedFree: the end kept through restarts, the watchdog
// and the agent's follow locking again). Android also turns adb off when the
// lock goes back on (and wireless debugging with it), so a refused turning off
// is only logged. A debug build (keepsAdb) never has the lock on
// (Lockdown.KEPT_ON_DEBUG_BUILDS): restrictions is then empty, and the end of
// a free time leaves adb as it is, so a test device stays reachable. The
// Midview leaving the phone (leave) turns off what a free time turned on,
// without the lock: debugging as Android leaves it after the lock.
class DebuggingFree(
    file: File,
    restrictions: List<String>,
    isDeviceOwner: () -> Boolean,
    addRestriction: (String) -> Unit,
    clearRestriction: (String) -> Unit,
    private val usbDebugging: (Boolean) -> Unit,
    private val keepsAdb: Boolean = false,
    now: () -> Long = System::currentTimeMillis,
    private val log: (String, Throwable?) -> Unit = { _, _ -> },
) : TimedFree(
    file, KIND, restrictions, isDeviceOwner, addRestriction, clearRestriction, TEXTS, now, log,
    afterFree = { turnOff(keepsAdb, usbDebugging, log) },
    whenFreed = { usbDebugging(true) },
) {
    // "Tirar o Midview": a free time dropped, and the USB debugging it turned
    // on turned off (the lock itself goes with the others). Nothing when
    // none was kept.
    fun leave() {
        if (forget()) turnOff(keepsAdb, usbDebugging, log)
    }

    companion object {
        private const val KIND = "debugging-free"

        const val LOCKED = "depuração USB travada"
        const val NOT_FREED = "o Android não liberou a depuração USB"
        const val NOT_LOCKED = "o Android não travou a depuração USB"

        fun freedText(minutes: Int): String =
            if (minutes == 1) "depuração USB liberada e ligada por 1 minuto" else "depuração USB liberada e ligada por $minutes minutos"

        private val TEXTS = Texts(LOCKED, NOT_FREED, NOT_LOCKED, ::freedText, logName = "USB debugging")

        private fun turnOff(keepsAdb: Boolean, usbDebugging: (Boolean) -> Unit, log: (String, Throwable?) -> Unit) {
            if (keepsAdb) return
            try {
                usbDebugging(false)
            } catch (e: RuntimeException) {
                log("Android did not turn USB debugging off", e)
            }
        }
    }
}
