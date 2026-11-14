using System;
using System.Collections.Generic;
using System.Linq;

namespace Midview.Master;

// The "Midview otimizado" part of the cable: a lighter phone.
public sealed partial class PhoneCable
{
    // Apps that come with the phone, are heavy and that a class does not need
    // running unseen. Never Midview, the launcher, the keyboard, the dialer,
    // Google Play services, the Play Store, Chrome or Settings. Undone on the phone with
    // "cmd appops set <app> RUN_ANY_IN_BACKGROUND allow".
    internal static readonly IReadOnlyList<string> HeavyApps = new[]
    {
        "cn.wps.moffice_eng",
        "com.google.android.apps.docs",
        "com.miui.weather2",
        "com.miui.player",
        "com.miui.videoplayer",
        "com.mi.globalminusscreen",
    };

    // Apps removed for the phone's user ("pm uninstall --user 0"), checked
    // against the Universal Android Debloater list (all "Recommended" there,
    // or "Advanced" and of no use to a class) and tried on the school's
    // Redmi 10C on 2026-11-13: factory tests, Xiaomi and Google telemetry and
    // ads, cloud backup, Chinese payments, "Ok Google", the Google app (about
    // 285 MB of a 2.7 GB phone's memory). Also what kept running unseen on a
    // school phone with no SIM (the school's choice, 2026-11-13): Messages
    // and its chat process (about 71 MB), Android System Intelligence and the
    // setup wizard's "finish setting up" reminder. Never Midview, the
    // launcher, the keyboard, the dialer, the camera, the gallery, the
    // calculator, the file manager, the screen recorder, Chrome, Google Play
    // services, the Play Store, the installers, the VPN screens (the internet
    // block), what the Device Owner step needs, or Xiaomi's Find device (Mi
    // Cloud's service crashes at every start without its provider). A
    // system app comes back with "cmd package install-existing <app>" or a
    // factory reset.
    internal static readonly IReadOnlyList<string> RemovedApps = new[]
    {
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
    };

    // A tablet's own extra apps out, besides the ones above: tried on the
    // school's Multilaser M10 (Android 10, Unisoc) on 2026-11-13, the
    // school's choice for a tablet with no SIM and no Google account. The
    // phone (dialer, SMS push, the SIM's secure element), the Unisoc and
    // factory test tools, the system's style packs, Keep (it opened by
    // itself), Digital Wellbeing (the school controls it through Midview),
    // and the maker's app store. Never the phone service (com.android.phone)
    // or the Contacts app the cable's local account check uses. Only on a
    // tablet, so a phone's proven list stays as it was.
    internal static readonly IReadOnlyList<string> TabletRemovedApps = new[]
    {
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
    };

    // On a tablet the Play Store and Google's voice also stop running unseen
    // (with no Google account they only keep themselves busy). Undone with
    // "cmd appops set <app> RUN_ANY_IN_BACKGROUND allow".
    internal static readonly IReadOnlyList<string> TabletHeavyApps = new[]
    {
        "com.android.vending",
        "com.google.android.tts",
    };

    // A lighter phone: no animations, the apps above removed, the heavy apps
    // still on it kept from running in the background, and the home screen's
    // gestures that open the removed Google app (a blank white screen) off.
    // Only from the cable, so right before the Device Owner step; a failure
    // does not stop the setup.
    private void Lighten(AndroidDeviceKind kind)
    {
        foreach (var scale in new[] { "window_animation_scale", "transition_animation_scale", "animator_duration_scale" })
        {
            Shell(Short, "settings", "put", "global", scale, "0");
        }
        // MIUI's home: "Deslize para cima na Tela inicial para abrir a
        // pesquisa" and "Google Discover" (read and applied by the launcher
        // at once; unknown and harmless on other phones).
        Shell(Short, "settings", "put", "system", "QSB_SCROLL_ENTRANCE_STATUS", "0");
        Shell(Short, "settings", "put", "system", "open_personal_assistant", "0");
        var installed = Shell(Short, "pm", "list", "packages").Text
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("package:", StringComparison.Ordinal))
            .Select(line => line["package:".Length..])
            .ToHashSet(StringComparer.Ordinal);
        var tablet = kind == AndroidDeviceKind.Tablet;
        foreach (var app in (tablet ? RemovedApps.Concat(TabletRemovedApps) : RemovedApps).Where(installed.Contains))
        {
            if (Shell(Short, "pm", "uninstall", "--user", "0", app).Text.Contains("Success", StringComparison.Ordinal))
            {
                installed.Remove(app);
            }
        }
        foreach (var app in (tablet ? HeavyApps.Concat(TabletHeavyApps) : HeavyApps).Where(installed.Contains))
        {
            Shell(Short, "cmd", "appops", "set", app, "RUN_ANY_IN_BACKGROUND", "ignore");
        }
    }
}
