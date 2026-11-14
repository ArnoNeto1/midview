using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Midview.Master;
using Xunit;

namespace Midview.Master.Tests;

// Chrome and WebView copied from the first device the Play Store updated,
// and installed from that copy over the cable on the next ones; any copy
// that does not fit or does not go leaves the Play Store as it was.
public sealed class PhoneCableChromeCopyTests : IDisposable
{
    private const string Serial = "130821010200057";
    private const string S = "-s " + Serial + " ";
    private const string Devices = "List of devices attached\n" + Serial + "\tdevice\n\n";
    private const string ChromeDump = S + "shell dumpsys package com.android.chrome";
    private const string WebViewDump = S + "shell dumpsys package com.google.android.webview";
    private const string ChromeData = "/data/app/com.android.chrome-vFkPeB2v-min6zhfMSR2ig==/";
    private const string WebViewData = "/data/app/com.google.android.webview-EBBDZHlMpLPNph32doUhwg==/";
    private const string Abis = S + "shell getprop ro.product.cpu.abilist";
    private const string Sdk = S + "shell getprop ro.build.version.sdk";

    private static readonly string[] ChromeFiles =
        { "base.apk", "split_chrome.apk", "split_config.pt.apk", "split_dev_ui.apk", "split_on_demand.apk", "split_stack_unwinder.apk" };

    private static readonly DateTimeOffset T0 = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "midview-chrome-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // One "Package [x]" block as the Multilaser M10 printed it (trimmed).
    private static string Block(string package, string versionName, long code, string abi, int minSdk, bool staticLibraries) =>
        $"  Package [{package}] (425b0cf):\n    userId=10134\n    codePath=/data/app/{package}-x==\n" +
        $"    primaryCpuAbi={abi}\n    secondaryCpuAbi=null\n    versionCode={code} minSdk={minSdk} targetSdk=36\n" +
        $"    versionName={versionName}\n    splits=[base]\n" +
        (staticLibraries ? "    usesStaticLibraries:\n      com.google.android.trichromelibrary version:404413833\n" : "") +
        "    timeStamp=2026-11-14 11:20:09\n";

    // An app the Play Store updated: the update, then the factory one under "Hidden system packages".
    private static string Updated(string package, string versionName = "157.0.8103.51", long code = 810305100,
        string abi = "armeabi-v7a", bool staticLibraries = false) =>
        "Key Set Manager:\n  [" + package + "]\n      Signing KeySets: 8\n\nPackages:\n" +
        Block(package, versionName, code, abi, 29, staticLibraries) +
        "\nHidden system packages:\n" + Block(package, "81.0.4044.138", 404413833, "armeabi-v7a", 29, true) +
        "\nPackage Changes:\n  Sequence number=12\n";

    // The factory app alone, as on a device fresh from the box.
    private static string Factory(string package, string versionName = "81.0.4044.138", long code = 404413833) =>
        "Packages:\n" + Block(package, versionName, code, "armeabi-v7a", 29, true) + "\n";

    // Answers by the exact arguments (a queue answers in turn and keeps its
    // last); "pull" writes the device's file, "install" answers InstallAnswer.
    private sealed class FakeAdb : IAdb
    {
        public readonly List<string> Calls = new();
        public readonly Dictionary<string, string> Remote = new();
        public string InstallAnswer = "Success\n";
        public bool PullFails;
        private readonly Dictionary<string, Queue<string>> _answers = new();

        public FakeAdb Set(string key, params string[] texts)
        {
            _answers[key] = new Queue<string>(texts);
            return this;
        }

        public AdbOutput Run(TimeSpan timeout, params string[] args)
        {
            var key = string.Join(' ', args);
            Calls.Add(key);
            if (args.Length == 5 && args[2] == "pull")
            {
                if (PullFails)
                {
                    return new AdbOutput(1, "adb: error: failed to copy: device offline", false);
                }
                File.WriteAllText(args[4], Remote[args[3]]);
                return new AdbOutput(0, args[3] + ": 1 file pulled, 0 skipped.", false);
            }
            if (args.Length > 2 && args[2] is "install" or "install-multiple")
            {
                return new AdbOutput(0, InstallAnswer, false);
            }
            if (!_answers.TryGetValue(key, out var queue))
            {
                return new AdbOutput(0, "", false);
            }
            return new AdbOutput(0, queue.Count > 1 ? queue.Dequeue() : queue.Peek(), false);
        }
    }

