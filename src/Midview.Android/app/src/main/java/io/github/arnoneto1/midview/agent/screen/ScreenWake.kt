package io.github.arnoneto1.midview.agent.screen

import android.content.Context
import android.os.PowerManager

// "Ver a tela" and "Controlar" on a phone whose screen went dark: the screen
// lights up, on the lock screen if there is one, so the teacher sees it. It
// then goes dark again after the phone's own timeout, as if the student had
// touched it. The wake lock lets go by itself after a moment.
object ScreenWake {
    private const val HOLD_MILLIS = 3_000L

    fun wake(context: Context) {
        val power = context.getSystemService(PowerManager::class.java) ?: return
        if (power.isInteractive) return
        // ACQUIRE_CAUSES_WAKEUP is deprecated for apps that can show an
        // activity instead; here nothing is shown, the screen only lights up.
        @Suppress("DEPRECATION")
        val lock = power.newWakeLock(PowerManager.SCREEN_BRIGHT_WAKE_LOCK or PowerManager.ACQUIRE_CAUSES_WAKEUP, "midview:ver-a-tela")
        lock.setReferenceCounted(false)
        lock.acquire(HOLD_MILLIS)
    }
}
