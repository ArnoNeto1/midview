using System;
using System.Collections.Generic;
using System.Linq;
using Midview.Master;
using Xunit;

namespace Midview.Master.Tests;

// PhoneCable.Prepare on a Redmi still in the school's Mi account: signed out
// over the cable when the password is saved, sent back to the step otherwise.
public sealed class PhoneCableMiTests
{
    private const string Serial = "79a61f85";
    private const string Apk = @"C:\Midview\agent.apk";
    private const string Admin = "io.github.arnoneto1.midview.agent/.AdminReceiver";
    private const string Thumb = "AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12";
    private const string Password = "Escola2026";

    // The Redmi's answers ("shell" commands without "-s <serial> shell"),
    // its accounts, and the Mi settings screen whose "Sair" signs out at once.
    private sealed class MiRedmi : IAdb
    {
        public readonly List<string> Calls = new();
        public string Accounts = "  Accounts: 1\n    Account {name=6512345678, type=com.xiaomi}\n";
        public bool SignOutWorks = true;
        public string AdbInputAfter = "1\n";
        public bool SignedOut;
        public string Packages = "";

        private readonly Dictionary<string, string> _shell = new()
        {
            ["getprop ro.build.version.sdk"] = "33\n",
            ["getprop ro.miui.ui.version.name"] = "V140\n",
            ["getprop persist.security.adbinstall"] = "1\n",
            ["pm list users"] = "Users:\n\tUserInfo{0:Propriet\u00e1rio:c13} running\n",
            ["dpm list-owners"] = "no owners\n",
            ["content call --uri content://io.github.arnoneto1.midview.agent.setup --method prepare --extra setup:s:"
             + CableSetupPayload.Encode("07", Thumb, "Lab", "pw")] =
                "Result: Bundle[{ok=true, thumbprint=" + Thumb + ", name=Celular 07}]\n",
            ["dpm set-device-owner " + Admin] = "Success: Device owner set\n",
            ["cat " + MiSignOut.DumpFile] =
                "<hierarchy><node text=\"Sair\" resource-id=\"com.xiaomi.account:id/logout_btn\" class=\"android.widget.Button\" " +
                "package=\"com.xiaomi.account\" password=\"false\" scrollable=\"false\" bounds=\"[60,1300][660,1380]\" /></hierarchy>",
        };

        public AdbOutput Run(TimeSpan timeout, params string[] args)
        {
            var key = string.Join(' ', args);
            Calls.Add(key);
            if (key == "devices")
            {
                return Out("List of devices attached\n" + Serial + "\tdevice\n\n");
            }
            if (key == "-s " + Serial + " install -r " + Apk)
            {
                return Out("Success\n");
            }
            var shell = "-s " + Serial + " shell ";
            if (!key.StartsWith(shell, StringComparison.Ordinal))
            {
                return Out("");
            }
            var command = key[shell.Length..];
            if (command == "dumpsys account")
            {
                return Out(SignedOut ? "  Accounts: 0\n" : Accounts);
            }
            if (command == "getprop persist.security.adbinput")
            {
                return Out(SignedOut ? AdbInputAfter : "1\n");
            }
            if (AsksPassword)
            {
                switch (command)
                {
                    case "input tap 360 1340":
                        Showing = Prompt(focused: false);
                        break;
                    case "input tap 360 650":
                        Showing = Prompt(focused: true);
                        break;
                    case "input tap 530 840" when Typed:
                        SignedOut = true;
                        Showing = "<hierarchy></hierarchy>";
                        break;
                    case "cat " + MiSignOut.DumpFile when Showing.Length > 0:
                        return Out(Showing);
                }
            }
            else if (command == "input tap 360 1340" && SignOutWorks)
            {
                SignedOut = true;
            }
            if (command == "pm list packages io.github.arnoneto1.midview.agent")
            {
                return Out(Packages);
            }
            return Out(_shell.TryGetValue(command, out var answer) ? answer : "");
        }

        // "Sair" asks for the password: a prompt whose field takes the
        // focus when tapped, and OK signs out once something was typed.
        public bool AsksPassword;
        public bool Typed;
        public string Showing = "";
        public readonly List<(string Args, string Input)> Inputs = new();

        public AdbOutput RunWithInput(TimeSpan timeout, string input, params string[] args)
        {
            var key = string.Join(' ', args);
            Calls.Add(key);
            Inputs.Add((key, input));
            Typed |= key == "-s " + Serial + " shell" && input == "input text '" + Password + "'\n";
            return Out("");
        }

        private static string Prompt(bool focused) =>
            "<hierarchy><node text=\"\" resource-id=\"com.xiaomi.account:id/input_pwd_view\" class=\"android.widget.EditText\" " +
            "package=\"com.xiaomi.account\" password=\"true\" scrollable=\"false\" focused=\"" + (focused ? "true" : "false") + "\" " +
            "bounds=\"[60,600][660,700]\" />" +
            "<node text=\"OK\" resource-id=\"android:id/button1\" class=\"android.widget.Button\" package=\"com.xiaomi.account\" " +
            "password=\"false\" scrollable=\"false\" bounds=\"[400,800][660,880]\" /></hierarchy>";

        private static AdbOutput Out(string text) => new(0, text, false);
    }

    private static CableResult Prepare(MiRedmi adb, string? miPassword, List<CableStage> stages)
    {
        var elapsed = TimeSpan.Zero;
        var cable = new PhoneCable(adb)
        {
            MakeSignOut = (shell, secretShell) => new MiSignOut(shell, secretShell, pause => elapsed += pause, () => elapsed),
        };
        return cable.Prepare(Apk, "07", Thumb, "Lab", "pw", stages.Add, miPassword: miPassword);
    }

