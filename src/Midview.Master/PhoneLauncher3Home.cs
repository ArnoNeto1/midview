using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace Midview.Master;

// Arranges the home screen of a tablet or phone whose launcher is Android's
// own (Launcher3, com.android.launcher3) for a class over the cable, the way
// the teacher would on its screen: every icon, folder and widget taken off
// the home pages, the class's apps in the dock (Chrome, Câmera, Fotos, Files
// and Calculadora, the ones installed), and, in the launcher's settings,
// "Adicionar ícone à tela inicial" and "Mostrar Google App" off. Tried on the
// school's Multilaser M10 (Android 10) on 2026-11-13: a long press shows no
// "Remover" there; an item is dragged ("input draganddrop") to the
// "Remover" target the launcher shows at the top only while dragging,
// centred when alone, on the left half when "Desinstalar" shows beside it
// (an app the student installed), so it is aimed at the middle first and at
// the left quarter after a miss, never at the right half. A folder dropped
// there goes with every shortcut in it (the apps stay in the drawer). The
// date and the search bar cannot be removed and stay. Every step looks at
// the screen first; anything unexpected ends it, and the teacher arranges
// the rest by hand.
public sealed class PhoneLauncher3Home
{
    // Measured on the M10: about 3 s a screen read, 7 s an item removed,
    // 9 s an app put in the dock; nine items on two pages (a folder and a
    // widget among them), four dock apps taken off and five put in took
    // 168 s; a tablet already arranged, 18 s.
    public static readonly TimeSpan Limit = TimeSpan.FromMinutes(4);

    internal const string Launcher = "com.android.launcher3";
    internal const string DumpFile = "/data/local/tmp/midview-home.xml";
    internal const string SettingsAction = "android.intent.action.APPLICATION_PREFERENCES";

    // The class's apps, in the dock's order, with the names Launcher3 gives them.
    internal static readonly IReadOnlyList<(string Package, string[] Names)> DockApps = new[]
    {
        ("com.android.chrome", new[] { "Chrome" }),
        ("com.android.camera2", new[] { "Câmera", "Camera" }),
        ("com.google.android.apps.photos", new[] { "Fotos", "Photos", "Google Fotos" }),
        ("com.google.android.apps.nbu.files", new[] { "Files", "Files by Google", "Arquivos" }),
        ("com.google.android.calculator", new[] { "Calculadora", "Calculator" }),
    };

    private const string Id = Launcher + ":id/";
    private const string Title = "android:id/title";
    private const string Switch = "android:id/switch_widget";

    // The settings rows turned off: "Adicionar ícone à tela inicial" (a new
    // app's icon on the home) and, where there is one, the Google app left
    // of the home. Matched without case or accents (HomeScreenNode.Plain).
    private static readonly string[] AddIconTitles = { "adicionar icone", "add icon", "add app icon" };
    private const string GoogleTitle = "google";

    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Moment = TimeSpan.FromSeconds(0.5);

    private readonly Func<string[], AdbOutput> _shell;
    private readonly Action<TimeSpan> _pause;
    private readonly Func<TimeSpan> _elapsed;

    // shell: one "adb shell" command on the device. pause and elapsed: the
    // clock (a fake one in the tests).
    public PhoneLauncher3Home(Func<string[], AdbOutput> shell, Action<TimeSpan> pause, Func<TimeSpan> elapsed)
    {
        _shell = shell;
        _pause = pause;
        _elapsed = elapsed;
    }

    public static PhoneLauncher3Home WithRealClock(Func<string[], AdbOutput> shell)
    {
        var watch = Stopwatch.StartNew();
        return new PhoneLauncher3Home(shell, Thread.Sleep, () => watch.Elapsed);
    }