    // The tablet with both apps updated by the Play Store.
    private static FakeAdb UpdatedTablet()
    {
        var adb = new FakeAdb()
            .Set("devices", Devices)
            .Set(ChromeDump, Updated(PhoneCable.ChromePackage))
            .Set(WebViewDump, Updated(PhoneCable.WebViewPackage, "157.0.8103.40", 810304001))
            .Set(S + "shell pm path com.android.chrome", string.Concat(ChromeFiles.Select(f => "package:" + ChromeData + f + "\n")))
            .Set(S + "shell pm path com.google.android.webview", "package:" + WebViewData + "base.apk\n");
        foreach (var file in ChromeFiles)
        {
            adb.Remote[ChromeData + file] = "chrome " + file;
        }
        adb.Remote[WebViewData + "base.apk"] = "webview base.apk";
        return adb;
    }

    // A tablet fresh from the box (Chrome and WebView 81), Android 10, arm:
    // read as such by the look and by the install, updated after it.
    private static FakeAdb OldTablet(string abis = "arm64-v8a,armeabi-v7a,armeabi", string sdk = "29") => new FakeAdb()
        .Set("devices", Devices)
        .Set(ChromeDump, Factory(PhoneCable.ChromePackage), Factory(PhoneCable.ChromePackage), Updated(PhoneCable.ChromePackage))
        .Set(WebViewDump, Factory(PhoneCable.WebViewPackage), Factory(PhoneCable.WebViewPackage),
            Updated(PhoneCable.WebViewPackage, "157.0.8103.40", 810304001))
        .Set(Abis, abis + "\n")
        .Set(Sdk, sdk + "\n");

    // The copies the updated tablet leaves on this computer.
    private ChromeCopy SavedStore()
    {
        var store = new ChromeCopy(_folder);
        var cable = new PhoneCable(UpdatedTablet());
        cable.LookAtChrome();
        Assert.True(cable.SaveChromeCopy(store));
        return store;
    }

    [Fact]
    public void The_installed_version_is_the_update_not_the_hidden_factory_one()
    {
        var app = PhoneCable.ReadApp(PhoneCable.ChromePackage, Updated(PhoneCable.ChromePackage));

        Assert.Equal(new InstalledApp(PhoneCable.ChromePackage, "157.0.8103.51", 810305100, "armeabi-v7a", 29, false), app);
        Assert.Equal(157, app!.Major);
        var factory = PhoneCable.ReadApp(PhoneCable.ChromePackage, Factory(PhoneCable.ChromePackage));
        Assert.Equal((404413833L, true), (factory!.VersionCode, factory.StaticLibraries));
        Assert.Null(PhoneCable.ReadApp(PhoneCable.ChromePackage, "Unable to find package: com.android.chrome\n"));
        Assert.Equal("", PhoneCable.ReadApp(PhoneCable.ChromePackage,
            Updated(PhoneCable.ChromePackage, abi: "null").Replace("\n", "\r\n"))!.Abi);
    }

    [Fact]
    public void Pm_path_lists_the_files_base_first()
    {
        Assert.Equal(
            new[] { ChromeData + "base.apk", ChromeData + "split_chrome.apk" },
            PhoneCable.ApkPaths("package:" + ChromeData + "split_chrome.apk\r\npackage:" + ChromeData + "base.apk\r\n"));
        Assert.Null(PhoneCable.ApkPaths("package:" + ChromeData + "split_chrome.apk\n"));
        Assert.Null(PhoneCable.ApkPaths(""));
        Assert.Null(PhoneCable.ApkPaths("package:/data/app/x/base.apk\npackage:/data/app/x/odd name.apk\n"));
    }

