using System;
using System.Collections.Generic;
using System.Linq;
using Midview.Master;
using Midview.Protocol;
using Xunit;

namespace Midview.Master.Tests;

public sealed class PhoneCableTests
{
    private const string Apk = @"C:\Midview\agent.apk";
    private const string Package = "io.github.arnoneto1.midview.agent";
    private const string Admin = "io.github.arnoneto1.midview.agent/.AdminReceiver";
    private const string Thumb = "AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12";
    private const string SetDeviceOwner = "shell dpm set-device-owner " + Admin;

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
            if (args[0] == "install")
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

    // The real answers of the school's Redmi 10C (MIUI 14, Android 13), freshly reset.
    private static FakeAdb Redmi() => new FakeAdb()
        .Set("devices", "List of devices attached\n79a61f85\tdevice\n\n")
        .Set("shell getprop ro.build.version.sdk", "33\r\n")
        .Set("shell getprop ro.miui.ui.version.name", "V140\n")
        .Set("shell getprop persist.security.adbinput", "1\n")
        .Set("shell getprop persist.security.adbinstall", "1\n")
        .Set("shell dumpsys account", "Accounts: 0\n  Accounts: 0\n  Active Sync Adapters\n")
        .Set("shell pm list users", "Users:\n\tUserInfo{0:Propriet\u00e1rio:c13} running\n")
        .Set("shell dpm list-owners", "no owners\n");

    private static string ContentCall() =>
        "shell content call --uri content://io.github.arnoneto1.midview.agent.setup --method prepare --extra setup:s:"
        + CableSetupPayload.Encode("07", Thumb, "Lab", "pw");

