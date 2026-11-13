using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Midview.Master;

// The majors of Chrome and of Android System WebView on the phone (null: not
// installed). Up to date from ChromeMinimum on.
public sealed record ChromeVersions(int? Chrome, int? WebView)
{
    public bool ChromeOk => Chrome >= PhoneCable.ChromeMinimum;

    public bool WebViewOk => WebView >= PhoneCable.ChromeMinimum;

    public bool UpToDate => ChromeOk && WebViewOk;

    // The app whose Play Store page comes next: Chrome first, then WebView.
    public string? Needed => !ChromeOk ? PhoneCable.ChromePackage : !WebViewOk ? PhoneCable.WebViewPackage : null;
}

// Chrome and Android System WebView over the cable, on the "chrome" step,
// while the phone still has a Google account for the Play Store: their
// versions read with dumpsys, and the Play Store opened on the app's page for
// the teacher to tap Atualizar. Nothing is installed from here.
public sealed partial class PhoneCable
{
    // The phone app's ChromePolicy.DOWNLOADS_FROM_VERSION: the first Chrome on
    // Android that follows DownloadRestrictions ("Travar downloads"). WebView
    // is asked for the same, so it is not left years behind the browser.
    public const int ChromeMinimum = 131;

    public const string ChromePackage = "com.android.chrome";
    public const string WebViewPackage = "com.google.android.webview";
    public const string StorePackage = "com.android.vending";

    // "versionName=126.0.6478.71": the major.
    private static readonly Regex VersionName = new(@"versionName=(\d+)\.", RegexOptions.Compiled);

    // What "am start" prints when the activity did not start.
    private static readonly Regex StartFailed = new(@"Error|Exception", RegexOptions.Compiled);

    // The phone on the cable and its versions; the problem (NoPhone,
    // TooMany, Unauthorized) when there is no single allowed phone. Accounts
    // and the security switches are not looked at: a Google account is
    // what the Play Store needs here.
    public (CableProblem? Problem, ChromeVersions? Versions) LookAtChrome()
    {
        var (problem, serial) = Devices();
        _serial = serial;
        if (problem is not null)
        {
            return (problem, null);
        }
        var versions = new ChromeVersions(
            Major(Shell(Short, "dumpsys", "package", ChromePackage).Text),
            Major(Shell(Short, "dumpsys", "package", WebViewPackage).Text));
        // The phone left while it was read: both look missing.
        return versions is { Chrome: null, WebView: null } && !Present() ? (CableProblem.NoPhone, null) : (null, versions);
    }

    // The Play Store's page of the app, on the phone's screen (woken and
    // unlocked first, as for the install). False when the phone did not
    // open it: no Play Store, or the phone said no; the step then says how
    // to find the app by hand. The address is quoted for the phone's shell
    // ("?" is a pattern there).
    public bool OpenStore(string package)
    {
        Shell(Short, "input", "keyevent", "KEYCODE_WAKEUP");
        Shell(Short, "wm", "dismiss-keyguard");
        var started = Shell(
            Short, "am", "start", "-a", "android.intent.action.VIEW",
            "-d", $"'market://details?id={package}'", "-p", StorePackage);
        return !started.TimedOut
               && started.Text.Contains("Starting", StringComparison.Ordinal)
               && !StartFailed.IsMatch(started.Text);
    }

    // The highest major dumpsys shows: an updated system app is listed twice
    // (the update, then the factory one under "Hidden system packages").
    internal static int? Major(string dumpsys)
    {
        var majors = VersionName.Matches(dumpsys)
            .Select(m => int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ? major : (int?)null)
            .Where(major => major is not null)
            .ToList();
        return majors.Count == 0 ? null : majors.Max();
    }
}