    [Fact]
    public void An_updated_device_is_copied_to_this_computer_once()
    {
        var store = new ChromeCopy(_folder);
        var adb = UpdatedTablet();
        var cable = new PhoneCable(adb);
        cable.LookAtChrome();
        var starts = 0;

        Assert.True(cable.SaveChromeCopy(store, () => starts++));

        Assert.Equal(1, starts);
        Assert.Equal(
            ChromeFiles.Select(f => ChromeData + f).Append(WebViewData + "base.apk"),
            adb.Calls.Where(c => c.StartsWith(S + "pull ", StringComparison.Ordinal)).Select(c => c.Split(' ')[3]));
        var (chrome, chromeFolder) = Assert.Single(store.Copies(PhoneCable.ChromePackage));
        Assert.Equal(("157.0.8103.51", 810305100L, "armeabi-v7a", 29), (chrome.VersionName, chrome.VersionCode, chrome.Abi, chrome.MinSdk));
        Assert.Equal(ChromeFiles, chrome.Files.Select(f => f.Name));
        Assert.Equal("chrome split_chrome.apk", File.ReadAllText(Path.Combine(chromeFolder, "split_chrome.apk")));
        var webView = Assert.Single(store.Copies(PhoneCable.WebViewPackage)).Manifest;
        Assert.Equal(810304001, webView.VersionCode);
        Assert.Equal(new[] { "base.apk" }, webView.Files.Select(f => f.Name));

        // The next updated device finds both kept: nothing is pulled.
        var again = UpdatedTablet();
        var next = new PhoneCable(again);
        next.LookAtChrome();
        Assert.False(next.SaveChromeCopy(store, () => starts++));
        Assert.Equal(1, starts);
        Assert.DoesNotContain(again.Calls, c => c.Contains(" pull ") || c.Contains("pm path"));
    }

    [Fact]
    public void A_newer_version_replaces_the_kept_copy()
    {
        var store = SavedStore();
        var adb = UpdatedTablet().Set(ChromeDump, Updated(PhoneCable.ChromePackage, "158.0.1.2", 820000000));
        var cable = new PhoneCable(adb);
        cable.LookAtChrome();

        Assert.True(cable.SaveChromeCopy(store));

        Assert.Equal(820000000, Assert.Single(store.Copies(PhoneCable.ChromePackage)).Manifest.VersionCode);
        Assert.DoesNotContain(adb.Calls, c => c.Contains("pull " + WebViewData));
    }

    [Fact]
    public void A_chrome_that_needs_a_static_library_is_never_copied()
    {
        var store = new ChromeCopy(_folder);
        var adb = UpdatedTablet()
            .Set(ChromeDump, Updated(PhoneCable.ChromePackage, staticLibraries: true))
            .Set(WebViewDump, Factory(PhoneCable.WebViewPackage, "131.0.6778.39", 677803900));
        var cable = new PhoneCable(adb);
        cable.LookAtChrome();

        Assert.False(cable.SaveChromeCopy(store));

        Assert.Empty(store.Copies(PhoneCable.ChromePackage));
        Assert.Empty(store.Copies(PhoneCable.WebViewPackage));
        Assert.DoesNotContain(adb.Calls, c => c.Contains(" pull "));
    }

    [Fact]
    public void A_pull_that_fails_leaves_no_copy_and_no_draft()
    {
        var store = new ChromeCopy(_folder);
        var adb = UpdatedTablet();
        adb.PullFails = true;
        var cable = new PhoneCable(adb);
        cable.LookAtChrome();

        Assert.False(cable.SaveChromeCopy(store));

        Assert.Empty(store.Copies(PhoneCable.ChromePackage));
        Assert.Empty(Directory.GetDirectories(store.Root));
    }

    [Fact]
    public void An_old_device_gets_the_copy_that_fits_over_the_cable()
    {
        var store = SavedStore();
        var adb = OldTablet();
        var cable = new PhoneCable(adb);
        var (_, versions) = cable.LookAtChrome();
        var starts = 0;

        var result = cable.InstallChromeCopy(store, versions!, () => starts++);

        Assert.Equal(new[] { PhoneCable.ChromePackage, PhoneCable.WebViewPackage }, result.Installed);
        Assert.Null(result.Miss);
        Assert.Equal(1, starts);
        var chromeFolder = store.Copies(PhoneCable.ChromePackage)[0].Folder;
        var webViewFolder = store.Copies(PhoneCable.WebViewPackage)[0].Folder;
        var installs = adb.Calls.Where(c => c.Contains(" install")).ToList();
        Assert.Equal(
            new[]
            {
                S + "install-multiple -r " + string.Join(' ', ChromeFiles.Select(f => Path.Combine(chromeFolder, f))),
                S + "install -r " + Path.Combine(webViewFolder, "base.apk"),
            },
            installs);
        // Woken and unlocked first, as for the Midview's own install.
        var wake = adb.Calls.IndexOf(S + "shell input keyevent KEYCODE_WAKEUP");
        Assert.InRange(wake, 0, adb.Calls.IndexOf(installs[0]));
        Assert.DoesNotContain(adb.Calls, c => c.Contains("am start"));
    }

