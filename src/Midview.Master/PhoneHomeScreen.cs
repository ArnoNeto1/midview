using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace Midview.Master;

// Arranges a Xiaomi phone's home screen for a class over the cable, the way
// the teacher would on its screen: MIUI's launcher (com.miui.home) in
// "Com Gaveta de apps" (every app in the drawer, none on the home pages),
// every icon, folder and widget taken off the home pages, the class's apps
// in the dock (Chrome, Câmera, Galeria, Gerenciador de Arquivos and
// Calculadora, the ones installed), and the layout locked. Only with
// "Depuração USB (configurações de segurança)" on, which lets the cable
// tap. Tried on the school's Redmi 10C (MIUI 14, Android 13) on
// 2026-11-13: the launcher keeps its mode in its own files (no setting
// changes with it), so the mode is chosen on its settings screen; it reads
// miui_home_lock_screen_cells at once ("Bloquear layout da Tela inicial");
// a long press on an icon or a widget offers "Remover" (only the shortcut:
// the app stays in the drawer), on a folder only "Editar pasta", so a
// folder is opened and emptied; the drawer's app is dragged to the dock.
// Every step looks at the screen first; anything unexpected ends it, and
// the teacher arranges the rest by hand.
public sealed class PhoneHomeScreen
{
    // Measured on the Redmi: about 3 s a screen read, 7 s an item removed,
    // 8 s an app put in the dock; a folder, an icon, 4 dock apps taken off
    // and 5 put back took 101 s.
    public static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    internal const string DumpFile = "/data/local/tmp/midview-home.xml";
    internal const string SettingsActivity = "com.miui.home/.settings.MiuiHomeSettingActivity";
    internal const string LockSetting = "miui_home_lock_screen_cells";

    // The class's apps, in the dock's order, with the names MIUI gives them.
    internal static readonly IReadOnlyList<(string Package, string[] Names)> DockApps = new[]
    {
        ("com.android.chrome", new[] { "Chrome" }),
        ("com.android.camera", new[] { "Câmera", "Camera" }),
        ("com.miui.gallery", new[] { "Galeria", "Gallery" }),
        ("com.mi.android.globalFileexplorer", new[] { "Gerenciador de Arquivos", "File Manager" }),
        ("com.miui.calculator", new[] { "Calculadora", "Calculator" }),
    };

    private const string Home = "com.miui.home";
    private const string Id = Home + ":id/";

