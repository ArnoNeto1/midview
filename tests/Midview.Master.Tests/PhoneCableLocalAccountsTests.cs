using System;
using System.Collections.Generic;
using System.Linq;
using Midview.Master;
using Xunit;

namespace Midview.Master.Tests;

// PhoneCable on the school's Android 10 tablet: its Contacts app keeps a
// local "Phone" account, not a reason to stop and hidden only around
// "dpm set-device-owner"; its dpm has no "list-owners".
public sealed class PhoneCableLocalAccountsTests
{
    private const string Serial = "130821010200057";
    private const string S = "-s " + Serial + " ";
    private const string Apk = @"C:\Midview\agent.apk";
    private const string Admin = "io.github.arnoneto1.midview.agent/.AdminReceiver";
    private const string Thumb = "AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12";
    private const string SetDeviceOwner = S + "shell dpm set-device-owner " + Admin;
    private const string Disable = S + "shell pm disable-user --user 0 com.android.contacts";
    private const string Enable = S + "shell pm enable com.android.contacts";

    // What the school's Multilaser M10 (Unisoc, Android 10) lists, trimmed.
    private const string TabletAccounts =
        "User UserInfo{0:Propriet\u00e1rio:13}:\n" +
        "  Accounts: 1\n" +
        "    Account {name=Phone, type=sprd.com.android.account.phone}\n" +
        "  \n" +
        "  RegisteredServicesCache: 8 services\n" +
        "    ServiceInfo: AuthenticatorDescription {type=sprd.com.android.account.sim}, ComponentInfo{com.android.contacts/com.sprd.contacts.account.SimAuthenticationService}, uid 10066\n" +
        "    ServiceInfo: AuthenticatorDescription {type=com.google}, ComponentInfo{com.google.android.gms/com.google.android.gms.auth.account.authenticator.GoogleAccountAuthenticatorService}, uid 10114\n" +
        "    ServiceInfo: AuthenticatorDescription {type=sprd.com.android.account.phone}, ComponentInfo{com.android.contacts/com.sprd.contacts.account.PhoneAuthenticationService}, uid 10066\n" +
        "    ServiceInfo: AuthenticatorDescription {type=sprd.com.android.account.usim}, ComponentInfo{com.android.contacts/com.sprd.contacts.account.USimAuthenticationService}, uid 10066\n";

    // Answers by the exact arguments; whatever is not set answers an empty
    // success. Records every call in order; Throw makes one command fail.
    private sealed class FakeAdb : IAdb
    {
        public readonly List<string> Calls = new();
        private readonly Dictionary<string, AdbOutput> _answers = new();
        public string? Throw;

        public FakeAdb Set(string key, string text, bool timedOut = false)
        {
            _answers[key] = new AdbOutput(0, text, timedOut);
            return this;
        }

        public AdbOutput Run(TimeSpan timeout, params string[] args)
        {
            var key = string.Join(' ', args);
            Calls.Add(key);
            if (key == Throw)
            {
                throw new InvalidOperationException("adb stopped");
            }
            return _answers.TryGetValue(key, out var answer) ? answer : new AdbOutput(0, "", false);
        }
    }

    private static FakeAdb Tablet(string accounts = TabletAccounts) => new FakeAdb()
        .Set("devices", "List of devices attached\n" + Serial + "\tdevice\n\n")
        .Set(S + "shell getprop ro.build.version.sdk", "29\n")
        .Set(S + "shell getprop ro.miui.ui.version.name", "\n")
        .Set(S + "shell dumpsys account", accounts)
        .Set(S + "shell pm list users", "Users:\n\tUserInfo{0:Propriet\u00e1rio:13} running\n")
        .Set(S + "shell dpm list-owners", "usage: dpm [subcommand] [options]\n\nError: unknown command 'list-owners'\n")
        .Set(S + "shell dumpsys device_policy", "Current Device Policy Manager state:\n  Immutable state:\n")
        .Set(S + "install -r " + Apk, "Performing Streamed Install\nSuccess\n")
        .Set(S + "shell content call --uri content://io.github.arnoneto1.midview.agent.setup --method prepare --extra setup:s:"
             + CableSetupPayload.Encode("01", Thumb, "Lab", "pw", AndroidDeviceKind.Tablet, optimize: false),
            "Result: Bundle[{ok=true, thumbprint=" + Thumb + ", name=Tablet 01}]\n")
        .Set(SetDeviceOwner, "Success: Device owner set to package ComponentInfo{" + Admin + "}\n");

    private static CableResult Prepare(IAdb adb) =>
        new PhoneCable(adb).Prepare(Apk, "01", Thumb, "Lab", "pw", _ => { }, optimize: false, kind: AndroidDeviceKind.Tablet);