    [Fact]
    public void Only_the_old_app_is_installed()
    {
        var store = SavedStore();
        var adb = OldTablet().Set(ChromeDump, Updated(PhoneCable.ChromePackage));
        var cable = new PhoneCable(adb);
        var (_, versions) = cable.LookAtChrome();
        Assert.True(versions!.ChromeOk);

        var result = cable.InstallChromeCopy(store, versions);

        Assert.Equal(new[] { PhoneCable.WebViewPackage }, result.Installed);
        Assert.Single(adb.Calls, c => c.Contains(" install"));
    }

    [Theory]
    [InlineData("x86_64,x86", "29")]
    [InlineData("arm64-v8a,armeabi-v7a,armeabi", "28")]
    public void A_copy_for_another_processor_or_a_newer_android_does_not_go(string abis, string sdk)
    {
        var store = SavedStore();
        var adb = OldTablet(abis, sdk);
        var cable = new PhoneCable(adb);
        var (_, versions) = cable.LookAtChrome();
        var starts = 0;

        var result = cable.InstallChromeCopy(store, versions!, () => starts++);

        Assert.Empty(result.Installed);
        Assert.Equal(ChromeCopyMiss.DoesNotFit, result.Miss);
        Assert.Equal(0, starts);
        Assert.DoesNotContain(adb.Calls, c => c.Contains(" install"));
    }

    [Fact]
    public void A_copy_not_newer_than_the_devices_own_does_not_go()
    {
        var copy = new ChromeCopyManifest(PhoneCable.ChromePackage, "157.0.8103.51", 810305100, "armeabi-v7a", 29,
            new[] { new ChromeCopyFile("base.apk", 1, "00") });
        var abis = new[] { "arm64-v8a", "armeabi-v7a" };

        Assert.True(PhoneCable.Fits(copy, abis, 29, 404413833));
        Assert.False(PhoneCable.Fits(copy, abis, 29, 810305100));
        Assert.False(PhoneCable.Fits(copy, abis, 28, 404413833));
        Assert.False(PhoneCable.Fits(copy, new[] { "arm64-v8a" }, 29, 404413833));
        Assert.True(PhoneCable.Fits(copy with { Abi = "" }, new[] { "x86_64" }, 29, 0));
        Assert.False(PhoneCable.Fits(copy with { VersionName = "126.0.6478.71" }, abis, 29, 0));
    }

    [Theory]
    [InlineData("Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: Package com.android.chrome signatures do not match]", ChromeCopyMiss.Refused)]
    [InlineData("Failure [INSTALL_FAILED_INSUFFICIENT_STORAGE]", ChromeCopyMiss.NoSpace)]
    [InlineData("adb: error: failed to read: device offline", ChromeCopyMiss.Refused)]
    public void An_install_android_refuses_says_why(string answer, ChromeCopyMiss miss)
    {
        var store = SavedStore();
        var adb = OldTablet();
        adb.InstallAnswer = answer;
        var cable = new PhoneCable(adb);
        var (_, versions) = cable.LookAtChrome();

        var result = cable.InstallChromeCopy(store, versions!);

        Assert.Empty(result.Installed);
        Assert.Equal(miss, result.Miss);
        // The copy stays for the next device.
        Assert.Single(store.Copies(PhoneCable.ChromePackage));
    }

    [Fact]
    public void A_damaged_copy_is_not_installed_and_is_dropped()
    {
        var store = SavedStore();
        var folder = store.Copies(PhoneCable.ChromePackage)[0].Folder;
        File.AppendAllText(Path.Combine(folder, "split_chrome.apk"), "!");
        var adb = OldTablet();
        var cable = new PhoneCable(adb);
        var (_, versions) = cable.LookAtChrome();

        var result = cable.InstallChromeCopy(store, versions!);

        Assert.Equal(new[] { PhoneCable.WebViewPackage }, result.Installed);
        Assert.Equal(ChromeCopyMiss.Damaged, result.Miss);
        Assert.Empty(store.Copies(PhoneCable.ChromePackage));
        Assert.DoesNotContain(adb.Calls, c => c.Contains("install-multiple"));
    }