    // Never "Desinstalar": only the shortcut is taken off.
    private static readonly string[] RemoveTexts = { "Remover", "Remove" };

    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);
    // Before a screen read, which itself waits for the screen to be still.
    private static readonly TimeSpan Moment = TimeSpan.FromSeconds(0.5);
    // The launcher reloads its pages after the mode changes.
    private static readonly TimeSpan Reload = TimeSpan.FromSeconds(3);

    private readonly Func<string[], AdbOutput> _shell;
    private readonly Action<TimeSpan> _pause;
    private readonly Func<TimeSpan> _elapsed;

    // shell: one "adb shell" command on the phone. pause and elapsed: the
    // clock (a fake one in the tests).
    public PhoneHomeScreen(Func<string[], AdbOutput> shell, Action<TimeSpan> pause, Func<TimeSpan> elapsed)
    {
        _shell = shell;
        _pause = pause;
        _elapsed = elapsed;
    }

    public static PhoneHomeScreen WithRealClock(Func<string[], AdbOutput> shell)
    {
        var watch = Stopwatch.StartNew();
        return new PhoneHomeScreen(shell, Thread.Sleep, () => watch.Elapsed);
    }

    // True once the home screen is arranged and locked. False on any
    // failure, with the phone left as far as it got (on its home screen).
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
        // Unlocked, or nothing moves; locked again only once arranged.
        Shell("settings", "put", "system", LockSetting, "0");
        // Swiping up opens the drawer (not the search), and swiping past the
        // pages opens no Google Discover: as Lighten leaves them.
        Shell("settings", "put", "system", "QSB_SCROLL_ENTRANCE_STATUS", "0");
        Shell("settings", "put", "system", "open_personal_assistant", "0");

        // Before anything is removed: in "Clássico" a removed icon would be
        // the app itself leaving the phone's screens.
        if (!DrawerMode())
        {
            return false;
        }
        var dock = InstalledDockApps();
        if (!Emptied(dock) || !Docked(dock))
        {
            return false;
        }
        Shell("settings", "put", "system", LockSetting, "1");
        return Shell("settings", "get", "system", LockSetting).Text.Trim() == "1";
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

    // The launcher's settings: the "Tela inicial" row (the first with a
    // value on its right), then the "Com Gaveta de apps" card, until it is
    // the one checked.
    private bool DrawerMode()
    {
        var started = Shell("am", "start", "-W", "--activity-clear-task", "-n", SettingsActivity);
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
            if (Ours(screen, Id + "drawer") is { } drawer)
            {
                if (drawer.Checked)
                {
                    Key("KEYCODE_HOME");
                    _pause(Settle);
                    return true;
                }
                Tap(drawer);
                taps++;
                _pause(Reload);
                continue;
            }
            if (screen.Descendants().Where(n => n.Id == Id + "text_right" && n.Shown).MinBy(n => n.Top) is { } row)
            {
                Tap(row);
                taps++;
                _pause(Settle);
                continue;
            }
            _pause(Second);
        }
        return false;
    }

    private enum Removal { Removed, Folder, Failed }

    // Every home page emptied (icons, folders, widgets), and the dock too
    // unless it already holds the class's apps in order.
    private bool Emptied(List<string[]> dock)
    {
        Key("KEYCODE_HOME");
        _pause(Settle);
        var failures = 0;
        var emptyLooks = 0;
        while (InTime && failures < 4)
        {
            if (Screen() is not { } screen)
            {
                failures++;
                _pause(Second);
                continue;
            }
            // A menu or the drawer left open.
            if (screen.Has(Id + "shortcut_menu") || screen.Has(Id + "apps_view"))
            {
                Key("KEYCODE_BACK");
                _pause(Second);
                continue;
            }
            if (Ours(screen, Id + "folder_grid_view") is { } grid)
            {
                if (grid.Descendants().FirstOrDefault(n => n.Description.Length > 0 && n.Shown && grid.Holds(n)) is { } app)
                {
                    if (Remove(app) != Removal.Removed)
                    {
                        failures++;
                    }
                }
                else
                {
                    Key("KEYCODE_BACK");
                    _pause(Second);
                }
                continue;
            }
            if (Ours(screen, Id + "workspace") is not { } workspace)
            {
                failures++;
                Key("KEYCODE_HOME");
                _pause(Settle);
                continue;
            }

            var item = PageItems(workspace).FirstOrDefault()
                       ?? (Matches(DockLabels(screen), dock) ? null : DockItems(screen).FirstOrDefault());
            if (item is null)
            {
                // This page is empty: the next one, while the launcher says there are more.
                if (++emptyLooks >= Pages(workspace))
                {
                    Key("KEYCODE_HOME");
                    _pause(Settle);
                    return true;
                }
                var (width, height) = Size(screen);
                Swipe(width * 85 / 100, height / 2, width * 15 / 100, height / 2, 300);
                _pause(Settle);
                continue;
            }
            emptyLooks = 0;
            if (item.Has(Id + "preview_icons_container"))
            {
                Tap(item);
                _pause(Second);
                continue;
            }
            switch (Remove(item))
            {
                case Removal.Folder:
                    Tap(item);
                    _pause(Second);
                    break;
                case Removal.Failed:
                    failures++;
                    break;
            }
        }
        return false;
    }

    // A long press, then "Remover" in its menu. A folder's menu has only
    // "Editar pasta": closed, for the folder to be opened.
    private Removal Remove(HomeScreenNode item)
    {
        Swipe(item.X, item.Y, item.X, item.Y, 1200);
        _pause(Moment);
        var menu = Screen();
        if (menu?.Descendants().FirstOrDefault(n => n.Id == Id + "item_title" && n.Shown
                && RemoveTexts.Any(t => string.Equals(n.Text.Trim(), t, StringComparison.OrdinalIgnoreCase))) is { } remove)
        {
            Tap(remove);
            _pause(Moment);
            return Removal.Removed;
        }
        var folder = menu?.Has(Id + "folder_shortcut_menu") == true;
        Key("KEYCODE_BACK");
        _pause(Moment);
        return folder ? Removal.Folder : Removal.Failed;
    }

    // The class's apps dragged from the drawer to the dock's end, in order,
    // each after the ones already there; the dock read again after them all,
    // and once more for what did not land.
    private bool Docked(List<string[]> dock)
    {
        for (var round = 0; round < 2 && InTime; round++)
        {
            Key("KEYCODE_HOME");
            _pause(Moment);
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
                || (Ours(home, Id + "hotseat_content_screen") ?? Ours(home, Id + "hotseat")) is not { } hotseat)
            {
                return false;
            }
            foreach (var names in dock.Skip(labels.Count))
            {
                if (!InTime || InDrawer(home, names) is not { } app)
                {
                    break;
                }
                Shell("input", "draganddrop", N(app.X), N(app.Y), N(hotseat.Right - 40), N(hotseat.Y), "2500");
                _pause(Moment);
            }
        }
        Key("KEYCODE_HOME");
        _pause(Moment);
        return Screen() is { } last && Matches(DockLabels(last), dock);
    }

    // The app in the drawer (swiped up from the home), scrolled to when it
    // is further down; null when the drawer did not open or has no such app.
    private HomeScreenNode? InDrawer(HomeScreenNode home, string[] names)
    {
        var (width, height) = Size(home);
        Swipe(width / 2, height * 7 / 10, width / 2, height * 2 / 10, 300);
        _pause(Second);
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

    // A home page's icons, folders and widgets: what its cell layouts hold.
    private static IEnumerable<HomeScreenNode> PageItems(HomeScreenNode workspace) =>
        workspace.Descendants()
            .Where(n => n.Class.EndsWith(".ViewGroup", StringComparison.Ordinal))
            .SelectMany(cells => cells.Children)
            .Where(n => n.Shown);

    // How many home pages there are: the dots under them (one, or the
    // drawer's arrow, for a single page).
    private static int Pages(HomeScreenNode workspace) =>
        Math.Max(1, workspace.Descendants()
            .Where(n => n.Class.EndsWith(".ScreenView", StringComparison.Ordinal))
            .Sum(n => n.Children.Count(dot => dot.Shown && dot.Class.EndsWith(".ImageView", StringComparison.Ordinal))));

    private static IEnumerable<HomeScreenNode> DockItems(HomeScreenNode screen) =>
        Ours(screen, Id + "hotseat_content_screen")?.Children.Where(n => n.Shown && n.Description.Length > 0)
        ?? Enumerable.Empty<HomeScreenNode>();

    private static List<string> DockLabels(HomeScreenNode screen) => DockItems(screen).Select(n => n.Description).ToList();

    private static bool Matches(List<string> labels, List<string[]> apps) =>
        labels.Count == apps.Count && labels.Zip(apps).All(pair => pair.Second.Any(name => HomeScreenNode.Plain(name) == HomeScreenNode.Plain(pair.First)));

    private static bool Named(HomeScreenNode node, string[] names) =>
        node.Description.Length > 0 && names.Any(name => HomeScreenNode.Plain(name) == HomeScreenNode.Plain(node.Description));

    // Taps only ever land on the launcher's own screens.
    private static HomeScreenNode? Ours(HomeScreenNode screen, string id) =>
        screen.Descendants().FirstOrDefault(n => n.Id == id && n.Package == Home && n.Shown);

    private static (int Width, int Height) Size(HomeScreenNode screen)
    {
        var nodes = screen.Descendants().ToList();
        return nodes.Count == 0 ? (720, 1500) : (nodes.Max(n => n.Right), nodes.Max(n => n.Bottom));
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
