package io.github.arnoneto1.midview.agent.optimize

import io.github.arnoneto1.midview.agent.home.Launcher3Home
import io.github.arnoneto1.midview.agent.policy.Lockdown

// The apps the cable's "Midview otimizado" removes ("pm uninstall --user 0"
// in src/Midview.Master/PhoneCable.Lighten.cs, Lighten), so that "Otimizar"
// over the network leaves a device the way the cable does: REMOVED on every
// device, then TABLET or PHONE by the kind the cable saved (CableSetup.isTablet;
// a device never prepared by cable is a phone, as it is named). The lists are
// copies of RemovedApps, TabletRemovedApps and PhoneRemovedApps there, where
// the reasons and the dates the school chose them are written: change both
// together (tests/Midview.Master.Tests/CableAppsListTests.cs fails while
// they differ).
//
// A Device Owner cannot "pm uninstall --user 0" an app that came with the
// system (nor an update of one): such an app is hidden instead
// (setApplicationHidden), which, like the cable's removal for the user, takes
// it off the device's screens and keeps it from running; an app the student
// installed is uninstalled for good (SilentUninstaller), as UnusedApps' are.
// One the cable removed already is not on the device any more and is
// skipped. What else the cable does a Device Owner cannot do, and it is
// skipped here, since the cable setup did it already: the animations off and
// MIUI's home search gestures off (global and system settings, which need
// WRITE_SECURE_SETTINGS), and the heavy apps kept from running in the
// background ("cmd appops", which needs the shell's MANAGE_APP_OPS_MODES).
object CableApps {
    val REMOVED: List<String> = listOf(
        "android.autoinstalls.config.Xiaomi.model",
        "com.android.apps.tag",
        "com.android.bluetoothmidiservice",
        "com.android.calllogbackup",
        "com.android.dreams.basic",
        "com.android.dreams.phototable",
        "com.android.egg",
        "com.android.hotwordenrollment.okgoogle",
        "com.android.hotwordenrollment.xgoogle",
        "com.android.providers.partnerbookmarks",
        "com.android.theme.font.notoserifsource",
        "com.android.traceur",
        "com.android.wallpaper.livepicker",
        "com.android.wallpaperbackup",
        "com.bsp.catchlog",
        "com.fido.asm",
        "com.fingerprints.sensortesttool",
        "com.google.android.adservices.api",
        "com.google.android.apps.messaging",
        "com.google.android.apps.restore",
        "com.google.android.as",
        "com.google.android.federatedcompute",
        "com.google.android.feedback",
        "com.google.android.gms.location.history",
        "com.google.android.gms.supervision",
        "com.google.android.googlequicksearchbox",
        "com.google.android.ondevicepersonalization.services",
        "com.google.android.setupwizard",
        "com.google.android.onetimeinitializer",
        "com.google.android.printservice.recommendation",
        "com.google.mainline.adservices",
        "com.longcheertel.AutoTest",
        "com.longcheertel.cit",
        "com.longcheertel.sarauth",
        "com.mi.global.shop",
        "com.mi.globallayout",
        "com.mi.globalminusscreen",
        "com.miui.audiomonitor",
        "com.miui.backup",
        "com.miui.cleaner",
        "com.miui.cloudbackup",
        "com.miui.daemon",
        "com.miui.face",
        "com.miui.face.overlay.miui",
        "com.miui.fmservice",
        "com.miui.micloudsync",
        "com.miui.phrase",
        "com.miuix.editor",
        "com.modemdebug",
        "com.qti.qualcomm.deviceinfo",
        "com.qti.xdivert",
        "com.qualcomm.embms",
        "com.qualcomm.qti.devicestatisticsservice",
        "com.qualcomm.qti.performancemode",
        "com.qualcomm.qti.powersavemode",
        "com.qualcomm.qti.qms.service.trustzoneaccess",
        "com.qualcomm.qti.ridemodeaudio",
        "com.quicinc.voice.activation",
        "com.silead.fingerprint",
        "com.tencent.soter.soterserver",
        "com.wdstechnology.android.kryten",
        "com.xiaomi.barrage",
        "com.xiaomi.cameratools",
        "com.xiaomi.mtb",
        "com.xiaomi.payment",
        "com.xiaomi.simactivate.service",
        "com.xiaomi.xmsfkeeper",
        "de.telekom.tsc",
        "org.ifaa.aidl.manager",
        "org.mipay.android.manager",
    )

