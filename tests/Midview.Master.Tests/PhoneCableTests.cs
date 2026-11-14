using System;
using System.Collections.Generic;
using System.Linq;
using Midview.Master;
using Midview.Protocol;
using Xunit;

namespace Midview.Master.Tests;

public sealed class PhoneCableTests
{
    // Every command after "devices" names the phone the check found.
    private const string Serial = "79a61f85";
    private const string S = "-s " + Serial + " ";
    private const string Apk = @"C:\Midview\agent.apk";
    private const string Package = "io.github.arnoneto1.midview.agent";
    private const string Admin = "io.github.arnoneto1.midview.agent/.AdminReceiver";
    private const string Thumb = "AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12";
    private const string SetDeviceOwner = S + "shell dpm set-device-owner " + Admin;
    private const string Accessibility = "io.github.arnoneto1.midview.agent/io.github.arnoneto1.midview.agent.screen.AccessibilityCapture";

    // Answers by the exact arguments, joined with spaces; whatever is not set
    // answers an empty success. Records every call in order.
    private sealed class FakeAdb : IAdb
    {
        public readonly List<string> Calls = new();
        private readonly Dictionary<string, AdbOutput> _answers = new();

        public TimeSpan LastInstallTimeout;

        public FakeAdb Set(string key, string text, int exit = 0, bool timedOut = false)
        {
            _answers[key] = new AdbOutput(exit, text, timedOut);
            return this;
        }

        public AdbOutput Run(TimeSpan timeout, params string[] args)
        {
            var key = string.Join(' ', args);
            Calls.Add(key);
            if (args.Contains("install"))
            {
                LastInstallTimeout = timeout;
            }
            return _answers.TryGetValue(key, out var answer) ? answer : new AdbOutput(0, "", false);
        }
    }

    // After the given command has run, "devices" lists nothing.
    private sealed class DropAfter : IAdb
    {
        private readonly FakeAdb _inner;
        private readonly string _after;
        private bool _dropped;

        public DropAfter(FakeAdb inner, string after)
        {
            _inner = inner;
            _after = after;
        }

        public AdbOutput Run(TimeSpan timeout, params string[] args)
        {
            var key = string.Join(' ', args);
            if (_dropped && key == "devices")
            {
                _inner.Calls.Add(key);
                return new AdbOutput(0, "List of devices attached\n\n", false);
            }
            var output = _inner.Run(timeout, args);
            if (key == _after)
            {
                _dropped = true;
            }
            return output;
        }
    }

    // After the given command has run, "devices" lists what is given instead.
    private class DevicesAfter : IAdb
    {
        private readonly FakeAdb _inner;
        private readonly string _after;
        private readonly string _listed;
        private bool _changed;

        protected DevicesAfter(FakeAdb inner, string after, string listed)
        {
            _inner = inner;
            _after = after;
            _listed = listed;
        }

        public AdbOutput Run(TimeSpan timeout, params string[] args)
        {
            var key = string.Join(' ', args);
            if (_changed && key == "devices")
            {
                _inner.Calls.Add(key);
                return new AdbOutput(0, _listed, false);
            }
            var output = _inner.Run(timeout, args);
            if (key == _after)
            {
                _changed = true;
            }
            return output;
        }
    }

    // A second phone is plugged in beside the one being prepared.
    private sealed class SecondPhoneAfter : DevicesAfter
    {
        public SecondPhoneAfter(FakeAdb inner, string after)
            : base(inner, after, "List of devices attached\n" + Serial + "\tdevice\n0123456789\tdevice\n\n")
        {
        }
    }

    // The phone being prepared left, and another one is on the cable.
    private sealed class OtherPhoneOnlyAfter : DevicesAfter
    {
        public OtherPhoneOnlyAfter(FakeAdb inner, string after)
            : base(inner, after, "List of devices attached\n0123456789\tdevice\n\n")
        {
        }
    }

    // The real answers of the school's Redmi 10C (MIUI 14, Android 13), freshly reset.
    private static FakeAdb Redmi() => new FakeAdb()
        .Set("devices", "List of devices attached\n79a61f85\tdevice\n\n")
        .Set(S + "shell getprop ro.build.version.sdk", "33\r\n")
        .Set(S + "shell getprop ro.miui.ui.version.name", "V140\n")
        .Set(S + "shell getprop persist.security.adbinput", "1\n")
        .Set(S + "shell getprop persist.security.adbinstall", "1\n")
        .Set(S + "shell dumpsys account", "Accounts: 0\n  Accounts: 0\n  Active Sync Adapters\n")
        .Set(S + "shell pm list users", "Users:\n\tUserInfo{0:Propriet\u00e1rio:c13} running\n")
        .Set(S + "shell dpm list-owners", "no owners\n");

    private static string ContentCall() =>
        S + "shell content call --uri content://io.github.arnoneto1.midview.agent.setup --method prepare --extra setup:s:"
        + CableSetupPayload.Encode("07", Thumb, "Lab", "pw");

    private static string OnlyMidviewCall() =>
        S + "shell content call --uri content://io.github.arnoneto1.midview.agent.setup --method prepare --extra setup:s:"
        + CableSetupPayload.Encode("07", Thumb, "Lab", "pw", optimize: false);

    private static FakeAdb ReadyToPrepare() => Redmi()
        .Set(S + "install -r " + Apk, "Performing Streamed Install\nSuccess\n")
        .Set(ContentCall(), "Result: Bundle[{ok=true, thumbprint=" + Thumb + ", name=Celular 07}]\n")
        .Set(SetDeviceOwner, "Success: Device owner set to package ComponentInfo{" + Admin + "}\nActive admin set to component {" + Admin + "}\n");

