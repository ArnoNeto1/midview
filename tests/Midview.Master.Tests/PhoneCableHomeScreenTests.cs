using System;
using System.Collections.Generic;
using System.Linq;
using Midview.Master;
using Xunit;

namespace Midview.Master.Tests;

// PhoneCable.Prepare and the home screen: arranged on a Xiaomi in "Midview
// otimizado" only, after the setup is handed over and before the Mi
// sign-out; a failure only leaves the teacher a note.
public sealed class PhoneCableHomeScreenTests
{
    private const string Serial = "79a61f85";
    private const string Apk = @"C:\Midview\agent.apk";
    private const string Admin = "io.github.arnoneto1.midview.agent/.AdminReceiver";
    private const string Thumb = "AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12";

    // A freshly reset Redmi whose launcher is the fake one: every "shell"
    // command the cable's own answers do not cover goes to the launcher.
    private sealed class Redmi : IAdb
    {
        public readonly FakeMiuiLauncher Launcher = new();
        public readonly List<string> Calls = new();
        public string Miui = "V140\n";

        private readonly Dictionary<string, string> _shell = new()
        {
            ["getprop ro.build.version.sdk"] = "33\n",
            ["getprop persist.security.adbinput"] = "1\n",
            ["getprop persist.security.adbinstall"] = "1\n",
            ["dumpsys account"] = "  Accounts: 0\n",
            ["pm list users"] = "Users:\n\tUserInfo{0:Propriet\u00e1rio:c13} running\n",
            ["dpm list-owners"] = "no owners\n",
            ["pm list packages io.github.arnoneto1.midview.agent"] = "",
            ["content call --uri content://io.github.arnoneto1.midview.agent.setup --method prepare --extra setup:s:"
             + CableSetupPayload.Encode("07", Thumb, "Lab", "pw")] =
                "Result: Bundle[{ok=true, thumbprint=" + Thumb + ", name=Celular 07}]\n",
            ["dpm set-device-owner " + Admin] = "Success: Device owner set\n",
        };

        public AdbOutput Run(TimeSpan timeout, params string[] args)
        {
            var key = string.Join(' ', args);
            Calls.Add(key);
            if (key == "devices")
            {
                return new AdbOutput(0, "List of devices attached\n" + Serial + "\tdevice\n\n", false);
            }
            if (key == "-s " + Serial + " install -r " + Apk)
            {
                return new AdbOutput(0, "Success\n", false);
            }
            var command = args.Skip(3).ToArray();
            var line = string.Join(' ', command);
            if (line == "getprop ro.miui.ui.version.name")
            {
                return new AdbOutput(0, Miui, false);
            }
            return _shell.TryGetValue(line, out var answer) ? new AdbOutput(0, answer, false) : Launcher.Shell(command);
        }
    }

    private static CableResult Prepare(Redmi adb, List<CableStage> stages, bool optimize = true)
    {
        var elapsed = TimeSpan.Zero;
        var cable = new PhoneCable(adb)
        {
            MakeHomeScreen = shell => new PhoneHomeScreen(shell, pause => elapsed += pause, () => elapsed),
        };
        return cable.Prepare(Apk, "07", Thumb, "Lab", "pw", stages.Add, optimize);
    }

    private static int First(Redmi adb, string part) => adb.Calls.FindIndex(c => c.Contains(part, StringComparison.Ordinal));

    [Fact]
    public void An_optimized_xiaomi_gets_its_home_screen_arranged_after_the_handover()
    {
        var adb = new Redmi();
        adb.Launcher.Pages[0].Add(FakeMiuiLauncher.Item.Icon("Midview"));
        var stages = new List<CableStage>();

        var result = Prepare(adb, stages);

        Assert.Null(result.Problem);
        Assert.NotNull(result.Phone);
        Assert.False(result.HomeByHand);
        Assert.Equal(
            new[] { CableStage.Checking, CableStage.Installing, CableStage.Wifi, CableStage.Pairing, CableStage.HomeScreen, CableStage.Owner },
            stages);
        Assert.True(adb.Launcher.Drawer);
        Assert.True(adb.Launcher.Empty);
        Assert.Equal(FakeMiuiLauncher.ClassApps, adb.Launcher.Dock);
        Assert.Equal("1", adb.Launcher.System[PhoneHomeScreen.LockSetting]);
        // After the setup is handed over, before the lighter phone and the Owner step.
        var arranged = First(adb, PhoneHomeScreen.SettingsActivity);
        Assert.True(arranged > First(adb, "content call"));
        Assert.True(First(adb, "rm -f " + PhoneHomeScreen.DumpFile) < First(adb, "window_animation_scale"));
        Assert.Equal("-s " + Serial + " shell dpm set-device-owner " + Admin, adb.Calls[^1]);
    }

