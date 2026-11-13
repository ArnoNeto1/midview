package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.policy.Lockdown

// "Otimizar": the apps the school does not use, which come on the phones
// (games, social networks and short videos, shopping and streaming, and
// Xiaomi's and Google's extras), as the teacher saw and approved the list.
// The ones the phone has are uninstalled for good by the Device Owner
// (silently); one that came with the phone's system (a system app, or an
// update of one) cannot be uninstalled by a Device Owner, so it is hidden
// instead, like BloatList's. A package the phone does not have is skipped.
// Nothing else is ever removed: this list is a vetted path of its own, apart
// from the teacher's "Desinstalar programa" (which removes only what the
// Midview installed), and no app of PROTECTED nor of the phone's never-hidden
// apps (BloatList.neverHide) is ever touched, whatever the list says.
object UnusedApps {
    val GAMES: List<String> = listOf(
        "com.king.candycrushsaga",
        "com.block.juggle",
        "com.ew.solitaire",
        "com.nebula.mahjongtile",
        "com.vitastudio.mahjong",
        "com.fugo.wow",
        "com.oakever.arrows",
        "com.oakever.tiletrip",
    )

    // Social networks and short videos.
    val SOCIAL: List<String> = listOf(
        "com.zhiliaoapp.musically",
        "com.zhiliaoapp.musically.go",
        "com.kwai.video",
        "kwai.lite.video",
        "com.facebook.katana",
        "com.linkedin.android",
        "com.lemon.lvoverseas",
        "com.google.android.apps.tachyon",
        "com.mi.global.bbs",
    )

    // Shopping and streaming.
    val SHOPPING: List<String> = listOf(
        "com.alibaba.aliexpresshd",
        "com.amazon.mShop.android.shopping",
        "com.amazon.avod.thirdpartyclient",
        "com.amazon.mp3",
        "com.netflix.mediaclient",
        "com.spotify.music",
        "com.google.android.apps.youtube.music",
        "com.booking",
        "com.google.android.videos",
        "com.google.android.apps.magazines",
        "com.google.android.apps.podcasts",
        "com.google.android.apps.walletnfcrel",
    )

    // Xiaomi's and Google's extras.
    val EXTRAS: List<String> = listOf(
        "com.xiaomi.smarthome",
        "com.xiaomi.midrop",
        "com.xiaomi.scanner",
        "com.microsoft.skydrive",
        "com.google.android.apps.photos",
        "com.tencent.igxiaomi",
        "com.google.android.gm",
        "com.google.android.calendar",
        "com.google.android.apps.googleassistant",
        "com.google.android.apps.maps",
        "com.miui.fm",
        "com.google.android.youtube",
    )

    val PACKAGES: List<String> = GAMES + SOCIAL + SHOPPING + EXTRAS

    // Never removed nor hidden on any phone, on top of BloatList.NEVER_HIDE
    // and each phone's own never-hidden apps: the agent, the home screen, the
    // keyboard, the WebView, Google Play services and the Play Store, the
    // settings, Chrome, the camera, the phone and messages, the installer,
    // and the school's everyday apps (Calculadora, Galeria, Relógio,
    // Arquivos, Notas, Clima, Gravador, WPS and Google's Docs).
    val PROTECTED: Set<String> = setOf(
        Lockdown.PACKAGE,
        "com.miui.home",
        "com.mi.android.globallauncher",
        "com.google.android.inputmethod.latin",
        "com.android.inputmethod.latin",
        "com.google.android.webview",
        "com.android.webview",
        "com.google.android.gms",
        "com.google.android.gsf",
        "com.android.vending",
        "com.android.settings",
        "com.android.chrome",
        "com.android.camera",
        "com.android.camera2",
        "com.google.android.dialer",
        "com.android.dialer",
        "com.android.contacts",
        "com.google.android.contacts",
        "com.android.phone",
        "com.google.android.apps.messaging",
        "com.android.mms",
        "com.google.android.packageinstaller",
        "com.android.packageinstaller",
        "com.miui.calculator",
        "com.google.android.calculator",
        "com.miui.gallery",
        "com.android.deskclock",
        "com.google.android.deskclock",
        "com.mi.android.globalFileexplorer",
        "com.google.android.apps.nbu.files",
        "com.google.android.documentsui",
        "com.android.documentsui",
        "com.miui.notes",
        "com.miui.weather2",
        "com.android.soundrecorder",
        "com.miui.soundrecorder",
        "cn.wps.xiaomi.abroad.lite",
        "cn.wps.moffice_eng",
        "com.google.android.apps.docs",
        "com.google.android.apps.docs.editors.docs",
        "com.google.android.apps.docs.editors.sheets",
        "com.google.android.apps.docs.editors.slides",
    )

    // The listed apps the phone has, none protected nor never hidden on this
    // phone, in the list's order.
    fun toRemove(installed: Set<String>, neverHide: Set<String>): List<String> =
        PACKAGES.filter { it in installed && it !in neverHide && it !in PROTECTED && it !in BloatList.NEVER_HIDE }
}