    private static CableResult Prepare(IAdb adb, List<CableStage>? stages = null) =>
        new PhoneCable(adb).Prepare(Apk, "07", Thumb, "Lab", "pw", s => stages?.Add(s));

    // ---- Check ----

    [Fact]
    public void Real_redmi_outputs_pass_the_check()
    {
        Assert.Null(new PhoneCable(Redmi()).Check());
    }

    [Fact]
    public void No_phone_when_devices_lists_none()
    {
        var adb = Redmi().Set("devices", "List of devices attached\n\n");
        Assert.Equal(CableProblem.NoPhone, new PhoneCable(adb).Check());
    }

    [Fact]
    public void No_phone_when_the_phone_is_offline()
    {
        var adb = Redmi().Set("devices", "List of devices attached\n79a61f85\toffline\n");
        Assert.Equal(CableProblem.NoPhone, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Server_start_lines_are_ignored()
    {
        var adb = Redmi().Set("devices", "* daemon not running; starting now at tcp:5039\n* daemon started successfully\nList of devices attached\r\n79a61f85\tdevice\r\n");
        Assert.Null(new PhoneCable(adb).Check());
    }

    [Fact]
    public void Too_many_phones()
    {
        var adb = Redmi().Set("devices", "List of devices attached\n79a61f85\tdevice\n0123456789\tdevice\n");
        Assert.Equal(CableProblem.TooMany, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Unauthorized_phone()
    {
        var adb = Redmi().Set("devices", "List of devices attached\n79a61f85\tunauthorized\n");
        Assert.Equal(CableProblem.Unauthorized, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Too_old_below_android_10()
    {
        var adb = Redmi().Set(S + "shell getprop ro.build.version.sdk", "28\n");
        Assert.Equal(CableProblem.TooOld, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Android_10_is_enough()
    {
        var adb = Redmi().Set(S + "shell getprop ro.build.version.sdk", "29\n");
        Assert.Null(new PhoneCable(adb).Check());
    }

    [Fact]
    public void Security_switch_off_on_miui()
    {
        var adb = Redmi().Set(S + "shell getprop persist.security.adbinput", "0\n");
        Assert.Equal(CableProblem.SecurityOff, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Security_switch_missing_on_miui_counts_as_off()
    {
        var adb = Redmi().Set(S + "shell getprop persist.security.adbinput", "\n");
        Assert.Equal(CableProblem.SecurityOff, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Install_switch_off_on_miui()
    {
        var adb = Redmi().Set(S + "shell getprop persist.security.adbinstall", "0\n");
        Assert.Equal(CableProblem.InstallOff, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Phone_without_miui_does_not_check_the_switches()
    {
        var adb = Redmi()
            .Set(S + "shell getprop ro.miui.ui.version.name", "\n")
            .Set(S + "shell getprop persist.security.adbinput", "")
            .Set(S + "shell getprop persist.security.adbinstall", "");
        Assert.Null(new PhoneCable(adb).Check());
        Assert.DoesNotContain(S + "shell getprop persist.security.adbinput", adb.Calls);
        Assert.DoesNotContain(S + "shell getprop persist.security.adbinstall", adb.Calls);
    }

    [Fact]
    public void An_account_on_the_phone()
    {
        var adb = Redmi().Set(S + "shell dumpsys account", "Accounts: 1\n  Account {name=a@b.c, type=com.google}\n");
        Assert.Equal(CableProblem.HasAccounts, new PhoneCable(adb).Check());
    }

    [Fact]
    public void An_account_with_extra_whitespace_and_indent()
    {
        var adb = Redmi().Set(S + "shell dumpsys account", "User UserInfo{0:x:c13}:\r\n    Accounts:   2\r\n");
        Assert.Equal(CableProblem.HasAccounts, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Dual_apps_user_means_has_users()
    {
        var adb = Redmi().Set(S + "shell pm list users",
            "Users:\n\tUserInfo{0:Propriet\u00e1rio:c13} running\n\tUserInfo{999:XSpace:801013} running\n");
        Assert.Equal(CableProblem.HasUsers, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Phone_already_owned_by_the_school_app()
    {
        var adb = Redmi().Set(S + "shell dpm list-owners",
            "1 owner:\nUser  0: admin=" + Admin + ",DeviceOwner,Affiliated\n");
        Assert.Equal(CableProblem.AlreadySchool, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Another_owner_is_not_the_school()
    {
        var adb = Redmi().Set(S + "shell dpm list-owners", "1 owner:\nUser  0: admin=com.other/.Admin,DeviceOwner\n");
        Assert.Null(new PhoneCable(adb).Check());
    }

    // ---- Prepare ----

    [Fact]
    public void Happy_path_runs_the_commands_in_the_fixed_order()
    {
        var adb = ReadyToPrepare();
        var stages = new List<CableStage>();

        var result = Prepare(adb, stages);

        Assert.Null(result.Problem);
        Assert.False(result.OwnerUnconfirmed);
        Assert.Equal(
            new KnownMachine("Celular 07", "0.0.0.0", Thumb, Thumb, null, DeviceKind.Android), result.Phone);

        var afterCheck = adb.Calls.SkipWhile(c => c != S + "shell dpm list-owners").Skip(1).ToList();
        Assert.Equal(new[]
        {
            S + "shell dumpsys window policy",
            S + "shell pm list packages " + Package,
            S + "shell input keyevent KEYCODE_WAKEUP",
            S + "shell wm dismiss-keyguard",
            S + "install -r " + Apk,
            S + "shell cmd appops set " + Package + " 10008 allow",
            S + "shell cmd deviceidle whitelist +" + Package,
            S + "shell cmd appops set --uid " + Package + " MANAGE_EXTERNAL_STORAGE allow",
            S + "shell pm grant " + Package + " android.permission.READ_EXTERNAL_STORAGE",
            S + "shell pm grant " + Package + " android.permission.WRITE_EXTERNAL_STORAGE",
            S + "shell cmd appops set " + Package + " PROJECT_MEDIA allow",
            S + "shell settings put secure enabled_accessibility_services " + Accessibility,
            S + "shell settings put secure accessibility_enabled 1",
            S + "shell settings put global force_fsg_nav_bar 0",
            ContentCall(),
            // The home screen (PhoneHomeScreen), on a Xiaomi: its settings do
            // not open here, so it stops there and the phone goes on.
            S + "shell input keyevent KEYCODE_WAKEUP",
            S + "shell wm dismiss-keyguard",
            S + "shell settings put system miui_home_lock_screen_cells 0",
            S + "shell settings put system QSB_SCROLL_ENTRANCE_STATUS 0",
            S + "shell settings put system open_personal_assistant 0",
            S + "shell am start -W --activity-clear-task -n com.miui.home/.settings.MiuiHomeSettingActivity",
            S + "shell input keyevent KEYCODE_HOME",
            S + "shell rm -f /data/local/tmp/midview-home.xml",
            // Lighten: last before the Owner step (after a Mi sign-out, when there is one).
            S + "shell settings put global window_animation_scale 0",
            S + "shell settings put global transition_animation_scale 0",
            S + "shell settings put global animator_duration_scale 0",
            S + "shell settings put system QSB_SCROLL_ENTRANCE_STATUS 0",
            S + "shell settings put system open_personal_assistant 0",
            S + "shell pm list packages",
            "devices",
            SetDeviceOwner,
        }, afterCheck);
        Assert.Equal(
            new[] { CableStage.Checking, CableStage.Installing, CableStage.Wifi, CableStage.Pairing, CableStage.HomeScreen, CableStage.Owner },
            stages);
        Assert.True(result.HomeByHand);
        Assert.Equal(TimeSpan.FromMinutes(3), adb.LastInstallTimeout);
    }

    [Fact]
    public void Nothing_runs_after_the_dpm_command()
    {
        var adb = ReadyToPrepare();
        Prepare(adb);
        Assert.Equal(SetDeviceOwner, adb.Calls[^1]);
    }

    [Fact]
    public void Installed_package_is_uninstalled_before_the_install()
    {
        var adb = ReadyToPrepare().Set(S + "shell pm list packages " + Package, "package:" + Package + "\n");

        Assert.Null(Prepare(adb).Problem);

        var uninstall = adb.Calls.IndexOf(S + "shell pm uninstall " + Package);
        Assert.True(uninstall >= 0);
        Assert.True(uninstall < adb.Calls.IndexOf(S + "install -r " + Apk));
        Assert.DoesNotContain(adb.Calls, c => c.Contains("pm clear"));
    }

    [Fact]
    public void Fresh_phone_is_not_uninstalled()
    {
        var adb = ReadyToPrepare();
        Prepare(adb);
        Assert.DoesNotContain(adb.Calls, c => c.Contains("pm uninstall") || c.Contains("pm clear"));
    }

    [Fact]
    public void Every_command_after_devices_names_the_phone()
    {
        var adb = ReadyToPrepare();
        Assert.Null(Prepare(adb).Problem);
        Assert.All(adb.Calls.Where(c => c != "devices"), c => Assert.StartsWith(S, c));
    }

    [Fact]
    public void A_second_phone_plugged_in_after_the_check_does_not_stop_the_setup()
    {
        // The check saw one phone; from the content call on, two are listed.
        var adb = ReadyToPrepare();
        var result = Prepare(new SecondPhoneAfter(adb, ContentCall()));
        Assert.Null(result.Problem);
        Assert.False(result.OwnerUnconfirmed);
        Assert.Equal(SetDeviceOwner, adb.Calls[^1]);
    }

    [Fact]
    public void The_prepared_phone_gone_and_another_one_left_is_lost()
    {
        var adb = ReadyToPrepare();
        var result = Prepare(new OtherPhoneOnlyAfter(adb, ContentCall()));
        Assert.Equal(CableProblem.Lost, result.Problem);
        Assert.DoesNotContain(adb.Calls, c => c.Contains("set-device-owner"));
    }

    [Fact]
    public void More_than_one_device_from_the_dpm_means_it_did_not_run()
    {
        var adb = ReadyToPrepare().Set(SetDeviceOwner, "adb: error: more than one device/emulator\n", 1);
        var result = Prepare(adb);
        Assert.Equal(CableProblem.Refused, result.Problem);
        Assert.Null(result.Phone);
        Assert.False(result.OwnerUnconfirmed);
    }

    [Fact]
    public void A_closed_connection_during_the_dpm_is_unconfirmed()
    {
        var adb = ReadyToPrepare().Set(SetDeviceOwner, "error: closed\n", 1);
        var result = Prepare(adb);
        Assert.True(result.OwnerUnconfirmed);
        Assert.NotNull(result.Phone);
    }

    [Fact]
    public void Closed_inside_another_failure_is_not_a_lost_cable()
    {
        var adb = ReadyToPrepare().Set(SetDeviceOwner,
            "java.lang.IllegalStateException: Trying to set the device owner, but the session is closed\n", 255);
        var result = Prepare(adb);
        Assert.Equal(CableProblem.Refused, result.Problem);
        Assert.False(result.OwnerUnconfirmed);
    }

    [Fact]
    public void A_check_problem_stops_before_any_change()
    {
        var adb = ReadyToPrepare().Set(S + "shell dumpsys account", "Accounts: 1\n");
        var result = Prepare(adb);
        Assert.Equal(CableProblem.HasAccounts, result.Problem);
        Assert.Null(result.Phone);
        Assert.DoesNotContain(adb.Calls, c => c.Contains("install -r"));
    }

    // What the Redmi 10C (MIUI 14) answered on 2026-11-12 when nobody touched
    // "Instalar" in its "Instalar via USB" question (it says no by itself
    // after 10 s), and when its screen was locked (no question at all). The
    // "Instalar via USB" switch was on: Check had read it.
    [Fact]
    public void Install_question_not_answered_means_declined()
    {
        var adb = ReadyToPrepare().Set(S + "install -r " + Apk,
            "Performing Streamed Install\nadb.exe: failed to install C:\\x\\MidviewAgent.apk: Failure [INSTALL_FAILED_USER_RESTRICTED: Install canceled by user]\n", 1);
        Assert.Equal(CableProblem.InstallDeclined, Prepare(adb).Problem);
        Assert.DoesNotContain(adb.Calls, c => c.Contains("content call"));
    }

    // Google Play Protect may ask first ("Enviar o app para uma verificação
    // de segurança?") and the install waits for the answer: on the Redmi 10C
    // it never ended by itself.
    [Fact]
    public void Install_left_waiting_on_the_phone_means_declined()
    {
        var adb = ReadyToPrepare().Set(S + "install -r " + Apk, "", -1, timedOut: true);
        Assert.Equal(CableProblem.InstallDeclined, Prepare(adb).Problem);
    }

    // Every answer exactly as the Redmi 10C (MIUI 14) gave it on 2026-11-12,
    // preparing it over the cable from start to end (number and thumbprints
    // swapped for this test's).
    [Fact]
    public void Real_redmi_answers_of_a_whole_preparation()
    {
        var adb = Redmi()
            .Set(S + "shell pm list packages " + Package, "package:" + Package + "\n")
            .Set(S + "shell pm uninstall " + Package, "Success\n")
            .Set(S + "install -r " + Apk, "Performing Streamed Install\nSuccess\n")
            .Set(S + "shell cmd deviceidle whitelist +" + Package, "Added: " + Package + "\n")
            .Set(ContentCall(), "Result: Bundle[{thumbprint=" + Thumb + ", ok=true, name=Celular 07}]\n")
            .Set(SetDeviceOwner,
                "Success: Device owner set to package " + Admin + "\nActive admin set to component " + Admin + "\n");

        var result = Prepare(adb);

        Assert.Null(result.Problem);
        Assert.False(result.OwnerUnconfirmed);
        Assert.Equal("Celular 07", result.Phone!.Name);
        Assert.Equal(Thumb, result.Phone.AgentThumbprint);
    }

    // The phone asks only with its screen on and unlocked: woken first.
    [Fact]
    public void Screen_is_woken_and_unlocked_before_installing()
    {
        var adb = ReadyToPrepare();
        Prepare(adb);
        var wake = adb.Calls.IndexOf(S + "shell input keyevent KEYCODE_WAKEUP");
        var unlock = adb.Calls.IndexOf(S + "shell wm dismiss-keyguard");
        var install = adb.Calls.IndexOf(S + "install -r " + Apk);
        Assert.True(wake >= 0 && wake < unlock && unlock < install);
    }

    [Fact]
    public void Other_install_failure()
    {
        var adb = ReadyToPrepare().Set(S + "install -r " + Apk, "Failure [INSTALL_FAILED_INSUFFICIENT_STORAGE]\n", 1);
        Assert.Equal(CableProblem.InstallFailed, Prepare(adb).Problem);
    }

    [Fact]
    public void Release_commands_failing_do_not_stop_the_setup()
    {
        var adb = ReadyToPrepare()
            .Set(S + "shell cmd appops set " + Package + " 10008 allow", "Error: unknown op", 255)
            .Set(S + "shell cmd deviceidle whitelist +" + Package, "Error", 255)
            .Set(S + "shell cmd appops set --uid " + Package + " MANAGE_EXTERNAL_STORAGE allow", "Error: Unknown operation string", 255)
            .Set(S + "shell pm grant " + Package + " android.permission.READ_EXTERNAL_STORAGE", "Exception occurred while executing 'grant'", 255)
            .Set(S + "shell pm grant " + Package + " android.permission.WRITE_EXTERNAL_STORAGE", "Exception occurred while executing 'grant'", 255)
            .Set(S + "shell cmd appops set " + Package + " PROJECT_MEDIA allow", "Error: Unknown operation string", 255)
            .Set(S + "shell settings put secure enabled_accessibility_services " + Accessibility, "Error", 255)
            .Set(S + "shell settings put secure accessibility_enabled 1", "Error", 255)
            .Set(S + "shell settings put global force_fsg_nav_bar 0", "Error", 255)
            .Set(S + "shell settings put global window_animation_scale 0", "Error", 255)
            .Set(S + "shell settings put global transition_animation_scale 0", "Error", 255)
            .Set(S + "shell settings put global animator_duration_scale 0", "Error", 255)
            .Set(S + "shell settings put system QSB_SCROLL_ENTRANCE_STATUS 0", "Error", 255)
            .Set(S + "shell settings put system open_personal_assistant 0", "Error", 255)
            .Set(S + "shell pm list packages", "package:com.miui.weather2\npackage:com.miui.daemon\n")
            .Set(S + "shell pm uninstall --user 0 com.miui.daemon", "Failure [-1000]", 1)
            .Set(S + "shell cmd appops set com.miui.weather2 RUN_ANY_IN_BACKGROUND ignore", "Error", 255);
        var result = Prepare(adb);
        Assert.Null(result.Problem);
        Assert.NotNull(result.Phone);
    }

    [Fact]
    public void Only_the_heavy_apps_on_the_phone_are_kept_from_running_in_the_background()
    {
        var adb = ReadyToPrepare().Set(S + "shell pm list packages",
            "package:com.android.chrome\npackage:cn.wps.moffice_eng\npackage:com.miui.home\n"
            + "package:com.google.android.apps.messaging\npackage:" + Package + "\npackage:com.mi.globalminusscreen\n"
            + "package:com.google.android.gms\npackage:com.android.vending\n");

        Prepare(adb);

        var background = adb.Calls.Where(c => c.Contains("RUN_ANY_IN_BACKGROUND", StringComparison.Ordinal)).ToList();
        Assert.Equal(new[]
        {
            S + "shell cmd appops set cn.wps.moffice_eng RUN_ANY_IN_BACKGROUND ignore",
            S + "shell cmd appops set com.mi.globalminusscreen RUN_ANY_IN_BACKGROUND ignore",
        }, background);
        var owner = adb.Calls.IndexOf(SetDeviceOwner);
        Assert.All(background, c => Assert.True(adb.Calls.IndexOf(c) < owner));
    }

    [Fact]
    public void Heavy_app_list_spares_what_the_phone_and_midview_need()
    {
        foreach (var app in new[]
                 {
                     Package, "com.miui.home", "com.google.android.inputmethod.latin", "com.google.android.dialer", "com.google.android.apps.messaging",
                     "com.android.phone", "com.google.android.gms", "com.android.vending", "com.android.chrome",
                     "com.android.settings",
                 })
        {
            Assert.DoesNotContain(app, PhoneCable.HeavyApps);
        }
        Assert.Equal(6, PhoneCable.HeavyApps.Count);
    }

    [Fact]
    public void Only_the_listed_apps_on_the_phone_are_removed_and_a_removed_one_is_not_restricted()
    {
        var adb = ReadyToPrepare()
            .Set(S + "shell pm list packages",
                "package:com.android.chrome\npackage:com.miui.daemon\npackage:com.google.android.googlequicksearchbox\n"
                + "package:com.mi.globalminusscreen\npackage:cn.wps.moffice_eng\npackage:com.miui.calculator\n")
            .Set(S + "shell pm uninstall --user 0 com.miui.daemon", "Success\n")
            .Set(S + "shell pm uninstall --user 0 com.google.android.googlequicksearchbox", "Success\n")
            .Set(S + "shell pm uninstall --user 0 com.mi.globalminusscreen", "Success\n");

        Prepare(adb);

        var removed = adb.Calls.Where(c => c.Contains("pm uninstall --user 0", StringComparison.Ordinal)).ToList();
        Assert.Equal(new[]
        {
            S + "shell pm uninstall --user 0 com.google.android.googlequicksearchbox",
            S + "shell pm uninstall --user 0 com.mi.globalminusscreen",
            S + "shell pm uninstall --user 0 com.miui.daemon",
        }, removed);
        // App Vault is gone, so only WPS is still kept from the background.
        var background = adb.Calls.Where(c => c.Contains("RUN_ANY_IN_BACKGROUND", StringComparison.Ordinal)).ToList();
        Assert.Equal(new[] { S + "shell cmd appops set cn.wps.moffice_eng RUN_ANY_IN_BACKGROUND ignore" }, background);
        var owner = adb.Calls.IndexOf(SetDeviceOwner);
        Assert.All(removed, c => Assert.True(adb.Calls.IndexOf(c) < owner));
    }

    [Fact]
    public void Removed_app_list_spares_what_the_class_the_phone_and_midview_need()
    {
        foreach (var app in new[]
                 {
                     Package, "com.miui.home", "com.google.android.inputmethod.latin", "com.google.android.dialer",
                     "com.android.phone", "com.google.android.gms", "com.google.android.gsf", "com.xiaomi.finddevice",
                     "com.android.vending", "com.android.chrome", "com.google.android.webview", "com.android.settings",
                     "com.android.camera", "com.miui.gallery", "com.miui.calculator", "com.mi.android.globalFileexplorer",
                     "com.google.android.documentsui", "com.miui.screenrecorder", "com.miui.securitycenter",
                     "com.miui.global.packageinstaller", "com.google.android.packageinstaller", "com.android.managedprovisioning",
                     "com.android.vpndialogs", "com.android.providers.media", "com.android.externalstorage",
                     "com.android.shell", "cn.wps.moffice_eng", "cn.wps.xiaomi.abroad.lite",
                 })
        {
            Assert.DoesNotContain(app, PhoneCable.RemovedApps);
        }
        Assert.Equal(70, PhoneCable.RemovedApps.Count);
        Assert.Equal(PhoneCable.RemovedApps.Count, PhoneCable.RemovedApps.Distinct().Count());
    }

    // "Só o Midview": the phone is prepared as before, but none of Lighten's
    // commands touch it (animations, home gestures, removed apps, background).
    [Fact]
    public void Without_optimize_nothing_is_lightened_yet_the_phone_is_ready()
    {
        var adb = ReadyToPrepare()
            .Set(S + "shell pm list packages", "package:com.miui.daemon\npackage:cn.wps.moffice_eng\n")
            .Set(S + "shell pm uninstall --user 0 com.miui.daemon", "Success\n")
            .Set(OnlyMidviewCall(), "Result: Bundle[{ok=true, thumbprint=" + Thumb + ", name=Celular 07}]\n");

        var result = new PhoneCable(adb).Prepare(Apk, "07", Thumb, "Lab", "pw", _ => { }, optimize: false);

        // The phone is told too, so its agent leaves the apps as they came.
        Assert.Contains(OnlyMidviewCall(), adb.Calls);

        Assert.Null(result.Problem);
        Assert.Equal(new KnownMachine("Celular 07", "0.0.0.0", Thumb, Thumb, null, DeviceKind.Android), result.Phone);
        Assert.DoesNotContain(adb.Calls, c => c.Contains("animation_scale", StringComparison.Ordinal)
                                              || c.Contains("animator_duration_scale", StringComparison.Ordinal));
        Assert.DoesNotContain(adb.Calls, c => c.Contains("QSB_SCROLL_ENTRANCE_STATUS", StringComparison.Ordinal));
        Assert.DoesNotContain(adb.Calls, c => c.Contains("open_personal_assistant", StringComparison.Ordinal));
        Assert.DoesNotContain(adb.Calls, c => c.Contains("pm uninstall --user 0", StringComparison.Ordinal));
        Assert.DoesNotContain(adb.Calls, c => c.Contains("RUN_ANY_IN_BACKGROUND", StringComparison.Ordinal));
        Assert.DoesNotContain(S + "shell pm list packages", adb.Calls);
        // Everything else runs as with the optimized setup.
        Assert.Contains(S + "install -r " + Apk, adb.Calls);
        Assert.Contains(S + "shell settings put secure accessibility_enabled 1", adb.Calls);
        Assert.Contains(S + "shell settings put global force_fsg_nav_bar 0", adb.Calls);
        Assert.Equal(SetDeviceOwner, adb.Calls[^1]);
    }

    [Fact]
    public void A_phone_without_miui_gets_the_three_buttons_through_the_overlay()
    {
        var adb = ReadyToPrepare()
            .Set(S + "shell getprop ro.miui.ui.version.name", "\n")
            .Set(S + "shell cmd overlay enable-exclusive --category com.android.internal.systemui.navbar.threebutton",
                "Error: no overlay", 255);

        var result = Prepare(adb);

        Assert.Null(result.Problem);
        Assert.Contains(
            S + "shell cmd overlay enable-exclusive --category com.android.internal.systemui.navbar.threebutton", adb.Calls);
        Assert.DoesNotContain(adb.Calls, c => c.Contains("force_fsg_nav_bar", StringComparison.Ordinal));
    }

    // A tablet: named "Tablet 07" here, and told so in the package.
    [Fact]
    public void A_tablet_is_named_tablet_and_its_package_says_so()
    {
        var call = S + "shell content call --uri content://io.github.arnoneto1.midview.agent.setup --method prepare --extra setup:s:"
                   + CableSetupPayload.Encode("07", Thumb, "Lab", "pw", AndroidDeviceKind.Tablet);
        var adb = ReadyToPrepare().Set(call, "Result: Bundle[{ok=true, thumbprint=" + Thumb + ", name=Tablet 07}]\n");

        var result = new PhoneCable(adb).Prepare(Apk, "07", Thumb, "Lab", "pw", _ => { }, kind: AndroidDeviceKind.Tablet);

        Assert.Null(result.Problem);
        Assert.Equal("Tablet 07", result.Phone!.Name);
        Assert.Contains(call, adb.Calls);
        Assert.DoesNotContain(ContentCall(), adb.Calls);
    }

    // A tablet loses its own extra apps too (TabletRemovedApps) and keeps the
    // Play Store from running unseen; a phone with the same apps does not.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Only_a_tablet_loses_the_tablet_apps(bool tablet)
    {
        var kind = tablet ? AndroidDeviceKind.Tablet : AndroidDeviceKind.Phone;
        var call = S + "shell content call --uri content://io.github.arnoneto1.midview.agent.setup --method prepare --extra setup:s:"
                   + CableSetupPayload.Encode("07", Thumb, "Lab", "pw", kind);
        var adb = ReadyToPrepare()
            .Set(call, "Result: Bundle[{ok=true, thumbprint=" + Thumb + ", name=Tablet 07}]\n")
            .Set(S + "shell pm list packages",
                "package:com.sprd.engineermode\npackage:com.android.dialer\npackage:com.miui.daemon\n"
                + "package:com.android.vending\npackage:com.android.phone\npackage:com.android.contacts\n")
            .Set(S + "shell pm uninstall --user 0 com.sprd.engineermode", "Success\n")
            .Set(S + "shell pm uninstall --user 0 com.android.dialer", "Success\n")
            .Set(S + "shell pm uninstall --user 0 com.miui.daemon", "Success\n");

        new PhoneCable(adb).Prepare(Apk, "07", Thumb, "Lab", "pw", _ => { }, kind: kind);

        var removed = adb.Calls.Where(c => c.Contains("pm uninstall --user 0", StringComparison.Ordinal)).ToList();
        var background = adb.Calls.Where(c => c.Contains("RUN_ANY_IN_BACKGROUND", StringComparison.Ordinal)).ToList();
        if (tablet)
        {
            Assert.Equal(new[]
            {
                S + "shell pm uninstall --user 0 com.miui.daemon",
                S + "shell pm uninstall --user 0 com.android.dialer",
                S + "shell pm uninstall --user 0 com.sprd.engineermode",
            }, removed);
            Assert.Equal(new[] { S + "shell cmd appops set com.android.vending RUN_ANY_IN_BACKGROUND ignore" }, background);
        }
        else
        {
            Assert.Equal(new[] { S + "shell pm uninstall --user 0 com.miui.daemon" }, removed);
            Assert.Empty(background);
        }
    }

    [Fact]
    public void Tablet_app_list_spares_the_phone_service_contacts_and_what_the_class_needs()
    {
        foreach (var app in new[]
                 {
                     Package, "com.android.phone", "com.android.contacts", "com.android.launcher3",
                     "com.google.android.inputmethod.latin", "com.google.android.gms", "com.android.vending",
                     "com.android.chrome", "com.google.android.webview", "com.android.settings",
                     "com.google.android.apps.photos", "com.google.android.documentsui", "com.google.android.apps.nbu.files",
                     "com.android.camera2", "com.google.android.calculator", "com.android.managedprovisioning",
                     "com.android.vpndialogs", "com.android.shell", "com.sprd.linkturbo", "com.sprd.systemupdate",
                 })
        {
            Assert.DoesNotContain(app, PhoneCable.TabletRemovedApps);
        }
        Assert.Equal(51, PhoneCable.TabletRemovedApps.Count);
        Assert.Empty(PhoneCable.TabletRemovedApps.Intersect(PhoneCable.RemovedApps));
        Assert.Equal(PhoneCable.TabletRemovedApps.Count, PhoneCable.TabletRemovedApps.Distinct().Count());
    }

    [Fact]
    public void Door_refusal_is_refused()
    {
        var adb = ReadyToPrepare().Set(ContentCall(), "Result: Bundle[{ok=false, error=not-shell}]\n");
        var result = Prepare(adb);
        Assert.Equal(CableProblem.Refused, result.Problem);
        Assert.DoesNotContain(adb.Calls, c => c.Contains("set-device-owner"));
    }

    [Fact]
    public void Bundle_is_read_by_key_whatever_the_order()
    {
        var adb = ReadyToPrepare().Set(ContentCall(),
            "Result: Bundle[{name=Celular 07,  thumbprint=" + Thumb + ",ok=true}]\r\n");
        var result = Prepare(adb);
        Assert.Null(result.Problem);
        Assert.Equal(Thumb, result.Phone!.SessionThumbprint);
        Assert.Equal("Celular 07", result.Phone.Name);
    }

    [Fact]
    public void Door_with_no_bundle_and_phone_gone_is_lost()
    {
        var adb = ReadyToPrepare().Set(ContentCall(), "error: device offline\n", 1);
        var result = Prepare(new DropAfter(adb, ContentCall()));
        Assert.Equal(CableProblem.Lost, result.Problem);
    }

    [Fact]
    public void Door_with_no_bundle_and_phone_present_is_refused()
    {
        var adb = ReadyToPrepare().Set(ContentCall(), "Error while accessing provider\n", 1);
        Assert.Equal(CableProblem.Refused, Prepare(adb).Problem);
    }

    [Fact]
    public void Phone_gone_before_the_dpm_is_lost_and_dpm_never_runs()
    {
        var adb = ReadyToPrepare();
        var result = Prepare(new DropAfter(adb, ContentCall()));
        Assert.Equal(CableProblem.Lost, result.Problem);
        Assert.Null(result.Phone);
        Assert.DoesNotContain(adb.Calls, c => c.Contains("set-device-owner"));
    }

    [Fact]
    public void Dpm_with_accounts_means_has_accounts()
    {
        var adb = ReadyToPrepare().Set(SetDeviceOwner,
            "java.lang.IllegalStateException: Not allowed to set the device owner because there are already some accounts on the device\n", 255);
        Assert.Equal(CableProblem.HasAccounts, Prepare(adb).Problem);
    }

    [Fact]
    public void Dpm_with_several_users_means_has_users()
    {
        var adb = ReadyToPrepare().Set(SetDeviceOwner,
            "java.lang.IllegalStateException: Not allowed to set the device owner because there are already several users on the device\n", 255);
        Assert.Equal(CableProblem.HasUsers, Prepare(adb).Problem);
    }

    [Fact]
    public void Dpm_without_the_security_switch_means_security_off()
    {
        var adb = ReadyToPrepare().Set(SetDeviceOwner,
            "java.lang.SecurityException: Neither user 2000 nor current process has android.permission.MANAGE_DEVICE_ADMINS.\n", 255);
        Assert.Equal(CableProblem.SecurityOff, Prepare(adb).Problem);
    }

    [Fact]
    public void Phone_vanishing_during_the_dpm_is_ready_but_unconfirmed()
    {
        var adb = ReadyToPrepare().Set(SetDeviceOwner, "error: device offline\n", 1);
        var result = Prepare(adb);
        Assert.Null(result.Problem);
        Assert.True(result.OwnerUnconfirmed);
        Assert.Equal("Celular 07", result.Phone!.Name);
        Assert.Equal(SetDeviceOwner, adb.Calls[^1]);
    }

    [Fact]
    public void Dpm_timing_out_is_unconfirmed()
    {
        var adb = ReadyToPrepare().Set(SetDeviceOwner, "", -1, timedOut: true);
        var result = Prepare(adb);
        Assert.True(result.OwnerUnconfirmed);
        Assert.NotNull(result.Phone);
    }

    [Fact]
    public void Unknown_dpm_failure_is_refused()
    {
        var adb = ReadyToPrepare().Set(SetDeviceOwner, "java.lang.IllegalArgumentException: weird\n", 255);
        Assert.Equal(CableProblem.Refused, Prepare(adb).Problem);
    }

    // ---- Describe ----

    [Theory]
    [InlineData(CableProblem.NoPhone, "O celular n\u00e3o est\u00e1 no cabo.\nFeche outros programas que usam o celular pelo cabo (Android Studio, scrcpy, outro Midview) e tente de novo.", "cabo")]
    [InlineData(CableProblem.ScreenLocked, "Tire a senha de bloqueio do aparelho antes (Configura\u00e7\u00f5es > Seguran\u00e7a > Bloqueio de tela > Nenhum): com ela, o PIN do professor n\u00e3o funciona neste aparelho.", "cabo")]
    [InlineData(CableProblem.TooMany, "H\u00e1 mais de um celular no cabo. Deixe s\u00f3 um.", "cabo")]
    [InlineData(CableProblem.Unauthorized, "Falta tocar em Permitir no celular.", "cabo")]
    [InlineData(CableProblem.TooOld, "Este celular \u00e9 antigo demais para o Midview (precisa do Android 10 ou mais novo).", null)]
    [InlineData(CableProblem.SecurityOff, "A chave \"Depura\u00e7\u00e3o USB (configura\u00e7\u00f5es de seguran\u00e7a)\" est\u00e1 desligada.", "chaves")]
    [InlineData(CableProblem.InstallOff, "A chave \"Instalar via USB\" est\u00e1 desligada.", "chaves")]
    [InlineData(CableProblem.HasAccounts, "Ainda h\u00e1 uma conta neste celular.", "sair-contas")]
    [InlineData(CableProblem.HasUsers, "O celular tem um Segundo espa\u00e7o ou Apps duplos. Apague-os nas Configura\u00e7\u00f5es e tente de novo.", null)]
    [InlineData(CableProblem.AlreadySchool, "Este celular j\u00e1 \u00e9 da escola. Para prepar\u00e1-lo de novo, use \"Apagar e preparar de novo\" no Midview.", null)]
    [InlineData(CableProblem.InstallDeclined, "O celular não deixou instalar. Deixe a tela dele acesa e responda às perguntas: em \"Enviar o app para uma verificação de segurança?\", toque em Não enviar; em \"Instalar via USB\", toque em Instalar (não em Recusar) antes de 10 segundos.", "cabo")]
    [InlineData(CableProblem.InstallFailed, "O Midview n\u00e3o foi instalado no celular. Se apareceu uma pergunta no celular, toque em Instalar e tente de novo.", "cabo")]
    [InlineData(CableProblem.Refused, "O celular recusou a prepara\u00e7\u00e3o. Tente de novo.", "cabo")]
    [InlineData(CableProblem.Lost, "O cabo soltou no meio. Ligue de novo e tente outra vez.", "cabo")]
    public void Describe_gives_the_exact_text_and_step(CableProblem problem, string text, string? step)
    {
        Assert.Equal((text, step), PhoneCable.Describe(problem));
    }

    [Fact]
    public void Waiting_for_the_phone_mentions_other_adb_programs_only_when_one_runs()
    {
        Assert.Equal("Esperando o celular no cabo...", PhoneCable.NoPhoneWait(otherAdb: false));
        Assert.Equal(
            "Esperando o celular no cabo...\nFeche outros programas que usam o celular pelo cabo (Android Studio, scrcpy, outro Midview) e tente de novo.",
            PhoneCable.NoPhoneWait(otherAdb: true));
    }

    // ---- Screen lock ----

    // "dumpsys window policy" of the school's Multilaser M10 (Android 10) with a PIN.
    private const string SecureKeyguard =
        "    KeyguardServiceDelegate\r\n      showing=true\r\n      showingAndNotOccluded=true\r\n      inputRestricted=true\r\n" +
        "      occluded=false\r\n      secure=true\r\n      dreaming=false\r\n      deviceHasKeyguard=true\r\n" +
        "      KeyguardStateMonitor\r\n        mIsShowing=true\r\n        mSimSecure=false\r\n";

    [Fact]
    public void A_screen_lock_stops_the_check()
    {
        var adb = Redmi().Set(S + "shell dumpsys window policy", SecureKeyguard);
        Assert.Equal(CableProblem.ScreenLocked, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Without_a_screen_lock_the_check_passes()
    {
        var adb = Redmi().Set(S + "shell dumpsys window policy", SecureKeyguard.Replace("secure=true", "secure=false", StringComparison.Ordinal));
        Assert.Null(new PhoneCable(adb).Check());
    }

    [Fact]
    public void Only_the_sim_being_secure_is_no_screen_lock()
    {
        var adb = Redmi().Set(S + "shell dumpsys window policy", "      secure=false\n        mSimSecure=true\n");
        Assert.Null(new PhoneCable(adb).Check());
    }

    // A device already the school's says so first: its lock may be the teacher's PIN.
    [Fact]
    public void Already_the_schools_comes_before_the_screen_lock()
    {
        var adb = Redmi()
            .Set(S + "shell dumpsys window policy", SecureKeyguard)
            .Set(S + "shell dpm list-owners", "1 owner:\nUser  0: admin=io.github.arnoneto1.midview.agent/.AdminReceiver,DeviceOwner\n");
        Assert.Equal(CableProblem.AlreadySchool, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Every_problem_is_described()
    {
        foreach (var problem in Enum.GetValues<CableProblem>())
        {
            Assert.False(string.IsNullOrWhiteSpace(PhoneCable.Describe(problem).Text));
        }
    }
}