    private static FakeAdb ReadyToPrepare() => Redmi()
        .Set("install -r " + Apk, "Performing Streamed Install\nSuccess\n")
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
        var adb = Redmi().Set("shell getprop ro.build.version.sdk", "28\n");
        Assert.Equal(CableProblem.TooOld, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Android_10_is_enough()
    {
        var adb = Redmi().Set("shell getprop ro.build.version.sdk", "29\n");
        Assert.Null(new PhoneCable(adb).Check());
    }

    [Fact]
    public void Security_switch_off_on_miui()
    {
        var adb = Redmi().Set("shell getprop persist.security.adbinput", "0\n");
        Assert.Equal(CableProblem.SecurityOff, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Security_switch_missing_on_miui_counts_as_off()
    {
        var adb = Redmi().Set("shell getprop persist.security.adbinput", "\n");
        Assert.Equal(CableProblem.SecurityOff, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Install_switch_off_on_miui()
    {
        var adb = Redmi().Set("shell getprop persist.security.adbinstall", "0\n");
        Assert.Equal(CableProblem.InstallOff, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Phone_without_miui_does_not_check_the_switches()
    {
        var adb = Redmi()
            .Set("shell getprop ro.miui.ui.version.name", "\n")
            .Set("shell getprop persist.security.adbinput", "")
            .Set("shell getprop persist.security.adbinstall", "");
        Assert.Null(new PhoneCable(adb).Check());
        Assert.DoesNotContain("shell getprop persist.security.adbinput", adb.Calls);
        Assert.DoesNotContain("shell getprop persist.security.adbinstall", adb.Calls);
    }

    [Fact]
    public void An_account_on_the_phone()
    {
        var adb = Redmi().Set("shell dumpsys account", "Accounts: 1\n  Account {name=a@b.c, type=com.google}\n");
        Assert.Equal(CableProblem.HasAccounts, new PhoneCable(adb).Check());
    }

    [Fact]
    public void An_account_with_extra_whitespace_and_indent()
    {
        var adb = Redmi().Set("shell dumpsys account", "User UserInfo{0:x:c13}:\r\n    Accounts:   2\r\n");
        Assert.Equal(CableProblem.HasAccounts, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Dual_apps_user_means_has_users()
    {
        var adb = Redmi().Set("shell pm list users",
            "Users:\n\tUserInfo{0:Propriet\u00e1rio:c13} running\n\tUserInfo{999:XSpace:801013} running\n");
        Assert.Equal(CableProblem.HasUsers, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Phone_already_owned_by_the_school_app()
    {
        var adb = Redmi().Set("shell dpm list-owners",
            "1 owner:\nUser  0: admin=" + Admin + ",DeviceOwner,Affiliated\n");
        Assert.Equal(CableProblem.AlreadySchool, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Another_owner_is_not_the_school()
    {
        var adb = Redmi().Set("shell dpm list-owners", "1 owner:\nUser  0: admin=com.other/.Admin,DeviceOwner\n");
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

        var afterCheck = adb.Calls.SkipWhile(c => c != "shell dpm list-owners").Skip(1).ToList();
        Assert.Equal(new[]
        {
            "shell pm list packages " + Package,
            "install -r " + Apk,
            "shell cmd appops set " + Package + " 10008 allow",
            "shell cmd deviceidle whitelist +" + Package,
            ContentCall(),
            "devices",
            SetDeviceOwner,
        }, afterCheck);
        Assert.Equal(
            new[] { CableStage.Checking, CableStage.Installing, CableStage.Wifi, CableStage.Pairing, CableStage.Owner },
            stages);
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
    public void Installed_package_is_cleared_before_the_install()
    {
        var adb = ReadyToPrepare().Set("shell pm list packages " + Package, "package:" + Package + "\n");

        Assert.Null(Prepare(adb).Problem);

        var clear = adb.Calls.IndexOf("shell pm clear " + Package);
        Assert.True(clear >= 0);
        Assert.True(clear < adb.Calls.IndexOf("install -r " + Apk));
    }

    [Fact]
    public void Fresh_phone_is_not_cleared()
    {
        var adb = ReadyToPrepare();
        Prepare(adb);
        Assert.DoesNotContain("shell pm clear " + Package, adb.Calls);
    }

    [Fact]
    public void A_check_problem_stops_before_any_change()
    {
        var adb = ReadyToPrepare().Set("shell dumpsys account", "Accounts: 1\n");
        var result = Prepare(adb);
        Assert.Equal(CableProblem.HasAccounts, result.Problem);
        Assert.Null(result.Phone);
        Assert.DoesNotContain(adb.Calls, c => c.StartsWith("install"));
    }

    [Fact]
    public void Install_blocked_by_the_phone_means_install_switch_off()
    {
        var adb = ReadyToPrepare().Set("install -r " + Apk,
            "Performing Streamed Install\nadb: failed to install x.apk: Failure [INSTALL_FAILED_USER_RESTRICTED: Install canceled by user]\n", 1);
        Assert.Equal(CableProblem.InstallOff, Prepare(adb).Problem);
        Assert.DoesNotContain(adb.Calls, c => c.Contains("content call"));
    }

    [Fact]
    public void Other_install_failure()
    {
        var adb = ReadyToPrepare().Set("install -r " + Apk, "Failure [INSTALL_FAILED_INSUFFICIENT_STORAGE]\n", 1);
        Assert.Equal(CableProblem.InstallFailed, Prepare(adb).Problem);
    }

    [Fact]
    public void Install_that_times_out_fails()
    {
        var adb = ReadyToPrepare().Set("install -r " + Apk, "", -1, timedOut: true);
        Assert.Equal(CableProblem.InstallFailed, Prepare(adb).Problem);
    }

    [Fact]
    public void Release_commands_failing_do_not_stop_the_setup()
    {
        var adb = ReadyToPrepare()
            .Set("shell cmd appops set " + Package + " 10008 allow", "Error: unknown op", 255)
            .Set("shell cmd deviceidle whitelist +" + Package, "Error", 255);
        var result = Prepare(adb);
        Assert.Null(result.Problem);
        Assert.NotNull(result.Phone);
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
    [InlineData(CableProblem.NoPhone, "O celular n\u00e3o est\u00e1 no cabo.", "cabo")]
    [InlineData(CableProblem.TooMany, "H\u00e1 mais de um celular no cabo. Deixe s\u00f3 um.", "cabo")]
    [InlineData(CableProblem.Unauthorized, "Falta tocar em Permitir no celular.", "cabo")]
    [InlineData(CableProblem.TooOld, "Este celular \u00e9 antigo demais para o Midview (precisa do Android 10 ou mais novo).", null)]
    [InlineData(CableProblem.SecurityOff, "A chave \"Depura\u00e7\u00e3o USB (configura\u00e7\u00f5es de seguran\u00e7a)\" est\u00e1 desligada.", "chaves")]
    [InlineData(CableProblem.InstallOff, "A chave \"Instalar via USB\" est\u00e1 desligada.", "chaves")]
    [InlineData(CableProblem.HasAccounts, "Ainda h\u00e1 uma conta neste celular.", "sair-contas")]
    [InlineData(CableProblem.HasUsers, "O celular tem um Segundo espa\u00e7o ou Apps duplos. Apague-os nas Configura\u00e7\u00f5es e tente de novo.", null)]
    [InlineData(CableProblem.AlreadySchool, "Este celular j\u00e1 \u00e9 da escola. Para prepar\u00e1-lo de novo, use \"Apagar e preparar de novo\" no Midview.", null)]
    [InlineData(CableProblem.InstallFailed, "O Midview n\u00e3o foi instalado no celular. Se apareceu uma pergunta no celular, toque em Instalar e tente de novo.", "cabo")]
    [InlineData(CableProblem.Refused, "O celular recusou a prepara\u00e7\u00e3o. Tente de novo.", "cabo")]
    [InlineData(CableProblem.Lost, "O cabo soltou no meio. Ligue de novo e tente outra vez.", "cabo")]
    public void Describe_gives_the_exact_text_and_step(CableProblem problem, string text, string? step)
    {
        Assert.Equal((text, step), PhoneCable.Describe(problem));
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
