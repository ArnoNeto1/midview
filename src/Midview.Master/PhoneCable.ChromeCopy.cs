using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Midview.Master;

// An app as dumpsys shows its installed version (the update, for an updated
// system app). Abi is primaryCpuAbi ("" when it has none); StaticLibraries:
// it needs another package to run (the factory Chrome of Android 10 runs on
// com.google.android.trichromelibrary), so its own files are not all of it.
public sealed record InstalledApp(
    string Package, string VersionName, long VersionCode, string Abi, int MinSdk, bool StaticLibraries)
{
    public int? Major => PhoneCable.Major("versionName=" + VersionName);
}

// Why a copy saved on this computer did not go on the device: none fits it
// (another processor, an older Android, not newer than its own), its files
// changed on this computer, Android refused it, or the device is full.
public enum ChromeCopyMiss { DoesNotFit, Damaged, Refused, NoSpace }

// Installed: the apps installed from the saved copies. Miss: why a copy that
// was there did not go (null: all went, or there was none).
public sealed record ChromeCopyResult(IReadOnlyList<string> Installed, ChromeCopyMiss? Miss);

// The "chrome" step's copy (ChromeCopy): Chrome and WebView pulled from the
// first device the Play Store updated, and installed over the cable on the
// next ones, which then need no Google account.
public sealed partial class PhoneCable
{
    // About 250 MB each way: minutes on a slow cable.
    private static readonly TimeSpan PullTime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CopyInstallTime = TimeSpan.FromMinutes(6);

    private static readonly string[] ChromeApps = { ChromePackage, WebViewPackage };

    // The installed version's block in "dumpsys package": the first
    // "Package [x]" one, up to the next line that is not inside it (the
    // factory version follows under "Hidden system packages:").
    private static Regex AppBlock(string package) => new(
        @"^  Package \[" + Regex.Escape(package) + @"\][^\n]*\n((?:(?:    [^\n]*)?\n)*)", RegexOptions.Multiline);

    private static readonly Regex VersionCodeLine = new(@"^\s*versionCode=(\d+)", RegexOptions.Multiline);
    private static readonly Regex MinSdkField = new(@"\bminSdk=(\d+)", RegexOptions.Compiled);
    private static readonly Regex VersionNameLine = new(@"^\s*versionName=(\S+)", RegexOptions.Multiline);
    private static readonly Regex AbiLine = new(@"^\s*primaryCpuAbi=(\S+)", RegexOptions.Multiline);
    private static readonly Regex StaticLibrariesLine = new(@"^\s*usesStaticLibraries:", RegexOptions.Multiline);