    [Fact]
    public void A_home_screen_that_could_not_be_arranged_leaves_a_note_and_the_phone_ready()
    {
        var adb = new Redmi();
        adb.Launcher.DrawerTapWorks = false;
        var stages = new List<CableStage>();

        var result = Prepare(adb, stages);

        Assert.Null(result.Problem);
        Assert.NotNull(result.Phone);
        Assert.True(result.HomeByHand);
        Assert.Contains(CableStage.HomeScreen, stages);
        Assert.Equal("-s " + Serial + " shell dpm set-device-owner " + Admin, adb.Calls[^1]);
    }

    [Fact]
    public void Only_the_midview_never_touches_the_home_screen()
    {
        var adb = new Redmi();
        var stages = new List<CableStage>();

        var result = Prepare(adb, stages, optimize: false);

        Assert.NotNull(result.Phone);
        Assert.False(result.HomeByHand);
        Assert.DoesNotContain(CableStage.HomeScreen, stages);
        Assert.DoesNotContain(adb.Calls, c => c.Contains("uiautomator", StringComparison.Ordinal)
                                              || c.Contains(PhoneHomeScreen.SettingsActivity, StringComparison.Ordinal)
                                              || c.Contains(PhoneHomeScreen.LockSetting, StringComparison.Ordinal)
                                              || c.Contains("input swipe", StringComparison.Ordinal)
                                              || c.Contains("draganddrop", StringComparison.Ordinal));
        Assert.Empty(adb.Launcher.Pages[0]);
        Assert.False(adb.Launcher.Drawer);
    }

    [Fact]
    public void A_phone_without_miui_is_skipped()
    {
        var adb = new Redmi { Miui = "\n" };
        var stages = new List<CableStage>();

        var result = Prepare(adb, stages);

        Assert.NotNull(result.Phone);
        Assert.False(result.HomeByHand);
        Assert.DoesNotContain(CableStage.HomeScreen, stages);
        Assert.DoesNotContain(adb.Calls, c => c.Contains("uiautomator", StringComparison.Ordinal)
                                              || c.Contains(PhoneHomeScreen.SettingsActivity, StringComparison.Ordinal)
                                              || c.Contains(PhoneHomeScreen.LockSetting, StringComparison.Ordinal));
    }

    [Fact]
    public void The_ready_page_says_how_to_arrange_it_by_hand()
    {
        var note = PhoneSetupGuide.HomeScreenByHand;
        Assert.StartsWith("Arrume a tela inicial à mão:", note, StringComparison.Ordinal);
        Assert.Contains("Com Gaveta de apps", note, StringComparison.Ordinal);
        Assert.Contains("Remover", note, StringComparison.Ordinal);
        Assert.Contains("Chrome, Câmera, Galeria, Gerenciador de Arquivos e Calculadora", note, StringComparison.Ordinal);
        Assert.Contains("Bloquear layout da Tela inicial", note, StringComparison.Ordinal);
    }

    // The cable list's "Arrumando a tela inicial" and the ready page's note,
    // until the next phone.
    [Fact]
    public void The_assistant_remembers_a_home_screen_left_by_hand_until_the_next_phone()
    {
        static PhoneSetupProgress Finishing()
        {
            var progress = new PhoneSetupProgress();
            progress.Start(hasWifi: true, gathered: true);
            progress.SetNumber("7");
            while (progress.StepId != "cabo")
            {
                PhoneSetupProgressTests.NextOrUpToDate(progress);
            }
            Assert.True(progress.CableSeen());
            return progress;
        }
        var at = new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

        var progress = Finishing();
        progress.CableStageReached(CableStage.Pairing);
        Assert.False(progress.HomeScreenShown);
        progress.CableStageReached(CableStage.HomeScreen);
        progress.CableStageReached(CableStage.Owner);
        Assert.True(progress.HomeScreenShown);
        progress.CablePrepared(at, homeByHand: true);
        Assert.True(progress.HomeByHand);

        progress.Again();
        Assert.False(progress.HomeByHand);
        Assert.False(progress.HomeScreenShown);

        var arranged = Finishing();
        arranged.CablePrepared(at);
        Assert.False(arranged.HomeByHand);
    }
}
