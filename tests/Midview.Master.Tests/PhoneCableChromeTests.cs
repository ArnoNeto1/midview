using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Midview.Master;
using Xunit;

namespace Midview.Master.Tests;

// Chrome and WebView over the cable: their versions read with dumpsys, and
// the Play Store page opened on the phone, polled until the update lands.
public sealed class PhoneCableChromeTests
{
    private const string Serial = "79a61f85";
    private const string S = "-s " + Serial + " ";
    private const string Devices = "List of devices attached\n" + Serial + "\tdevice\n\n";
    private const string ChromeDump = S + "shell dumpsys package com.android.chrome";
    private const string WebViewDump = S + "shell dumpsys package com.google.android.webview";
    private const string OpenChrome =
        S + "shell am start -a android.intent.action.VIEW -d 'market://details?id=com.android.chrome' -p com.android.vending";
    private const string OpenWebView =
        S + "shell am start -a android.intent.action.VIEW -d 'market://details?id=com.google.android.webview' -p com.android.vending";
    private const string Started =
        "Starting: Intent { act=android.intent.action.VIEW dat=market://details/... pkg=com.android.vending }\n";

    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    // What the Redmi 10C printed (trimmed), with the factory Chrome only.
    private static string Dump(string package, params string[] versions) =>
        "Packages:\n" + string.Concat(versions.Select(v =>
            $"  Package [{package}] (3697df6):\n    versionCode=647807132 minSdk=29 targetSdk=34\n    versionName={v}\n"));

    // Answers by the exact arguments; a queue answers in turn and keeps its last answer.
    private sealed class FakeAdb : IAdb
    {
        public readonly List<string> Calls = new();
        private readonly Dictionary<string, Queue<AdbOutput>> _answers = new();

        public FakeAdb Set(string key, params string[] texts)
        {
            _answers[key] = new Queue<AdbOutput>(texts.Select(t => new AdbOutput(0, t, false)));
            return this;
        }

        public AdbOutput Run(TimeSpan timeout, params string[] args)
        {
            var key = string.Join(' ', args);
            Calls.Add(key);
            if (!_answers.TryGetValue(key, out var queue))
            {
                return new AdbOutput(0, "", false);
            }
            return queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        }
    }

    private static FakeAdb Phone(string chrome, string webView) => new FakeAdb()
        .Set("devices", Devices)
        .Set(ChromeDump, Dump("com.android.chrome", chrome))
        .Set(WebViewDump, Dump("com.google.android.webview", webView));

    [Theory]
    [InlineData("    versionName=126.0.6478.71\n", 126)]
    [InlineData("    versionName=131.0.6778.39\n", 131)]
    [InlineData("    versionName=142.0.7444.48\n    versionName=126.0.6478.71\n", 142)]
    [InlineData("    versionName=126.0.6478.71\n    versionName=142.0.7444.48\n", 142)]
    [InlineData("Unable to find package: com.android.chrome\n", null)]
    [InlineData("", null)]
    public void The_major_is_the_highest_version_dumpsys_lists(string dumpsys, int? major) =>
        Assert.Equal(major, PhoneCable.Major(dumpsys));

