package io.github.arnoneto1.midview.agent.optimize

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class BloatListTest {
    // The plan's list (celulares/otimizar-celulares.ps1), in its order.
    private val plan = listOf(
        "com.miui.msa.global",
        "com.miui.analytics",
        "com.xiaomi.mipicks",
        "com.xiaomi.glgm",
        "com.miui.yellowpage",
        "com.facebook.services",
        "com.facebook.system",
        "com.facebook.appmanager",
        "com.amazon.appmanager",
        "com.miui.android.fashiongallery",
        "com.xiaomi.discover",
        "com.miui.miservice",
        "com.miui.bugreport",
        "com.google.android.projection.gearhead",
        "com.google.android.apps.subscriptions.red",
    )

    @Test fun theListIsThePlansFifteenApps() {
        assertEquals(plan, BloatList.PACKAGES)
    }

    @Test fun neverHiddenIsThePlansList() {
        assertEquals(
            setOf(
                "com.android.chrome",
                "com.google.android.gms",
                "com.android.vending",
                "com.android.settings",
                "io.github.arnoneto1.midview.agent",
                "com.miui.securitycenter",
                "com.miui.powerkeeper",
                "com.xiaomi.joyose",
                "com.miui.notification",
            ),
            BloatList.NEVER_HIDE,
        )
    }

    @Test fun onlyWhatThePhoneHasIsHidden() {
        assertEquals(
            setOf("com.miui.msa.global", "com.facebook.system"),
            BloatList.toHide(setOf("com.miui.msa.global", "com.facebook.system", "com.whatsapp", "com.android.chrome"), emptySet()),
        )
        assertEquals(emptySet<String>(), BloatList.toHide(emptySet(), emptySet()))
    }

    @Test fun neverHiddenWinsOverTheList() {
        assertEquals(
            plan.toSet() - "com.miui.yellowpage" - "com.xiaomi.discover",
            BloatList.toHide(plan.toSet(), neverHide = setOf("com.miui.yellowpage", "com.xiaomi.discover")),
        )
    }

    @Test fun whatIsHiddenKeepsTheListsOrder() {
        assertEquals(plan, BloatList.toHide(plan.reversed().toSet(), emptySet()).toList())
    }

    @Test fun thePhonesNeverHiddenAddsTheProtectedAppsAndTheCameras() {
        val never = BloatList.neverHide(
            protected = setOf("com.miui.home", "com.google.android.webview", "io.github.arnoneto1.midview.agent"),
            cameras = setOf("com.android.camera"),
        )
        assertTrue(never.containsAll(BloatList.NEVER_HIDE))
        assertTrue(never.containsAll(setOf("com.miui.home", "com.google.android.webview", "com.android.camera")))
    }

    // On a phone where a listed package is the camera or something the app
    // block protects (the home screen in use, say), it stays.
    @Test fun aListedAppThePhoneNeedsIsKept() {
        val never = BloatList.neverHide(protected = setOf("com.xiaomi.glgm"), cameras = setOf("com.miui.android.fashiongallery"))
        val hidden = BloatList.toHide(plan.toSet(), never)
        assertFalse("com.xiaomi.glgm" in hidden)
        assertFalse("com.miui.android.fashiongallery" in hidden)
        assertEquals(13, hidden.size)
    }

    // The Multilaser M10 tablet: Google Photos is its only gallery and stays.
    @Test fun theOnlyGalleryIsNeverHidden() {
        val never = BloatList.neverHide(emptySet(), emptySet(), galleries = setOf("com.google.android.apps.photos"))
        assertTrue("com.google.android.apps.photos" in never)
    }

    // A Xiaomi: MIUI's Galeria beside Google Photos, which the list may hide.
    @Test fun aSecondGalleryKeepsNeither() {
        val never = BloatList.neverHide(
            emptySet(), emptySet(), galleries = setOf("com.miui.gallery", "com.google.android.apps.photos"),
        )
        assertFalse("com.google.android.apps.photos" in never)
        assertFalse("com.miui.gallery" in never)
    }

    @Test fun theListHidesNothingThatIsNeverHidden() {
        assertTrue(BloatList.PACKAGES.none { it in BloatList.NEVER_HIDE })
    }
}