    // Whether "cmd package resolve-activity --brief" names Launcher3 as the
    // home: its last line is the activity ("com.android.launcher3/..."), or
    // the chooser while no home is the default.
    internal static bool IsHome(string resolved) =>
        resolved.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.Length > 0)?
            .StartsWith(Launcher + "/", StringComparison.Ordinal) == true;

    // True once the settings are off, the home pages empty and the dock
    // right. False on any failure, with the device left as far as it got
    // (on its home screen).
    public bool Run()
    {
        try
        {
            return Drive();
        }
        finally
        {
            Key("KEYCODE_HOME");
            Shell("rm", "-f", DumpFile);
        }
    }

    private bool InTime => _elapsed() < Limit;

    private AdbOutput Shell(params string[] command) => _shell(command);

    private bool Drive()
    {
        Key("KEYCODE_WAKEUP");
        Shell("wm", "dismiss-keyguard");
        if (!SettingsOff())
        {
            return false;
        }
        var dock = InstalledDockApps();
        return Emptied(dock) && Docked(dock);
    }

    private List<string[]> InstalledDockApps()
    {
        var installed = Shell("pm", "list", "packages").Text
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("package:", StringComparison.Ordinal))
            .Select(line => line["package:".Length..])
            .ToHashSet(StringComparer.Ordinal);
        return DockApps.Where(app => installed.Contains(app.Package)).Select(app => app.Names).ToList();
    }

    // The launcher's settings: each switch to turn off tapped until it
    // reads off. "Adicionar ícone" must be there; the Google one may not.
    private bool SettingsOff()
    {
        var started = Shell("am", "start", "-W", "-a", SettingsAction, "-p", Launcher);
        if (started.TimedOut || !started.Text.Contains("Starting", StringComparison.Ordinal)
            || started.Text.Contains("Error", StringComparison.Ordinal))
        {
            return false;
        }
        _pause(Settle);
        var taps = 0;
        var looks = 0;
        while (InTime && taps < 4 && looks < 10)
        {
            looks++;
            if (Screen() is not { } screen)
            {
                _pause(Second);
                continue;
            }
            var rows = SwitchRows(screen);
            var addIcon = rows.FirstOrDefault(row => AddIconTitles.Any(row.Title.Contains));
            if (addIcon.Switch is null)
            {
                _pause(Second);
                continue;
            }
            var on = rows.Where(row => row.Switch.Checked
                                       && (row.Title == addIcon.Title || row.Title.Contains(GoogleTitle, StringComparison.Ordinal)))
                .Select(row => row.Switch)
                .FirstOrDefault();
            if (on is null)
            {
                Key("KEYCODE_HOME");
                _pause(Settle);
                return true;
            }
            Tap(on);
            taps++;
            _pause(Second);
        }
        return false;
    }

    // A settings screen's rows that have a switch: the row's title (plain)
    // and its switch. A row is the element holding one title and a switch.
    private static List<(string Title, HomeScreenNode Switch)> SwitchRows(HomeScreenNode screen) =>
        screen.Descendants()
            .Where(n => n.Package == Launcher && n.Shown
                        && n.Descendants().Count(t => t.Id == Title) == 1 && n.Has(Switch))
            .Select(row => (HomeScreenNode.Plain(row.Find(Title)!.Text), row.Find(Switch)!))
            .Where(row => row.Item2.Shown)
            .ToList();

    // Every home page emptied (icons, folders, widgets), and the dock too
    // unless it already holds the class's apps in order.
    private bool Emptied(List<string[]> dock)
    {
        Key("KEYCODE_HOME");
        _pause(Settle);
        var failures = 0;
        var backs = 0;
        var misses = 0;
        var swipes = 0;
        var dropped = -1;
        while (InTime && failures < 4 && backs < 8 && swipes < 12)
        {
            if (Screen() is not { } screen)
            {
                failures++;
                _pause(Second);
                continue;
            }
            if (!screen.Descendants().Any(n => n.Package == Launcher))
            {
                // Something else on the screen (the uninstall question): nothing for the cable.
                Key("KEYCODE_BACK");
                return false;
            }
            var (width, height) = Size(screen);
            // A menu, the drawer, the widgets, or "Item removido" left open: closed.
            if (Ours(screen, Id + "workspace") is not { } workspace
                || Ours(screen, Id + "apps_view") is { } apps && apps.Top < height / 2
                || screen.Has(Id + "deep_shortcuts_container") || screen.Has(Id + "folder_content"))
            {
                backs++;
                Key("KEYCODE_BACK");
                _pause(Second);
                continue;
            }
            backs = 0;
            var pages = workspace.Children.Where(n => n.Shown).ToList();
            if (pages.FirstOrDefault(p => p.Left < width / 2 && p.Right > width / 2) is not { } page)
            {
                failures++;
                Key("KEYCODE_HOME");
                _pause(Settle);
                continue;
            }
            var items = PageItems(page).ToList();
            var docked = DockItems(screen).ToList();
            var count = items.Count + docked.Count;
            // The last item dropped is still there: "Remover" was not where it was aimed.
            misses = dropped < 0 ? misses : count < dropped ? 0 : misses + 1;
            dropped = -1;
            if (misses >= 3)
            {
                return false;
            }

            // The dock keeps the class's apps it starts with, in order; what follows them goes.
            var item = items.FirstOrDefault() ?? docked.Skip(Kept(docked, dock)).FirstOrDefault();
            if (item is null)
            {
                // This page is empty: the next one, while there is one on its right.
                if (pages.Any(p => p.Left >= width / 2))
                {
                    swipes++;
                    Swipe(width * 85 / 100, height / 2, width * 15 / 100, height / 2, 300);
                    _pause(Settle);
                    continue;
                }
                Key("KEYCODE_HOME");
                _pause(Settle);
                return true;
            }
            // Over "Remover": alone, it is in the middle; beside "Desinstalar", on the left.
            var x = misses % 2 == 0 ? width / 2 : width / 4;
            var y = Math.Max(1, pages.Min(p => p.Top) / 2);
            Shell("input", "draganddrop", N(item.X), N(item.Y), N(x), N(y), "2500");
            _pause(Second);
            // "Item removido" hides the home from the screen read for a few seconds.
            Key("KEYCODE_BACK");
            _pause(Moment);
            dropped = count;
        }
        return false;
    }

    // The class's apps dragged from the drawer to the dock's end, in order,
    // each after the ones already there; the dock read again after them all,
    // and once more for what did not land.
    private bool Docked(List<string[]> dock)
    {
        for (var round = 0; round < 2 && InTime; round++)
        {
            Key("KEYCODE_HOME");
            _pause(Settle);
            if (Screen() is not { } home)
            {
                continue;
            }
            var labels = DockLabels(home);
            if (Matches(labels, dock))
            {
                return true;
            }
            // Something else in the dock: not for the cable to sort out.
            if (labels.Count >= dock.Count || !Matches(labels, dock.Take(labels.Count).ToList())
                || Ours(home, Id + "hotseat") is not { } hotseat)
            {
                return false;
            }
            var cells = hotseat.Children.FirstOrDefault(n => n.Shown) ?? hotseat;
            var (dropX, dropY) = (cells.Right - Math.Min(40, cells.Width / 10), cells.Y);
            foreach (var names in dock.Skip(labels.Count))
            {
                if (!InTime || InDrawer(home, names) is not { } app)
                {
                    break;
                }
                Shell("input", "draganddrop", N(app.X), N(app.Y), N(dropX), N(dropY), "2500");
                _pause(Settle);
            }
        }
        Key("KEYCODE_HOME");
        _pause(Settle);
        return Screen() is { } last && Matches(DockLabels(last), dock);
    }

    // The app in the drawer (swiped up from the home), scrolled to when it
    // is further down; null when the drawer did not open or has no such app.
    private HomeScreenNode? InDrawer(HomeScreenNode home, string[] names)
    {
        var (width, height) = Size(home);
        Swipe(width / 2, height * 7 / 10, width / 2, height * 2 / 10, 300);
        _pause(Settle);
        for (var scrolls = 0; scrolls <= 8 && InTime; scrolls++)
        {
            if (Screen() is not { } screen || Ours(screen, Id + "apps_list_view") is not { } list)
            {
                return null;
            }
            if (list.Descendants().FirstOrDefault(n => n.Shown && list.Holds(n) && Named(n, names)) is { } app)
            {
                return app;
            }
            Swipe(list.X, list.Bottom - list.Height / 6, list.X, list.Top + list.Height / 6, 500);
            _pause(Moment);
        }
        return null;
    }

    // A home page's icons, folders and widgets: what its cell layout holds,
    // but the date and search bar, which cannot be removed.
    private static IEnumerable<HomeScreenNode> PageItems(HomeScreenNode page) =>
        page.Children
            .SelectMany(cells => cells.Children)
            .Where(n => n.Shown && n.Id != Id + "search_container_workspace" && !n.Has(Id + "smartspace_content"));

    private static IEnumerable<HomeScreenNode> DockItems(HomeScreenNode screen) =>
        Ours(screen, Id + "hotseat")?.Children.SelectMany(cells => cells.Children).Where(n => n.Shown)
        ?? Enumerable.Empty<HomeScreenNode>();

    private static List<string> DockLabels(HomeScreenNode screen) => DockItems(screen).Select(n => n.Description).ToList();

    // How many of the dock's first items are the class's first apps, in order.
    private static int Kept(List<HomeScreenNode> docked, List<string[]> dock) =>
        docked.Zip(dock).TakeWhile(pair => Named(pair.First, pair.Second)).Count();

    private static bool Matches(List<string> labels, List<string[]> apps) =>
        labels.Count == apps.Count && labels.Zip(apps).All(pair => pair.Second.Any(name => HomeScreenNode.Plain(name) == HomeScreenNode.Plain(pair.First)));

    private static bool Named(HomeScreenNode node, string[] names) =>
        node.Description.Length > 0 && names.Any(name => HomeScreenNode.Plain(name) == HomeScreenNode.Plain(node.Description));

    // Taps and drags only ever start on the launcher's own screens.
    private static HomeScreenNode? Ours(HomeScreenNode screen, string id) =>
        screen.Descendants().FirstOrDefault(n => n.Id == id && n.Package == Launcher && n.Shown);

    private static (int Width, int Height) Size(HomeScreenNode screen)
    {
        var nodes = screen.Descendants().ToList();
        return nodes.Count == 0 ? (800, 1232) : (nodes.Max(n => n.Right), nodes.Max(n => n.Bottom));
    }

    private HomeScreenNode? Screen()
    {
        Shell("uiautomator", "dump", DumpFile);
        return HomeScreenNode.Parse(Shell("cat", DumpFile).Text);
    }

    private void Key(string key) => Shell("input", "keyevent", key);

    private void Tap(HomeScreenNode node) => Shell("input", "tap", N(node.X), N(node.Y));

    private void Swipe(int x1, int y1, int x2, int y2, int milliseconds) =>
        Shell("input", "swipe", N(x1), N(y1), N(x2), N(y2), N(milliseconds));

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
}