    [Fact]
    public void With_no_copy_kept_nothing_is_asked_of_the_device()
    {
        var adb = OldTablet();
        var cable = new PhoneCable(adb);
        var (_, versions) = cable.LookAtChrome();
        var calls = adb.Calls.Count;

        var result = cable.InstallChromeCopy(new ChromeCopy(_folder), versions!, () => throw new InvalidOperationException());

        Assert.Empty(result.Installed);
        Assert.Null(result.Miss);
        Assert.Equal(calls, adb.Calls.Count);
    }

    // The window's loop, without the window: the copy tried before any
    // Play Store page, and saved once both are up to date.
    private static string? Look(PhoneSetupProgress progress, PhoneCable cable, ChromeCopy store, DateTimeOffset now)
    {
        var (problem, versions) = cable.LookAtChrome();
        if (progress.WantsCopy(versions))
        {
            var result = cable.InstallChromeCopy(store, versions!, progress.CopyStarting);
            progress.CopyEnded(result);
            if (result.Installed.Count > 0)
            {
                (problem, versions) = cable.LookAtChrome();
            }
        }
        if (progress.WantsSave(versions))
        {
            cable.SaveChromeCopy(store, progress.SaveStarting);
            progress.SaveEnded();
        }
        return progress.ChromeLooked(problem, versions, now);
    }

    [Fact]
    public void The_next_device_is_done_with_no_play_store()
    {
        var store = SavedStore();
        var adb = OldTablet();
        var progress = PhoneSetupProgressChromeTests.AtChrome();

        Assert.Null(Look(progress, new PhoneCable(adb), store, T0));

        Assert.Equal(ChromeStage.Done, progress.Chrome);
        Assert.Equal("conta-google", progress.StepId);
        Assert.Equal(new ChromeVersions(157, 157), progress.ChromeSeen);
        Assert.Null(progress.CopyMiss);
        Assert.Equal(ChromeWork.None, progress.Work);
        Assert.DoesNotContain(adb.Calls, c => c.Contains("am start") || c.Contains(" pull "));
    }

    [Fact]
    public void A_refused_copy_falls_back_to_the_play_store_and_is_not_tried_again()
    {
        var store = SavedStore();
        var adb = OldTablet().Set(ChromeDump, Factory(PhoneCable.ChromePackage)).Set(WebViewDump, Factory(PhoneCable.WebViewPackage));
        adb.InstallAnswer = "Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE]";
        var progress = PhoneSetupProgressChromeTests.AtChrome();
        var cable = new PhoneCable(adb);

        Assert.Equal(PhoneCable.ChromePackage, Look(progress, cable, store, T0));
        Assert.Equal(ChromeCopyMiss.Refused, progress.CopyMiss);
        Assert.Equal(ChromeStage.Updating, progress.Chrome);

        Assert.Null(Look(progress, cable, store, T0.AddSeconds(3)));
        Assert.Equal(2, adb.Calls.Count(c => c.Contains(" install")));
    }

    [Fact]
    public void The_first_device_updated_by_the_play_store_is_copied_before_moving_on()
    {
        var store = new ChromeCopy(_folder);
        var adb = UpdatedTablet()
            .Set(ChromeDump, Factory(PhoneCable.ChromePackage), Updated(PhoneCable.ChromePackage))
            .Set(WebViewDump, Factory(PhoneCable.WebViewPackage), Updated(PhoneCable.WebViewPackage, "157.0.8103.40", 810304001));
        var progress = PhoneSetupProgressChromeTests.AtChrome();
        var cable = new PhoneCable(adb);

        // Nothing kept yet: the Play Store, with no word about a copy.
        Assert.Equal(PhoneCable.ChromePackage, Look(progress, cable, store, T0));
        Assert.Null(progress.CopyMiss);
        Assert.DoesNotContain(adb.Calls, c => c.Contains(" install"));

        // The teacher updated both: they are copied, and the step is done.
        Assert.Null(Look(progress, cable, store, T0.AddSeconds(3)));
        Assert.Equal(ChromeStage.Done, progress.Chrome);
        Assert.Single(store.Copies(PhoneCable.ChromePackage));
        Assert.Single(store.Copies(PhoneCable.WebViewPackage));
        Assert.Equal(7, adb.Calls.Count(c => c.Contains(" pull ")));
    }
}