    // The installed version of the app, from "dumpsys package" (null: not installed).
    internal static InstalledApp? ReadApp(string package, string dumpsys)
    {
        var block = AppBlock(package).Match(dumpsys.Replace("\r\n", "\n"));
        if (!block.Success)
        {
            return null;
        }
        var text = block.Groups[1].Value;
        var code = VersionCodeLine.Match(text);
        var name = VersionNameLine.Match(text);
        if (!code.Success || !name.Success
            || !long.TryParse(code.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var versionCode))
        {
            return null;
        }
        var minSdk = MinSdkField.Match(text) is { Success: true } m
                     && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var sdk) ? sdk : 0;
        var abi = AbiLine.Match(text) is { Success: true } a && a.Groups[1].Value != "null" ? a.Groups[1].Value : "";
        return new InstalledApp(package, name.Groups[1].Value, versionCode, abi, minSdk, StaticLibrariesLine.IsMatch(text));
    }

    // The APK files "pm path" lists (one "package:" line each), base.apk
    // first. Null when there is no base.apk or a name a copy can't hold.
    internal static IReadOnlyList<string>? ApkPaths(string pmPath)
    {
        var paths = pmPath.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("package:", StringComparison.Ordinal))
            .Select(line => line["package:".Length..])
            .ToList();
        var names = paths.Select(p => p[(p.LastIndexOf('/') + 1)..]).ToList();
        if (paths.Count == 0 || !names.All(ChromeCopy.IsFileName) || names.Distinct().Count() != names.Count
            || !names.Contains("base.apk"))
        {
            return null;
        }
        return paths.OrderBy(p => p.EndsWith("/base.apk", StringComparison.Ordinal) ? 0 : 1).ToList();
    }

    // Chrome and WebView, once up to date on the device, copied to this
    // computer when no copy as new is kept for their processor. starting is
    // called before the pull (some 400 MB, seconds over USB), only when
    // there is one. True when a copy was saved. Nothing here fails the step:
    // a pull that does not finish leaves no copy, and the next device tries.
    public bool SaveChromeCopy(ChromeCopy store, Action? starting = null)
    {
        var toSave = new List<InstalledApp>();
        foreach (var package in ChromeApps)
        {
            var app = ReadApp(package, Shell(Short, "dumpsys", "package", package).Text);
            if (app is null || app.Major < ChromeMinimum || app.StaticLibraries || !ChromeCopy.IsAbi(app.Abi)
                || store.Holds(package, app.Abi, app.VersionCode))
            {
                continue;
            }
            toSave.Add(app);
        }
        if (toSave.Count == 0)
        {
            return false;
        }
        starting?.Invoke();
        store.CleanDrafts();
        var saved = false;
        foreach (var app in toSave)
        {
            saved |= Pull(store, app);
        }
        return saved;
    }

    private bool Pull(ChromeCopy store, InstalledApp app)
    {
        if (ApkPaths(Shell(Short, "pm", "path", app.Package).Text) is not { } paths)
        {
            return false;
        }
        string draft;
        try
        {
            draft = store.NewDraft();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        var names = new List<string>();
        foreach (var path in paths)
        {
            var name = path[(path.LastIndexOf('/') + 1)..];
            var local = Path.Combine(draft, name);
            var pulled = Run(PullTime, "pull", path, local);
            if (pulled.TimedOut || pulled.ExitCode != 0 || !File.Exists(local) || new FileInfo(local).Length == 0)
            {
                store.Discard(draft);
                return false;
            }
            names.Add(name);
        }
        return store.Keep(draft, app.Package, app.VersionName, app.VersionCode, app.Abi, app.MinSdk, names);
    }

    // The apps still old on the device installed from the copies kept on
    // this computer, each from the newest that fits it: built for one of
    // its processors (ro.product.cpu.abilist), for its Android or an older
    // one, and newer than what it has. starting is called before the
    // install, only when a copy fits. Chrome goes in with all its files
    // (install-multiple), WebView likewise when it has more than one.
    public ChromeCopyResult InstallChromeCopy(ChromeCopy store, ChromeVersions versions, Action? starting = null)
    {
        var old = ChromeApps.Where(p => p == ChromePackage ? !versions.ChromeOk : !versions.WebViewOk).ToList();
        var plan = new List<(ChromeCopyManifest Manifest, string Folder)>();
        ChromeCopyMiss? miss = null;
        var abis = Array.Empty<string>();
        var sdk = 0;
        foreach (var package in old)
        {
            var copies = store.Copies(package);
            if (copies.Count == 0)
            {
                continue;
            }
            if (abis.Length == 0)
            {
                abis = Prop("ro.product.cpu.abilist").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                int.TryParse(Prop("ro.build.version.sdk"), NumberStyles.None, CultureInfo.InvariantCulture, out sdk);
            }
            var own = ReadApp(package, Shell(Short, "dumpsys", "package", package).Text)?.VersionCode ?? 0;
            var fits = copies
                .Where(c => Fits(c.Manifest, abis, sdk, own))
                .OrderByDescending(c => c.Manifest.VersionCode)
                .ThenBy(c => c.Manifest.Abi.Length == 0 ? abis.Length : Array.IndexOf(abis, c.Manifest.Abi))
                .ToList();
            if (fits.Count == 0)
            {
                miss = ChromeCopyMiss.DoesNotFit;
                continue;
            }
            plan.Add(fits[0]);
        }
        var installed = new List<string>();
        if (plan.Count == 0)
        {
            return new ChromeCopyResult(installed, miss);
        }

        starting?.Invoke();
        // As for the Midview's own install: a Xiaomi asks on its screen,
        // and only with it awake and unlocked.
        Shell(Short, "input", "keyevent", "KEYCODE_WAKEUP");
        Shell(Short, "wm", "dismiss-keyguard");
        foreach (var (manifest, folder) in plan)
        {
            if (!store.Intact(manifest, folder))
            {
                store.Remove(folder);
                miss = ChromeCopyMiss.Damaged;
                continue;
            }
            var files = manifest.Files.Select(f => Path.Combine(folder, f.Name)).ToArray();
            var command = files.Length == 1
                ? new[] { "install", "-r", files[0] }
                : new[] { "install-multiple", "-r" }.Concat(files).ToArray();
            var result = Run(CopyInstallTime, command);
            if (!result.TimedOut && result.Text.Contains("Success", StringComparison.Ordinal))
            {
                installed.Add(manifest.Package);
            }
            else
            {
                miss = result.Text.Contains("INSUFFICIENT_STORAGE", StringComparison.Ordinal)
                    ? ChromeCopyMiss.NoSpace
                    : ChromeCopyMiss.Refused;
            }
        }
        return new ChromeCopyResult(installed, miss);
    }

    // A copy fits a device with these processors and Android when it is
    // built for one of them (or for none), runs on that Android, is up to
    // date and is newer than the device's own version. Only over an app the
    // device already has (own > 0): Android then installs it only with the
    // same signature as the factory one (Google's), so a copy changed on
    // this computer, hashes and all, can never plant a fake Chrome.
    internal static bool Fits(ChromeCopyManifest copy, IReadOnlyCollection<string> abis, int sdk, long own) =>
        own > 0
        && (copy.Abi.Length == 0 || abis.Contains(copy.Abi))
        && copy.MinSdk <= sdk
        && Major("versionName=" + copy.VersionName) >= ChromeMinimum
        && copy.VersionCode > own;
}
