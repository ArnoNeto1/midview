using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Midview.Master;

namespace Midview.Master.Tests;

// MIUI's launcher (com.miui.home) as PhoneHomeScreen sees it over the cable:
// "adb shell" commands in, uiautomator dumps out, shaped like the Redmi
// 10C's (MIUI 14) on 2026-11-13: its settings (the "Tela inicial" row, the
// "Clássico" and "Com Gaveta de apps" cards), home pages whose cell layout
// holds icons, folders and widgets, the dock, the long-press menu
// ("Remover", or "Editar pasta" on a folder), an open folder, and the
// drawer, scrolled a row at a time.
internal sealed class FakeMiuiLauncher
{
    public sealed class Item
    {
        public string Label = "";
        public bool Widget;
        public List<string>? Inside;
        // A folder drawn without its preview (as while it is dragged).
        public bool NoPreview;
        // A menu without "Remover" (only "Desinstalar").
        public bool NoRemove;

        public static Item Icon(string label) => new() { Label = label };
        public static Item Folder(params string[] apps) => new() { Label = "Pasta", Inside = apps.ToList() };
        public static Item Clock() => new() { Label = "Relógio", Widget = true };
    }

    private enum View { Home, SettingsMain, SettingsMode, Drawer }

    public static readonly string[] ClassApps = { "Chrome", "Câmera", "Galeria", "Gerenciador de Arquivos", "Calculadora" };

    public bool Drawer;
    public bool DrawerTapWorks = true;
    public bool StartFails;
    public bool DumpsFail;
    public List<List<Item>> Pages = new() { new List<Item>() };
    public List<Item> ClassicPage = new() { Item.Icon("Configurações"), Item.Icon("Clima") };
    public List<string> Dock = new();
    public List<string> DrawerApps = new() { "Calculadora", "Câmera", "Chrome", "Clima", "Configurações", "Galeria", "Gerenciador de Arquivos", "Play Store" };
    public HashSet<string> Packages = new(PhoneHomeScreen.DockApps.Select(app => app.Package));
    public readonly Dictionary<string, string> System = new() { [PhoneHomeScreen.LockSetting] = "0" };
    public readonly List<string> Commands = new();
    // What was taken off while the launcher was still "Clássico" (never wanted).
    public readonly List<string> RemovedInClassic = new();

    private View _view = View.Home;
    private int _page;
    private Item? _menuFor;
    private Item? _folder;
    private int _scroll;

    private const int CellWidth = 168;
    private const int CellHeight = 212;
    private static readonly (int L, int T, int R, int B) RemoveBounds = (424, 227, 640, 269);
    private static readonly (int L, int T, int R, int B) Hotseat = (16, 1420, 704, 1507);
    private static readonly (int L, int T, int R, int B) AppList = (0, 149, 720, 1424);

    private List<Item> CurrentPage => Drawer ? Pages[_page] : ClassicPage;

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
        if (c[0] == "am" && line.Contains(PhoneHomeScreen.SettingsActivity, StringComparison.Ordinal))
        {
            if (StartFails)
            {
                return "Error: Activity not started\n";
            }
            _view = View.SettingsMain;
            return "Starting: Intent { cmp=" + PhoneHomeScreen.SettingsActivity + " }\nStatus: ok\n";
        }
        if (c[0] == "settings" && c.Length == 5 && c[1] == "put")
        {
            System[c[3]] = c[4];
            return "";
        }
        if (c[0] == "settings" && c.Length == 4 && c[1] == "get")
        {
            return System.TryGetValue(c[3], out var value) ? value + "\n" : "null\n";
        }
        if (c[0] == "cat" && c[1] == PhoneHomeScreen.DumpFile)
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

    private static (int L, int T, int R, int B) Cell(int i) =>
        (24 + CellWidth * (i % 4), 87 + CellHeight * (i / 4), 24 + CellWidth * (i % 4 + 1), 87 + CellHeight * (i / 4 + 1));

    private static (int L, int T, int R, int B) DockCell(int i) => (16 + 132 * i, 1420, 182 + 132 * i, 1507);

    private static (int L, int T, int R, int B) FolderCell(int i) => (83 + 189 * i, 595, 251 + 189 * i, 799);

    private (int L, int T, int R, int B) AppCell(int i) =>
        (24 + CellWidth * (i % 4), 165 + 222 * (i / 4 - _scroll), 24 + CellWidth * (i % 4 + 1), 165 + 222 * (i / 4 - _scroll + 1));

