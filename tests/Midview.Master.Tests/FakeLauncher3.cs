using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Midview.Master;

namespace Midview.Master.Tests;

// Android's own launcher (Launcher3) as PhoneLauncher3Home sees it over the
// cable: "adb shell" commands in, uiautomator dumps out, shaped like the
// Multilaser M10's (Android 10, 800x1232) on 2026-11-13: its settings (rows
// with a title and a switch), home pages of 5x5 cells with the date on the
// first page's top row and a sliver of the next page on the right, the dock
// growing with its apps, the "Remover" target shown only while an item is
// dragged (centred, or on the left beside "Desinstalar"), "Item removido"
// hiding the home until it goes, and the drawer, scrolled two rows at a time.
internal sealed class FakeLauncher3
{
    public sealed class Item
    {
        public string Label = "";
        public bool Widget;
        public bool Folder;
        // An app the student installed: "Desinstalar" shows beside "Remover".
        public bool UserApp;
        // Only "Desinstalar" shows, in the middle (never on the school's tablet).
        public bool UninstallOnly;

        public static Item Icon(string label) => new() { Label = label };
    }

    private enum View { Home, Settings, Drawer, Uninstall }

    public static readonly string[] ClassApps = { "Chrome", "Câmera", "Fotos", "Files", "Calculadora" };

    public bool StartFails;
    public bool DumpsFail;
    public bool HasGoogleRow = true;
    public bool SwitchesStuck;
    public bool AddIcon = true;
    public bool GoogleApp = true;
    public List<List<Item>> Pages = new() { new List<Item>() };
    public List<string> Dock = new();
    public List<string> DrawerApps = new() { "Calculadora", "Câmera", "Chrome", "Configurações", "Contatos", "Files", "Fotos", "Play Store", "Relógio" };
    public HashSet<string> Packages = new(PhoneLauncher3Home.DockApps.Select(app => app.Package));
    public readonly List<string> Commands = new();
    // The apps the uninstall question was asked about (never answered).
    public readonly List<string> UninstallAsked = new();

    private View _view = View.Home;
    private int _page;
    private int _scroll;
    private bool _removedNote;

    private const int Width = 800;
    private const int Height = 1232;
    private static readonly (int L, int T, int R, int B) Hotseat = (0, 1071, 800, 1232);
    private static readonly (int L, int T, int R, int B) AppList = (0, 76, 800, 1232);

    public bool Empty => Pages.All(page => page.Count == 0);

    public AdbOutput Shell(string[] command)
    {
        var line = string.Join(' ', command);
        Commands.Add(line);
        return new AdbOutput(0, Answer(command, line), false);
    }

    private string Answer(string[] c, string line)
    {
        if (line == "pm list packages")
        {
            return string.Concat(Packages.Select(p => "package:" + p + "\n"));
        }
        if (c[0] == "am" && line.Contains(PhoneLauncher3Home.SettingsAction, StringComparison.Ordinal)
            && line.EndsWith("-p " + PhoneLauncher3Home.Launcher, StringComparison.Ordinal))
        {
            if (StartFails)
            {
                return "Error: Activity not started, unable to resolve Intent\n";
            }
            _view = View.Settings;
            return "Starting: Intent { act=" + PhoneLauncher3Home.SettingsAction + " }\nStatus: ok\n";
        }
        if (c[0] == "cat" && c[1] == PhoneLauncher3Home.DumpFile)
        {
            return DumpsFail ? "" : Dump();
        }
        if (c[0] == "input")
        {
            Input(c);
        }
        return "";
    }

    private static int I(string text) => int.Parse(text, CultureInfo.InvariantCulture);

    private static bool In((int L, int T, int R, int B) b, int x, int y) => x >= b.L && x <= b.R && y >= b.T && y <= b.B;

    // The first page's top row holds the date.
    private static (int L, int T, int R, int B) Cell(int page, int i)
    {
        var at = page == 0 ? i + 5 : i;
        return (80 + 128 * (at % 5), 129 + 164 * (at / 5), 80 + 128 * (at % 5 + 1), 129 + 164 * (at / 5 + 1));
    }

    private static (int L, int T, int R, int B) DockCell(int i) => (80 + 128 * i, 1071, 208 + 128 * i, 1160);

    private (int L, int T, int R, int B) AppCell(int i) =>
        (32 + 147 * (i % 5), 106 + 164 * (i / 5 - _scroll), 32 + 147 * (i % 5 + 1), 106 + 164 * (i / 5 - _scroll + 1));

