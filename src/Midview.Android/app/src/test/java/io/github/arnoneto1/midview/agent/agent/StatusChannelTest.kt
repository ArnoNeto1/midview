package io.github.arnoneto1.midview.agent.agent

import android.app.Notification
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Test

class StatusChannelTest {
    // The school's rules on the lock screen's wallpaper stay readable.
    @Test fun theFixedNotificationIsKeptOffTheLockScreen() {
        assertEquals(Notification.VISIBILITY_SECRET, StatusChannel.LOCK_SCREEN)
    }

    // A channel's lock screen visibility cannot change once created.
    @Test fun itHasAChannelOfItsOwn() {
        assertNotEquals(StatusChannel.OLD_ID, StatusChannel.ID)
    }

    // Only the old status channel goes: the teacher's messages, the
    // countdowns, the screen request and the pairing code keep theirs.
    @Test fun onlyTheOldStatusChannelIsDeleted() {
        val existing = listOf("status", "pairing", "teacher", "screen", StatusChannel.ID)
        assertEquals(listOf("status"), StatusChannel.stale(existing))
        assertEquals(emptyList<String>(), StatusChannel.stale(listOf("pairing", StatusChannel.ID)))
    }
}
