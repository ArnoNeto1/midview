package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.policy.Lockdown

// "Otimizar": the ad and bloat apps the school phone hides (the list of
// celulares/otimizar-celulares.ps1), only those the phone has, and the apps
// never hidden whatever the list says. Hiding one of those would break the
// phone: no browser, no web pages inside apps (the WebView), no Google Play
// services, no home screen, no settings, no camera, no agent, or MIUI's
// power, security and notification apps stopped.
object BloatList {
    val PACKAGES: List<String> = listOf(
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

    // Never hidden on any phone. The WebView and the home screen in use, the
    // camera and the rest of the app block's protected apps are added by
    // each phone (neverHide).
    val NEVER_HIDE: Set<String> = setOf(
        "com.android.chrome",
        "com.google.android.gms",
        "com.android.vending",
        "com.android.settings",
        Lockdown.PACKAGE,
        "com.miui.securitycenter",
        "com.miui.powerkeeper",
        "com.xiaomi.joyose",
        "com.miui.notification",
    )

    // The listed apps the phone has, none never hidden, in the list's order.
    fun toHide(installed: Set<String>, neverHide: Set<String>): Set<String> =
        PACKAGES.filter { it in installed && it !in neverHide && it !in NEVER_HIDE }.toSet()

    // Never hidden on this phone: NEVER_HIDE, the app block's protected apps
    // (the home screen, the WebView and the keyboard in use, the agent, the
    // system's dialer, settings and installer) and the phone's own cameras.
    fun neverHide(protected: Set<String>, cameras: Set<String>): Set<String> = NEVER_HIDE + protected + cameras
}