    private void Input(string[] c)
    {
        switch (c[1])
        {
            case "keyevent" when c[2] == "KEYCODE_HOME":
                _view = View.Home;
                _page = 0;
                break;
            case "keyevent" when c[2] == "KEYCODE_BACK":
                if (_removedNote)
                {
                    _removedNote = false;
                }
                else if (_view != View.Home)
                {
                    _view = View.Home;
                }
                break;
            case "tap":
                Tap(I(c[2]), I(c[3]));
                break;
            case "swipe":
                Swipe(I(c[2]), I(c[3]), I(c[4]), I(c[5]));
                break;
            case "draganddrop":
                DragAndDrop(I(c[2]), I(c[3]), I(c[4]), I(c[5]));
                break;
        }
    }

    private void Tap(int x, int y)
    {
        if (_view != View.Settings || SwitchesStuck)
        {
            return;
        }
        if (In((730, 184, 776, 211), x, y))
        {
            AddIcon = !AddIcon;
        }
        else if (HasGoogleRow && In((730, 257, 776, 284), x, y))
        {
            GoogleApp = !GoogleApp;
        }
    }

    private void Swipe(int x1, int y1, int x2, int y2)
    {
        if (_view == View.Home && !_removedNote && y1 - y2 > 300)
        {
            _view = View.Drawer;
            _scroll = 0;
        }
        else if (_view == View.Home && !_removedNote && x1 - x2 > 300)
        {
            _page = Math.Min(_page + 1, Pages.Count - 1);
        }
        else if (_view == View.Drawer && y1 - y2 > 300)
        {
            _scroll = Math.Min(_scroll + 2, Math.Max(0, (DrawerApps.Count + 4) / 5 - 6));
        }
    }

    private void DragAndDrop(int x1, int y1, int x2, int y2)
    {
        if (_view == View.Drawer)
        {
            var app = Enumerable.Range(0, DrawerApps.Count).FirstOrDefault(i => In(AppCell(i), x1, y1) && In(AppList, x1, y1), -1);
            _view = View.Home;
            if (app >= 0 && In(Hotseat, x2, y2) && Dock.Count < 5)
            {
                Dock.Add(DrawerApps[app]);
            }
            return;
        }
        if (_view != View.Home || _removedNote)
        {
            return;
        }
        var page = Pages[_page];
        var cell = Enumerable.Range(0, page.Count).FirstOrDefault(i => In(Cell(_page, i), x1, y1), -1);
        var docked = Enumerable.Range(0, Dock.Count).FirstOrDefault(i => In(DockCell(i), x1, y1), -1);
        var item = cell >= 0 ? page[cell] : docked >= 0 ? Item.Icon(Dock[docked]) : null;
        if (item is null || y2 > 110)
        {
            return;
        }
        // "Remover" and "Desinstalar" as the M10 lays them out: about 100 px wide each.
        (int L, int R)? remove = item.UninstallOnly ? null : item.UserApp ? (150, 250) : (350, 450);
        (int L, int R)? uninstall = item.UninstallOnly ? (350, 450) : item.UserApp ? (550, 650) : null;
        if (remove is { } r && x2 >= r.L && x2 <= r.R)
        {
            if (cell >= 0)
            {
                page.RemoveAt(cell);
            }
            else
            {
                Dock.RemoveAt(docked);
            }
            _removedNote = true;
        }
        else if (uninstall is { } u && x2 >= u.L && x2 <= u.R)
        {
            UninstallAsked.Add(item.Label);
            _view = View.Uninstall;
        }
    }

    private static string Node(string id, string cls, (int L, int T, int R, int B) b, string desc = "", string text = "",
        bool isChecked = false, string inner = "", string package = PhoneLauncher3Home.Launcher) =>
        $"<node index=\"0\" text=\"{text}\" resource-id=\"{id}\" class=\"{cls}\" package=\"{package}\" " +
        $"content-desc=\"{desc}\" checked=\"{(isChecked ? "true" : "false")}\" bounds=\"[{b.L},{b.T}][{b.R},{b.B}]\"" +
        (inner.Length == 0 ? " />" : $">{inner}</node>");

    private static string L(string id) => PhoneLauncher3Home.Launcher + ":id/" + id;

