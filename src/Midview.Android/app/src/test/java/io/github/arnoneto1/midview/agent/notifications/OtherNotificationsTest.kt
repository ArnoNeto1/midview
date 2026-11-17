package io.github.arnoneto1.midview.agent.notifications

import io.github.arnoneto1.midview.agent.policy.Lockdown
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

// Only the Midview's notifications on a school device: which are taken away
// and which apps are denied them at the source.
class OtherNotificationsTest {
    @Test fun anotherAppsNotificationTheStudentCouldSwipeAwayIsRemoved() {
        assertTrue(OtherNotifications.removes("com.whatsapp", clearable = true, foreground = false))
        assertTrue(OtherNotifications.removes("com.google.android.gms", clearable = true, foreground = false))
    }

    @Test fun theMidviewsOwnStay() {
        assertFalse(OtherNotifications.removes(Lockdown.PACKAGE, clearable = true, foreground = false))
    }

    // The Defesa Civil's alerts come by cell broadcast, on any Android.
    @Test fun emergencyAlertsStay() {
        for (pkg in listOf(
            "com.android.cellbroadcastreceiver",
            "com.android.cellbroadcastreceiver.module",
            "com.google.android.cellbroadcastreceiver",
        )) {
            assertFalse(pkg, OtherNotifications.removes(pkg, clearable = true, foreground = false))
            assertFalse(pkg, OtherNotifications.denies(pkg, emptySet()))
        }
    }

    @Test fun androidsOwnStay() {
        assertFalse(OtherNotifications.removes("android", clearable = true, foreground = false))
        // Android's "Atualizado pelo seu administrador" after a Midview update goes too.
        assertTrue(OtherNotifications.removes("android", clearable = true, foreground = false, tag = Lockdown.PACKAGE))
        assertFalse(OtherNotifications.removes("android", clearable = true, foreground = false, tag = "other"))
        assertFalse(OtherNotifications.removes("com.android.systemui", clearable = true, foreground = false))
    }

    // Its "Conectar" joins an open network with the Wi-Fi locked; the
    // voucher's "Fazer login na rede" comes on another channel and stays.
    @Test fun androidsOpenNetworkOfferGoes() {
        assertTrue(OtherNotifications.removes("android", clearable = true, foreground = false, channel = "NETWORK_AVAILABLE"))
        assertFalse(OtherNotifications.removes("android", clearable = true, foreground = false, channel = "NETWORK_ALERTS"))
        assertFalse(OtherNotifications.removes("com.android.systemui", clearable = true, foreground = false, channel = "NETWORK_AVAILABLE"))
    }

    // "Excluído pelo seu administrador", one per app the optimization took
    // away, tagged with that app's package; the untagged admin alerts stay.
    @Test fun androidsAppRemovedByTheAdminGoes() {
        assertTrue(OtherNotifications.removes("android", clearable = true, foreground = false, tag = "com.google.android.apps.photos", channel = "DEVICE_ADMIN_ALERTS"))
        assertFalse(OtherNotifications.removes("android", clearable = true, foreground = false, tag = null, channel = "DEVICE_ADMIN_ALERTS"))
        assertFalse(OtherNotifications.removes("android", clearable = true, foreground = false, tag = "ranker_group", channel = "DEVICE_ADMIN_ALERTS"))
        assertFalse(OtherNotifications.removes("android", clearable = true, foreground = false, tag = "com.google.android.apps.photos", channel = "USB"))
    }

    // Android ignores a listener's cancel of these: never asked for.
    @Test fun ongoingAndForegroundOnesAreLeftAlone() {
        assertFalse(OtherNotifications.removes("com.spotify.music", clearable = false, foreground = false))
        assertFalse(OtherNotifications.removes("com.spotify.music", clearable = true, foreground = true))
    }

    @Test fun everyOtherAppIsDeniedButTheDialer() {
        val calls = setOf("com.google.android.dialer")

        assertTrue(OtherNotifications.denies("com.whatsapp", calls))
        assertTrue(OtherNotifications.denies("com.android.chrome", calls))
        assertFalse(OtherNotifications.denies("com.google.android.dialer", calls))
        assertFalse(OtherNotifications.denies(Lockdown.PACKAGE, calls))
    }
}