    [Fact]
    public void The_minimum_is_the_phone_apps_download_lock_version()
    {
        var policy = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Midview.Android", "app", "src", "main", "java",
            "io", "github", "arnoneto1", "midview", "agent", "sites", "ChromePolicy.kt"));
        var from = Regex.Match(policy, @"DOWNLOADS_FROM_VERSION\s*=\s*(\d+)");
        Assert.True(from.Success);
        Assert.Equal(int.Parse(from.Groups[1].Value), PhoneCable.ChromeMinimum);
        Assert.Equal(131, PhoneCable.ChromeMinimum);
    }

    [Fact]
    public void The_versions_are_read_from_the_phone_on_the_cable()
    {
        var adb = Phone("126.0.6478.71", "126.0.6478.71");

        var (problem, versions) = new PhoneCable(adb).LookAtChrome();

        Assert.Null(problem);
        Assert.Equal(new ChromeVersions(126, 126), versions);
        Assert.False(versions!.ChromeOk);
        Assert.Equal(PhoneCable.ChromePackage, versions.Needed);
        Assert.Equal(new[] { "devices", ChromeDump, WebViewDump }, adb.Calls);
    }

    [Theory]
    [InlineData("List of devices attached\n\n", CableProblem.NoPhone)]
    [InlineData("List of devices attached\n" + Serial + "\tunauthorized\n\n", CableProblem.Unauthorized)]
    [InlineData("List of devices attached\n" + Serial + "\tdevice\n0123\tdevice\n\n", CableProblem.TooMany)]
    public void Without_one_allowed_phone_nothing_is_read(string devices, CableProblem expected)
    {
        var adb = new FakeAdb().Set("devices", devices);

        var (problem, versions) = new PhoneCable(adb).LookAtChrome();

        Assert.Equal(expected, problem);
        Assert.Null(versions);
        Assert.Equal(new[] { "devices" }, adb.Calls);
    }

    [Fact]
    public void A_google_account_does_not_stop_the_look()
    {
        var adb = Phone("131.0.6778.39", "131.0.6778.39").Set(S + "shell dumpsys account", "Accounts: 1\n  Account {name=escola@gmail.com, type=com.google}\n");

        var (problem, versions) = new PhoneCable(adb).LookAtChrome();

        Assert.Null(problem);
        Assert.True(versions!.UpToDate);
        Assert.DoesNotContain(adb.Calls, c => c.Contains("dumpsys account"));
    }

    [Fact]
    public void The_store_opens_on_the_apps_page_with_the_screen_awake()
    {
        var adb = Phone("126.0.6478.71", "126.0.6478.71").Set(OpenChrome, Started);
        var cable = new PhoneCable(adb);
        cable.LookAtChrome();

        Assert.True(cable.OpenStore(PhoneCable.ChromePackage));

        Assert.Equal(
            new[] { S + "shell input keyevent KEYCODE_WAKEUP", S + "shell wm dismiss-keyguard", OpenChrome },
            adb.Calls.Skip(3));
    }

    [Theory]
    [InlineData("Starting: Intent { act=android.intent.action.VIEW }\nError: Activity not started, unable to resolve Intent")]
    [InlineData("Exception occurred while executing 'start':\njava.lang.SecurityException: Permission Denial")]
    [InlineData("")]
    public void A_store_the_phone_did_not_open_says_so(string answer)
    {
        var adb = Phone("126.0.6478.71", "126.0.6478.71").Set(OpenChrome, answer);
        var cable = new PhoneCable(adb);
        cable.LookAtChrome();

        Assert.False(cable.OpenStore(PhoneCable.ChromePackage));
    }

    // The window's loop, without the window: a look every few seconds, the
    // page opened when the progress asks, until both apps are up to date.
    private static (PhoneSetupProgress Progress, int Looks) Poll(FakeAdb adb, int maxLooks)
    {
        var progress = PhoneSetupProgressChromeTests.AtChrome();
        var cable = new PhoneCable(adb);
        var now = T0;
        var looks = 0;
        while (progress.WatchesChrome && looks < maxLooks)
        {
            progress.ChromeTick(now);
            if (!progress.WatchesChrome)
            {
                break;
            }
            var (problem, versions) = cable.LookAtChrome();
            looks++;
            if (progress.ChromeLooked(problem, versions, now) is { } package)
            {
                progress.StoreOpened(cable.OpenStore(package));
            }
            now += TimeSpan.FromSeconds(3);
        }
        return (progress, looks);
    }

    [Fact]
    public void Polling_opens_chrome_then_webview_and_moves_on_once_both_are_updated()
    {
        var adb = new FakeAdb()
            .Set("devices", Devices)
            .Set(ChromeDump,
                Dump("com.android.chrome", "126.0.6478.71"),
                Dump("com.android.chrome", "126.0.6478.71"),
                Dump("com.android.chrome", "142.0.7444.48", "126.0.6478.71"))
            .Set(WebViewDump,
                Dump("com.google.android.webview", "126.0.6478.71"),
                Dump("com.google.android.webview", "126.0.6478.71"),
                Dump("com.google.android.webview", "126.0.6478.71"),
                Dump("com.google.android.webview", "126.0.6478.71"),
                Dump("com.google.android.webview", "142.0.7444.48", "126.0.6478.71"))
            .Set(OpenChrome, Started)
            .Set(OpenWebView, Started);

        var (progress, looks) = Poll(adb, maxLooks: 20);

        Assert.Equal(5, looks);
        Assert.Equal(ChromeStage.Done, progress.Chrome);
        Assert.Equal("conta-google", progress.StepId);
        Assert.Equal(new ChromeVersions(142, 142), progress.ChromeSeen);
        // Each page opened once, Chrome first.
        Assert.Equal(new[] { OpenChrome, OpenWebView }, adb.Calls.Where(c => c.Contains("am start")));
        Assert.Null(PhoneSetupGuide.ChromeWarning(progress.ChromeSkipped, progress.WebViewSkipped, progress.ChromeSeen));
    }

    [Fact]
    public void A_phone_already_up_to_date_never_sees_the_store()
    {
        var adb = Phone("142.0.7444.48", "142.0.7444.48");

        var (progress, looks) = Poll(adb, maxLooks: 20);

        Assert.Equal(1, looks);
        Assert.Equal("conta-google", progress.StepId);
        Assert.DoesNotContain(adb.Calls, c => c.Contains("am start"));
    }

    [Fact]
    public void Polling_stops_when_the_update_takes_too_long()
    {
        var adb = Phone("126.0.6478.71", "126.0.6478.71").Set(OpenChrome, Started);

        var (progress, looks) = Poll(adb, maxLooks: 1000);

        Assert.Equal(ChromeStage.TimedOut, progress.Chrome);
        Assert.Equal("chrome", progress.StepId);
        Assert.False(progress.WatchesChrome);
        // 10 minutes at a look every 3 seconds.
        Assert.Equal((int)(PhoneSetupProgress.ChromeTimeout.TotalSeconds / 3), looks);
        Assert.Single(adb.Calls, c => c.Contains("am start"));
    }

    [Fact]
    public void A_store_that_did_not_open_keeps_polling_for_the_update_done_by_hand()
    {
        var adb = new FakeAdb()
            .Set("devices", Devices)
            .Set(ChromeDump, Dump("com.android.chrome", "126.0.6478.71"), Dump("com.android.chrome", "131.0.6778.39"))
            .Set(WebViewDump, Dump("com.google.android.webview", "131.0.6778.39"))
            .Set(OpenChrome, "Error: Activity not started, unable to resolve Intent");
        var progress = PhoneSetupProgressChromeTests.AtChrome();
        var cable = new PhoneCable(adb);

        var (p, v) = cable.LookAtChrome();
        progress.StoreOpened(cable.OpenStore(progress.ChromeLooked(p, v, T0)!));
        Assert.True(progress.StoreFailed);
        Assert.True(progress.WatchesChrome);

        (p, v) = cable.LookAtChrome();
        progress.ChromeLooked(p, v, T0.AddSeconds(3));
        Assert.Equal("conta-google", progress.StepId);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Midview.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
