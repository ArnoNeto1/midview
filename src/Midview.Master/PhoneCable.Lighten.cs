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

    // A lighter phone: no animations, the apps above removed, the heavy apps
    // still on it kept from running in the background, and the home screen's
    // gestures that open the removed Google app (a blank white screen) off.
    // Only from the cable, so right before the Device Owner step; a failure
    // does not stop the setup.
    private void Lighten()
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
        foreach (var app in RemovedApps.Where(installed.Contains))
        {
            if (Shell(Short, "pm", "uninstall", "--user", "0", app).Text.Contains("Success", StringComparison.Ordinal))
            {
                installed.Remove(app);
            }
        }
        foreach (var app in HeavyApps.Where(installed.Contains))
        {
            Shell(Short, "cmd", "appops", "set", app, "RUN_ANY_IN_BACKGROUND", "ignore");
        }
    }
}