    private void Input(string[] c)
    {
        switch (c[1])
        {
            case "keyevent" when c[2] == "KEYCODE_HOME":
                _view = View.Home;
                _page = 0;
                _menuFor = null;
                _folder = null;
                break;
            case "keyevent" when c[2] == "KEYCODE_BACK":
                if (_menuFor is not null)
                {
                    _menuFor = null;
                }
                else if (_folder is not null)
                {
                    _folder = null;
                }
                else
                {
                    _view = _view == View.SettingsMode ? View.SettingsMain : View.Home;
                }
                break;
            case "tap":
                Tap(I(c[2]), I(c[3]));
                break;
            case "swipe" when c[2] == c[4] && c[3] == c[5]:
                LongPress(I(c[2]), I(c[3]));
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
        switch (_view)
        {
            case View.SettingsMain when In((343, 318, 628, 386), x, y):
                _view = View.SettingsMode;
                return;
            case View.SettingsMode when In((248, 294, 472, 766), x, y):
                Drawer |= DrawerTapWorks;
                return;
            case View.SettingsMode when In((24, 294, 248, 729), x, y):
                Drawer = false;
                return;
            case View.Home when _menuFor is { } item:
                _menuFor = null;
                if (In(RemoveBounds, x, y) && item.Inside is null && !item.NoRemove)
                {
                    Remove(item);
                }
                return;
            case View.Home when _folder is null && ItemAt(x, y) is { Inside: not null } folder:
                _folder = folder;
                return;
        }
    }

    private void Remove(Item item)
    {
        if (!Drawer)
        {
            RemovedInClassic.Add(item.Label);
        }
        if (_folder is { Inside: { } inside } folder)
        {
            inside.Remove(item.Label);
            if (inside.Count <= 1)
            {
                // A folder left with one app turns into that app's icon.
                var page = CurrentPage;
                var at = page.IndexOf(folder);
                page.RemoveAt(at);
                if (inside.Count == 1)
                {
                    page.Insert(at, Item.Icon(inside[0]));
                }
                _folder = null;
            }
            return;
        }
        if (!CurrentPage.Remove(item))
        {
            Dock.Remove(item.Label);
        }
    }

    private Item? ItemAt(int x, int y)
    {
        if (_folder is { Inside: { } inside })
        {
            var i = Enumerable.Range(0, inside.Count).FirstOrDefault(i => In(FolderCell(i), x, y), -1);
            return i < 0 ? null : Item.Icon(inside[i]);
        }
        var page = CurrentPage;
        var cell = Enumerable.Range(0, page.Count).FirstOrDefault(i => In(Cell(i), x, y), -1);
        if (cell >= 0)
        {
            return page[cell];
        }
        var dock = Enumerable.Range(0, Dock.Count).FirstOrDefault(i => In(DockCell(i), x, y), -1);
        return dock < 0 ? null : Item.Icon(Dock[dock]);
    }

    private void LongPress(int x, int y)
    {
        if (_view == View.Home && _menuFor is null)
        {
            _menuFor = ItemAt(x, y);
        }
    }

    private void Swipe(int x1, int y1, int x2, int y2)
    {
        if (_view == View.Home && _folder is null && _menuFor is null && Drawer && y1 - y2 > 300)
        {
            _view = View.Drawer;
            _scroll = 0;
        }
        else if (_view == View.Home && _folder is null && x1 - x2 > 300)
        {
            _page = Math.Min(_page + 1, (Drawer ? Pages.Count : 1) - 1);
        }
        else if (_view == View.Drawer && y1 - y2 > 300)
        {
            _scroll = Math.Min(_scroll + 2, Math.Max(0, (DrawerApps.Count + 3) / 4 - 5));
        }
    }

    private void DragAndDrop(int x1, int y1, int x2, int y2)
    {
        if (_view != View.Drawer)
        {
            return;
        }
        var i = Enumerable.Range(0, DrawerApps.Count).FirstOrDefault(i => In(AppCell(i), x1, y1), -1);
        _view = View.Home;
        if (i >= 0 && In(Hotseat, x2, y2) && Dock.Count < 5)
        {
            Dock.Add(DrawerApps[i]);
        }
    }

    private static string Node(string id, string cls, (int L, int T, int R, int B) b, string desc = "", string text = "",
        bool isChecked = false, string inner = "") =>
        $"<node index=\"0\" text=\"{text}\" resource-id=\"{(id.Length > 0 ? "com.miui.home:id/" + id : "")}\" " +
        $"class=\"{cls}\" package=\"com.miui.home\" content-desc=\"{desc}\" checked=\"{(isChecked ? "true" : "false")}\" " +
        $"bounds=\"[{b.L},{b.T}][{b.R},{b.B}]\"" + (inner.Length == 0 ? " />" : $">{inner}</node>");

    private string Dump()
    {
        var body = _view switch
        {
            View.SettingsMain =>
                Node("recycler_view", "androidx.recyclerview.widget.RecyclerView", (0, 281, 720, 1507),
                    inner: Node("", "android.widget.TextView", (58, 327, 229, 376), text: "Tela inicial")
                           + Node("text_right", "android.widget.TextView", (343, 318, 628, 386), text: Drawer ? "Com Gaveta de apps" : "Clássico")
                           + Node("text_right", "android.widget.TextView", (531, 782, 628, 850), text: "Botões")),
            View.SettingsMode =>
                Node("recycler_view", "androidx.recyclerview.widget.RecyclerView", (0, 281, 720, 1507),
                    inner: Node("desktop", "miuix.VisualCheckBox", (24, 294, 248, 729), "Clássico", "Clássico", !Drawer)
                           + Node("drawer", "miuix.VisualCheckBox", (248, 294, 472, 766), "Com Gaveta de apps", "Com Gaveta de apps", Drawer)),
            View.Drawer => Node("apps_view", "android.widget.RelativeLayout", (0, 0, 720, 1507),
                inner: Node("apps_list_view", "androidx.recyclerview.widget.RecyclerView", AppList,
                    inner: string.Concat(Enumerable.Range(0, DrawerApps.Count)
                        .Where(i => AppCell(i).T < AppList.B && AppCell(i).B > AppList.T)
                        .Select(i => Node("", "android.widget.RelativeLayout", AppCell(i), DrawerApps[i]))))),
            _ => HomeDump(),
        };
        var xml = new StringBuilder("<?xml version='1.0' encoding='UTF-8' standalone='yes' ?><hierarchy rotation=\"0\">");
        xml.Append(Node("", "android.widget.FrameLayout", (0, 0, 720, 1507), inner: body));
        return xml.Append("</hierarchy>").ToString();
    }

    private string HomeDump()
    {
        var page = CurrentPage;
        var cells = string.Concat(page.Select((item, i) => item.Widget
            ? Node("", "android.widget.FrameLayout", Cell(i), inner: Node("", "android.view.View", Cell(i), item.Label))
            : Node("", "android.widget.RelativeLayout", Cell(i), item.Label,
                inner: Node("icon_icon", "android.widget.ImageView", Cell(i), item.Label)
                       + (item.Inside is not null && !item.NoPreview
                           ? Node("preview_icons_container", "android.widget.LinearLayout", Cell(i))
                           : ""))));
        var dots = string.Concat(Enumerable.Range(0, Drawer ? Pages.Count : 1)
            .Select(i => Node("", "android.widget.ImageView", (346 + 28 * i, 1363, 374 + 28 * i, 1391), "Tela " + (i + 1))));
        var home = Node("workspace", "com.miui.home.launcher.ScreenView", (0, 0, 720, 1507),
                       inner: Node("", "android.widget.FrameLayout", (0, 40, 720, 1507),
                                  inner: Node("", "android.view.ViewGroup", (0, 40, 720, 1507), inner: cells))
                              + Node("", "android.widget.FrameLayout", (0, 1357, 720, 1397),
                                  inner: Node("", "com.miui.home.launcher.ScreenView", (0, 1357, 720, 1397), inner: dots)))
                   + Node("hotseat", "android.widget.FrameLayout", (0, 1420, 720, 1507),
                       inner: Node("hotseat_content_screen", "com.miui.home.launcher.ScreenView", Hotseat,
                           inner: string.Concat(Dock.Select((label, i) => Node("", "android.widget.RelativeLayout", DockCell(i), label)))));
        if (_folder is { Inside: { } inside })
        {
            home += Node("folder_cling", "android.widget.FrameLayout", (0, 0, 720, 1507),
                inner: Node("folder_grid_view", "android.widget.GridView", (74, 595, 639, 799),
                    inner: string.Concat(inside.Select((label, i) => Node("", "android.widget.RelativeLayout", FolderCell(i), label)))));
        }
        if (_menuFor is { } item)
        {
            home += Node("shortcut_menu", "android.widget.LinearLayout", (285, 180, 685, 316),
                inner: item.Inside is not null
                    ? Node("folder_shortcut_menu", "android.widget.LinearLayout", (301, 196, 669, 300),
                        inner: Node("item_title", "android.widget.TextView", RemoveBounds, text: "Editar pasta"))
                    : Node("system_shortcut_menu", "android.widget.LinearLayout", (301, 196, 669, 300),
                        inner: Node("item_title", "android.widget.TextView", RemoveBounds, text: item.NoRemove ? "Desinstalar" : "Remover")));
        }
        return home;
    }
}
