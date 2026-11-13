package io.github.arnoneto1.midview.agent.agent

import android.app.Notification

// The channel of the agent's fixed notification ("Celular da escola,
// gerenciado pelo Midview"): kept off the lock screen, where the school's
// rules on the wallpaper are read, and shown as before once the device is
// unlocked. Only this one: the teacher's messages, the countdowns, the
// screen request and the leaving notice keep their own channels. Android
// fixes a channel's lock screen visibility when the channel is created, so
// this one has an id of its own and the one before it ("status", shown on
// the lock screen) is deleted.
internal object StatusChannel {
    const val ID = "status-unlocked"
    const val OLD_ID = "status"
    const val LOCK_SCREEN = Notification.VISIBILITY_SECRET

    // Of the app's channels, the ones to delete: only the old status channel.
    fun stale(existing: Collection<String>): List<String> = existing.filter { it == OLD_ID }
}