    [Fact]
    public void A_local_contacts_account_does_not_stop_the_check()
    {
        Assert.Null(new PhoneCable(Tablet()).Check());
    }

    [Fact]
    public void Android_10_reads_the_owner_from_dumpsys()
    {
        var adb = Tablet().Set(S + "shell dumpsys device_policy",
            "Current Device Policy Manager state:\r\n  Device Owner: \r\n    admin=ComponentInfo{"
            + "io.github.arnoneto1.midview.agent/io.github.arnoneto1.midview.agent.AdminReceiver}\r\n"
            + "    name=\r\n    package=io.github.arnoneto1.midview.agent\r\n");
        Assert.Equal(CableProblem.AlreadySchool, new PhoneCable(adb).Check());
    }

    [Fact]
    public void Android_10_with_another_owner_is_not_the_schools()
    {
        var adb = Tablet().Set(S + "shell dumpsys device_policy",
            "  Device Owner: \n    admin=ComponentInfo{com.other/com.other.Admin}\n"
            + "  Enabled Device Admins (User 0, provisioningState: 3):\n"
            + "    io.github.arnoneto1.midview.agent/.AdminReceiver:\n");
        Assert.Null(new PhoneCable(adb).Check());
    }

    [Fact]
    public void A_google_account_beside_the_local_one_still_stops_it()
    {
        var adb = Tablet(TabletAccounts.Replace(
            "  Accounts: 1\n",
            "  Accounts: 2\n    Account {name=escola@gmail.com, type=com.google}\n",
            StringComparison.Ordinal));
        Assert.Equal(CableProblem.HasAccounts, new PhoneCable(adb).Check());
    }

    [Fact]
    public void A_local_account_whose_app_is_not_listed_stops_it()
    {
        var accounts = string.Join('\n', TabletAccounts.Split('\n')
            .Where(l => !l.Contains("type=sprd.com.android.account.phone}, ComponentInfo", StringComparison.Ordinal)));
        Assert.Equal(CableProblem.HasAccounts, new PhoneCable(Tablet(accounts)).Check());
    }

    [Fact]
    public void Accounts_counted_without_types_still_stop_it()
    {
        Assert.Equal(CableProblem.HasAccounts, new PhoneCable(Tablet("  Accounts: 1\n")).Check());
    }

    [Fact]
    public void Contacts_is_off_only_around_the_owner_step()
    {
        var adb = Tablet();
        var result = Prepare(adb);

        Assert.NotNull(result.Phone);
        Assert.Null(result.Problem);
        Assert.Equal("Tablet 01", result.Phone!.Name);
        var disable = adb.Calls.IndexOf(Disable);
        var owner = adb.Calls.IndexOf(SetDeviceOwner);
        var enable = adb.Calls.IndexOf(Enable);
        Assert.True(disable >= 0 && disable < owner && owner < enable);
        Assert.Equal(adb.Calls.Count - 1, enable);
        Assert.Single(adb.Calls, Disable);
        Assert.Single(adb.Calls, Enable);
    }

    [Fact]
    public void Contacts_comes_back_when_the_owner_step_is_refused()
    {
        var adb = Tablet().Set(SetDeviceOwner,
            "java.lang.IllegalStateException: Not allowed to set the device owner because there are already some accounts on the device\n");

        Assert.Equal(CableProblem.HasAccounts, Prepare(adb).Problem);
        Assert.Equal(Enable, adb.Calls[^1]);
    }

    [Fact]
    public void Contacts_comes_back_when_the_owner_step_times_out()
    {
        var adb = Tablet().Set(SetDeviceOwner, "", timedOut: true);

        Assert.True(Prepare(adb).OwnerUnconfirmed);
        Assert.Equal(Enable, adb.Calls[^1]);
    }

    [Fact]
    public void Contacts_comes_back_when_adb_fails_during_the_owner_step()
    {
        var adb = Tablet();
        adb.Throw = SetDeviceOwner;

        Assert.Throws<InvalidOperationException>(() => Prepare(adb));
        Assert.Equal(Enable, adb.Calls[^1]);
    }

    [Fact]
    public void Nothing_is_turned_off_without_local_accounts()
    {
        var adb = Tablet("  Accounts: 0\n  RegisteredServicesCache: 0 services\n");

        Assert.NotNull(Prepare(adb).Phone);
        Assert.DoesNotContain(adb.Calls, c => c.Contains("pm disable-user", StringComparison.Ordinal));
        Assert.DoesNotContain(adb.Calls, c => c.Contains("pm enable", StringComparison.Ordinal));
    }
}
