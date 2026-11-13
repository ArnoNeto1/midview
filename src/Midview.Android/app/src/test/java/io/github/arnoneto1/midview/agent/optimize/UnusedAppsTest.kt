package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.protocol.PhoneAppPolicy
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class UnusedAppsTest {
    // The list the teacher saw and approved, by group.
    @Test fun theListIsTheApprovedOne() {
        assertEquals(
            listOf(
                "com.king.candycrushsaga", "com.block.juggle", "com.ew.solitaire", "com.nebula.mahjongtile",
                "com.vitastudio.mahjong", "com.fugo.wow", "com.oakever.arrows", "com.oakever.tiletrip",
            ),
            UnusedApps.GAMES,
        )
        assertEquals(
            listOf(
                "com.zhiliaoapp.musically", "com.zhiliaoapp.musically.go", "com.kwai.video", "kwai.lite.video",
                "com.facebook.katana", "com.linkedin.android", "com.lemon.lvoverseas", "com.google.android.apps.tachyon",
                "com.mi.global.bbs",
            ),
            UnusedApps.SOCIAL,
        )
        assertEquals(
            listOf(
                "com.alibaba.aliexpresshd", "com.amazon.mShop.android.shopping", "com.amazon.avod.thirdpartyclient",
                "com.amazon.mp3", "com.netflix.mediaclient", "com.spotify.music", "com.google.android.apps.youtube.music",
                "com.booking", "com.google.android.videos", "com.google.android.apps.magazines",
                "com.google.android.apps.podcasts", "com.google.android.apps.walletnfcrel",
            ),
            UnusedApps.SHOPPING,
        )
        assertEquals(
            listOf(
                "com.xiaomi.smarthome", "com.xiaomi.midrop", "com.xiaomi.scanner", "com.microsoft.skydrive",
                "com.google.android.apps.photos", "com.tencent.igxiaomi", "com.google.android.gm",
                "com.google.android.calendar", "com.google.android.apps.googleassistant", "com.google.android.apps.maps",
                "com.miui.fm", "com.google.android.youtube",
            ),
            UnusedApps.EXTRAS,
        )
        assertEquals(41, UnusedApps.PACKAGES.size)
        assertEquals(UnusedApps.PACKAGES.size, UnusedApps.PACKAGES.toSet().size)
        assertTrue(UnusedApps.PACKAGES.all(PhoneAppPolicy::isValidPackageName))
    }

    // Nothing the phone needs or the school uses is ever on the list.
    @Test fun theListAndTheProtectedAppsAreDisjoint() {
        assertEquals(emptySet<String>(), UnusedApps.PACKAGES.toSet() intersect UnusedApps.PROTECTED)
        assertEquals(emptySet<String>(), UnusedApps.PACKAGES.toSet() intersect BloatList.NEVER_HIDE)
        // Hidden by BloatList, never uninstalled.
        assertEquals(emptySet<String>(), UnusedApps.PACKAGES.toSet() intersect BloatList.PACKAGES.toSet())
    }

    @Test fun theProtectedAppsAreTheEssentialOnes() {
        val essential = setOf(
            "io.github.arnoneto1.midview.agent", "com.miui.home", "com.google.android.inputmethod.latin",
            "com.google.android.webview", "com.google.android.gms", "com.android.vending", "com.android.settings",
            "com.android.chrome", "com.android.camera", "com.google.android.dialer", "com.miui.calculator",
            "com.miui.gallery", "com.android.deskclock", "com.mi.android.globalFileexplorer", "com.miui.notes",
            "com.miui.weather2", "com.android.soundrecorder", "cn.wps.xiaomi.abroad.lite",
            "com.google.android.apps.docs.editors.docs",
        )
        assertTrue((essential - UnusedApps.PROTECTED).toString(), UnusedApps.PROTECTED.containsAll(essential))
    }

    @Test fun onlyWhatThePhoneHasAndMayLoseIsRemovedInTheListsOrder() {
        val installed = setOf("com.google.android.youtube", "com.booking", "com.king.candycrushsaga", "com.whatsapp", "com.android.chrome")
        assertEquals(
            listOf("com.king.candycrushsaga", "com.booking", "com.google.android.youtube"),
            UnusedApps.toRemove(installed, neverHide = emptySet()),
        )
        // The phone's own never-hidden apps (the camera, the home screen in use) win.
        assertEquals(listOf("com.king.candycrushsaga", "com.booking"), UnusedApps.toRemove(installed, setOf("com.google.android.youtube")))
        assertEquals(emptyList<String>(), UnusedApps.toRemove(emptySet(), emptySet()))
    }
}