    private string Dump()
    {
        var body = _view switch
        {
            View.Settings => Node(L("recycler_view"), "androidx.recyclerview.widget.RecyclerView", (0, 88, 800, 1232),
                inner: Row(88, "Pontos de notificação", null)
                       + Row(161, "Adicionar ícone à tela inicial", AddIcon)
                       + (HasGoogleRow ? Row(234, "Mostrar Google App", GoogleApp) : "")
                       + Row(307, "Ecrã inicial contínuo", true)),
            View.Uninstall => Node("android:id/message", "android.widget.TextView", (40, 500, 760, 600),
                text: "Deseja desinstalar este app?", package: "com.google.android.packageinstaller"),
            View.Drawer => Node(L("apps_view"), "android.widget.RelativeLayout", (0, 0, Width, Height),
                inner: Node(L("apps_list_view"), "androidx.recyclerview.widget.RecyclerView", AppList,
                    inner: string.Concat(Enumerable.Range(0, DrawerApps.Count)
                        .Where(i => AppCell(i).T < AppList.B && AppCell(i).B > AppList.T)
                        .Select(i => Node(L("icon"), "android.widget.TextView", AppCell(i), DrawerApps[i], DrawerApps[i]))))),
            _ when _removedNote => Node(L("label"), "android.widget.TextView", (80, 1162, 647, 1194), text: "Item removido")
                                   + Node(L("action"), "android.widget.TextView", (647, 1162, 720, 1194), text: "Desfazer"),
            _ => HomeDump(),
        };
        var root = _view == View.Uninstall ? body : Node(L("drag_layer"), "android.widget.FrameLayout", (0, 0, Width, Height), inner: body);
        var xml = new StringBuilder("<?xml version='1.0' encoding='UTF-8' standalone='yes' ?><hierarchy rotation=\"0\">");
        xml.Append(Node("", "android.widget.FrameLayout", (0, 0, Width, Height), inner: root,
            package: _view == View.Uninstall ? "com.google.android.packageinstaller" : PhoneLauncher3Home.Launcher));
        return xml.Append("</hierarchy>").ToString();
    }

    private static string Row(int top, string title, bool? on) =>
        Node("", "android.widget.LinearLayout", (0, top, 800, top + 73),
            inner: Node("", "android.widget.RelativeLayout", (80, top, 714, top + 73),
                       inner: Node("android:id/title", "android.widget.TextView", (80, top + 16, 300, top + 38), text: title))
                   + (on is { } value
                       ? Node("android:id/widget_frame", "android.widget.LinearLayout", (714, top, 776, top + 73),
                           inner: Node("android:id/switch_widget", "android.widget.Switch", (730, top + 23, 776, top + 50),
                               text: value ? "ATIVADO" : "DESATIVADO", isChecked: value))
                       : ""));

    private string HomeDump()
    {
        var page = Pages[_page];
        var cells = string.Concat(page.Select((item, i) => item.Widget
            ? Node("", "com.android.launcher3.widget.LauncherAppWidgetHostView", Cell(_page, i), item.Label,
                inner: Node("com.android.chrome:id/dino", "android.widget.FrameLayout", Cell(_page, i), package: "com.android.chrome"))
            : item.Folder
                ? Node("", "android.widget.FrameLayout", Cell(_page, i), "Pasta: " + item.Label,
                    inner: Node(L("folder_icon_name"), "android.widget.TextView", Cell(_page, i)))
                : Node("", "android.widget.TextView", Cell(_page, i), item.Label, item.Label)));
        if (_page == 0)
        {
            cells = Node(L("search_container_workspace"), "android.widget.FrameLayout", (80, 129, 720, 293),
                        inner: Node(L("smartspace_content"), "android.widget.FrameLayout", (80, 129, 720, 293),
                            inner: Node(L("date_text"), "android.widget.TextView", (298, 197, 501, 225), "sexta-feira, nov 13", "sexta-feira, nov 13")))
                    + cells;
        }
        var pages = (_page > 0 ? Node("", "android.view.ViewGroup", (0, 129, 48, 950)) : "")
                    + Node("", "android.view.ViewGroup", (56, 129, 744, 950),
                        inner: Node("", "android.view.ViewGroup", (80, 129, 720, 950), inner: cells))
                    + (_page < Pages.Count - 1 ? Node("", "android.view.ViewGroup", (752, 129, 800, 950)) : "");
        return Node(L("workspace"), "android.widget.ScrollView", (0, 0, Width, Height), inner: pages)
               + Node(L("page_indicator"), "android.view.View", (0, 1047, 800, 1071))
               + Node(L("hotseat"), "android.view.ViewGroup", Hotseat,
                   inner: Node("", "android.view.ViewGroup", (80, 1071, 720, 1160),
                       inner: string.Concat(Dock.Select((label, i) => Node("", "android.widget.TextView", DockCell(i), label, label)))))
               + Node(L("apps_view"), "android.widget.RelativeLayout", (0, 1128, 800, 1232),
                   inner: Node(L("btn_qsb_search"), "android.widget.TextView", (88, 1180, 712, 1228), text: "Pesquisar"));
    }
}
