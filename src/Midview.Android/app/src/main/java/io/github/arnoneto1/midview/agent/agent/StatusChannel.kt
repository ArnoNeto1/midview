package io.github.arnoneto1.midview.agent.agent

import android.app.Notification

// The channel of the agent's fixed notification ("Celular da escola,
// gerenciado pelo Midview"): kept off the lock screen, where the school's
// rules on the wallpaper are read, and shown as before once the device is
// unlocked. Only this one: the teacher's messages, the countdowns, the
// screen request and the leaving notice keep their own channels. What hides
// it is the notification's own visibility (secret): Android 10 resets the
// lock screen visibility an app gives its channel (seen on the school's
// Multilaser M10), and fixes it once the channel exists. The channel asks
// for secret all the same, under an id of its own, and the one before it
// ("status") is deleted.
internal object StatusChannel {
    const val ID = "status-unlocked"
    const val OLD_ID = "status"
    const val LOCK_SCREEN = Notification.VISIBILITY_SECRET

    // Of the app's channels, the ones to delete: only the old status channel.
    fun stale(existing: Collection<String>): List<String> = existing.filter { it == OLD_ID }
}
