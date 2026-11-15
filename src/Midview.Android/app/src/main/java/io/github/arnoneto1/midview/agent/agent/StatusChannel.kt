package io.github.arnoneto1.midview.agent.agent

import android.app.Notification

// The channels of the two notifications Android requires while the agent
// runs ("Celular da escola, gerenciado pelo Midview") and while it shares the
// screen ("O professor pode ver a tela..."): minimal importance, so they show
// no icon on the status bar, stay off the lock screen (where the school's
// rules on the wallpaper are read) and sit folded at the bottom of the shade
// (the school's choice, 2026-11-15: nobody needs them). The teacher's
// messages, the countdowns, the screen request and the leaving notice keep
// their own channels. The notifications also ask for secret visibility:
// Android 10 resets the lock screen visibility an app gives its channel
// (seen on the school's Multilaser M10). A channel's importance cannot be
// lowered once it exists, so these have ids of their own and the ones before
// them are deleted.
internal object StatusChannel {
    const val ID = "status-quiet"
    const val SCREEN_ID = "screen-quiet"
    val OLD_IDS = setOf("status", "status-unlocked", "screen")
    const val LOCK_SCREEN = Notification.VISIBILITY_SECRET

    // Of the app's channels, the ones to delete: only the old status and screen channels.
    fun stale(existing: Collection<String>): List<String> = existing.filter { it in OLD_IDS }
}