    // Only on a tablet (the school's Multilaser M10).
    val TABLET: List<String> = listOf(
        "com.android.dialer",
        "com.android.modemnotifier",
        "com.android.se",
        "com.android.sharedstoragebackup",
        "com.android.smspush",
        "com.android.theme.color.black",
        "com.android.theme.color.cinnamon",
        "com.android.theme.color.green",
        "com.android.theme.color.ocean",
        "com.android.theme.color.orchid",
        "com.android.theme.color.purple",
        "com.android.theme.color.space",
        "com.android.theme.icon.roundedrect",
        "com.android.theme.icon.square",
        "com.android.theme.icon.squircle",
        "com.android.theme.icon.teardrop",
        "com.android.theme.icon_pack.circular.android",
        "com.android.theme.icon_pack.circular.launcher",
        "com.android.theme.icon_pack.circular.settings",
        "com.android.theme.icon_pack.circular.systemui",
        "com.android.theme.icon_pack.circular.themepicker",
        "com.android.theme.icon_pack.filled.android",
        "com.android.theme.icon_pack.filled.launcher",
        "com.android.theme.icon_pack.filled.settings",
        "com.android.theme.icon_pack.filled.systemui",
        "com.android.theme.icon_pack.filled.themepicker",
        "com.android.theme.icon_pack.rounded.android",
        "com.android.theme.icon_pack.rounded.launcher",
        "com.android.theme.icon_pack.rounded.settings",
        "com.android.theme.icon_pack.rounded.systemui",
        "com.cghs.stresstest",
        "com.google.android.apps.wellbeing",
        "com.google.android.gmsintegration",
        "com.google.android.keep",
        "com.google.android.partnersetup",
        "com.google.android.syncadapters.contacts",
        "com.google.android.tag",
        "com.sprd.autoslt",
        "com.sprd.cameracalibration",
        "com.sprd.cameraipcontrol",
        "com.sprd.engineermode",
        "com.sprd.logmanager",
        "com.sprd.quickcamera",
        "com.sprd.uasetting",
        "com.sprd.uplmnsettings",
        "com.sprd.validationtools",
        "com.spreadtrum.vce",
        "com.spreadtrum.vowifi",
        "com.unisoc.storageclearmanager",
        "com.zediel.pcbtest",
        "penseavanti.com.br.app2shop_multilaser",
        "com.android.fmradio",
        "com.google.android.apps.googleassistant",
        "com.google.android.apps.maps",
        "com.google.android.apps.tachyon",
        "com.google.android.apps.youtube.music",
        "com.google.android.calendar",
        "com.google.android.gm",
        "com.google.android.projection.gearhead",
        "com.google.android.videos",
        "com.google.android.youtube",
    )

    // Only on a phone (the school's Redmi 10C).
    val PHONE: List<String> = listOf(
        "com.android.deskclock",
        "com.android.providers.downloads.ui",
        "com.android.stk",
        "com.android.thememanager",
        "com.google.android.apps.docs",
        "com.google.android.apps.safetyhub",
        "com.google.android.apps.wellbeing",
        "com.google.android.contacts",
        "com.google.android.dialer",
        "com.miui.player",
        "com.miui.videoplayer",
        "com.miui.weather2",
    )

    // Never removed nor hidden, whatever the lists say: the agent, the vetted
    // UnusedApps.PROTECTED and BloatList.NEVER_HIDE (the keyboard, the
    // WebView, Chrome, the settings, Google Play...), Android's own launcher
    // and the class's dock apps (Launcher3Home.DOCK_APPS), which the home
    // arranged right after must still find. UnusedApps.PROTECTED holds a few
    // the cable removes on purpose (the dialers and Contacts, Messages, the
    // Clock, Google Drive): over the network they stay, as the optimizer has
    // always kept them.
    val KEPT: Set<String> = UnusedApps.PROTECTED + BloatList.NEVER_HIDE + Lockdown.PACKAGE +
        Launcher3Home.LAUNCHER + Launcher3Home.DOCK_APPS.map { it.pkg }

    // The cable's list for this kind of device, in the cable's order.
    fun listed(tablet: Boolean): List<String> = REMOVED + (if (tablet) TABLET else PHONE)

    // The listed apps the device has, none kept nor never hidden on this
    // device (BloatList.neverHide), in the list's order.
    fun toRemove(installed: Set<String>, neverHide: Set<String>, tablet: Boolean): List<String> =
        listed(tablet).filter { it in installed && it !in neverHide && it !in KEPT }.distinct()
}