    // The password goes to "adb shell"'s standard input: no adb command line
    // ever holds it.
    [Fact]
    public void The_password_is_typed_through_standard_input_never_in_arguments()
    {
        var adb = new MiRedmi { AsksPassword = true };
        var result = Prepare(adb, Password, new List<CableStage>());

        Assert.Null(result.Problem);
        Assert.Equal(new[] { ("-s " + Serial + " shell", "input text '" + Password + "'\n") }, adb.Inputs);
        Assert.DoesNotContain(adb.Calls, c => c.Contains(Password, StringComparison.Ordinal));
    }

    [Fact]
    public void A_phone_in_the_mi_account_is_signed_out_and_prepared()
    {
        var adb = new MiRedmi();
        var stages = new List<CableStage>();
        var result = Prepare(adb, Password, stages);

        Assert.Null(result.Problem);
        Assert.NotNull(result.Phone);
        Assert.Equal(
            new[]
            {
                CableStage.Checking, CableStage.Installing, CableStage.Wifi, CableStage.Pairing,
                CableStage.HomeScreen, CableStage.MiSignOut, CableStage.Owner,
            },
            stages);
        // Signed out last: after the setup is handed over and the home screen, right before the Owner step.
        var signOut = adb.Calls.IndexOf("-s " + Serial + " shell input tap 360 1340");
        Assert.True(signOut > adb.Calls.FindIndex(c => c.Contains("content call", StringComparison.Ordinal)));
        Assert.True(signOut > adb.Calls.FindIndex(c => c.Contains(PhoneHomeScreen.SettingsActivity, StringComparison.Ordinal)));
        // The optimized cleanup (it removes Xiaomi cloud apps) only after the sign-out.
        Assert.True(signOut < adb.Calls.IndexOf("-s " + Serial + " shell settings put global window_animation_scale 0"));
        Assert.True(signOut < adb.Calls.IndexOf("-s " + Serial + " shell pm list packages"));
        Assert.Equal("-s " + Serial + " shell dpm set-device-owner " + Admin, adb.Calls[^1]);
    }

    [Fact]
    public void Without_the_saved_password_the_account_goes_back_to_the_step()
    {
        var adb = new MiRedmi();
        var stages = new List<CableStage>();
        Assert.Equal(CableProblem.HasAccounts, Prepare(adb, null, stages).Problem);
        Assert.DoesNotContain(CableStage.MiSignOut, stages);
        Assert.DoesNotContain(adb.Calls, c => c.Contains("uiautomator", StringComparison.Ordinal));
    }

    [Fact]
    public void A_google_account_beside_it_is_left_to_the_teacher()
    {
        var adb = new MiRedmi
        {
            Accounts = "  Accounts: 2\n    Account {name=6512345678, type=com.xiaomi}\n    Account {name=escola@gmail.com, type=com.google}\n",
        };
        var stages = new List<CableStage>();
        Assert.Equal(CableProblem.HasAccounts, Prepare(adb, Password, stages).Problem);
        Assert.DoesNotContain(CableStage.MiSignOut, stages);
    }

    [Fact]
    public void A_sign_out_that_does_not_work_leaves_the_phone_and_says_so()
    {
        var adb = new MiRedmi { SignOutWorks = false };
        var stages = new List<CableStage>();
        var result = Prepare(adb, Password, stages);

        Assert.Equal(CableProblem.MiSignOutFailed, result.Problem);
        Assert.Null(result.Phone);
        Assert.DoesNotContain(adb.Calls, c => c.Contains("set-device-owner", StringComparison.Ordinal));
        Assert.Equal(("O Midview não conseguiu sair da conta Mi sozinho. Saia dela no celular e tente de novo.", "sair-contas"),
            PhoneCable.Describe(CableProblem.MiSignOutFailed));
    }

    [Fact]
    public void Switches_miui_turns_off_with_the_account_do_not_matter_after_it()
    {
        var adb = new MiRedmi { AdbInputAfter = "0\n" };
        var result = Prepare(adb, Password, new List<CableStage>());
        Assert.Null(result.Problem);
        Assert.NotNull(result.Phone);
    }

    [Fact]
    public void Tentar_de_novo_after_signing_out_by_hand_prepares_the_phone_with_the_app_left_on_it()
    {
        var adb = new MiRedmi { SignOutWorks = false };
        Assert.Equal(CableProblem.MiSignOutFailed, Prepare(adb, Password, new List<CableStage>()).Problem);

        // Signed out by hand: the app is still on the phone, which is not its owner.
        adb.SignedOut = true;
        adb.Packages = "package:io.github.arnoneto1.midview.agent\n";
        adb.Calls.Clear();
        var stages = new List<CableStage>();
        var result = Prepare(adb, null, stages);

        Assert.Null(result.Problem);
        Assert.NotNull(result.Phone);
        Assert.DoesNotContain(CableStage.MiSignOut, stages);
        Assert.Contains("-s " + Serial + " shell pm uninstall io.github.arnoneto1.midview.agent", adb.Calls);
        Assert.Contains("-s " + Serial + " install -r " + Apk, adb.Calls);
    }

    [Fact]
    public void Check_alone_still_reports_the_mi_account()
    {
        Assert.Equal(CableProblem.HasAccounts, new PhoneCable(new MiRedmi()).Check());
    }
}
