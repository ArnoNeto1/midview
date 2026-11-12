using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Midview.Protocol;

namespace Midview.Master;

// The "Abrir site" and "Abrir programa" menus: the teacher's saved items
// (SavedMenu), each opening with one click, then "Outro site..." or "Outro
// programa..." for the window. On the toolbar for the machines it reaches
// (the selection, or every one the "Telas" filter shows: ToolbarReach;
// straight to the window while nothing is saved), and on a machine's own menu for
// that machine only. Delete over an item takes it out of the menu. With
// several machines selected, a machine's menu acts on all of them
// (TargetsOf).
public partial class MainWindow
{
    private const string MenuHintTag = "dica";

    private enum LaunchKind
    {
        Website,
        Program,
    }

    private void OnOpenWebsiteClassClicked(object sender, RoutedEventArgs e) =>
        ShowClassLaunchMenu((FrameworkElement)sender, LaunchKind.Website);

    private void OnOpenProgramClassClicked(object sender, RoutedEventArgs e) =>
        ShowClassLaunchMenu((FrameworkElement)sender, LaunchKind.Program);

    private void OnOpenWebsiteMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            ShowLaunchWindow(LaunchKind.Website, MachinesOf(tile));
        }
    }

    private void OnOpenProgramMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            ShowLaunchWindow(LaunchKind.Program, MachinesOf(tile));
        }
    }

    private void OnWebsiteSubmenuOpened(object sender, RoutedEventArgs e) => FillMachineSubmenu(sender, e, LaunchKind.Website);

    private void OnProgramSubmenuOpened(object sender, RoutedEventArgs e) => FillMachineSubmenu(sender, e, LaunchKind.Program);

    private void OnWebsiteSubmenuKeyDown(object sender, KeyEventArgs e) =>
        DeleteHighlighted(((MenuItem)sender).Items, LaunchKind.Website, e);

    private void OnProgramSubmenuKeyDown(object sender, KeyEventArgs e) =>
        DeleteHighlighted(((MenuItem)sender).Items, LaunchKind.Program, e);

    private void ShowClassLaunchMenu(FrameworkElement button, LaunchKind kind)
    {
        var forWhat = kind == LaunchKind.Website ? "abrir o site" : "abrir o programa";
        if (ToolbarTargets(LaunchAction(kind), forWhat) is not { } reached)
        {
            return;
        }
        if (SavedMenu.Load(MenuPath(kind)).Count == 0)
        {
            ShowLaunchWindow(kind, reached);
            return;
        }

        // Asked again at the click: the selection may change while the menu is open.
        IReadOnlyList<KnownMachine> Reached() => ToolbarReach(LaunchAction(kind));
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        FillLaunchMenu(menu.Items, kind, Reached);
        menu.PreviewKeyDown += (_, e) => DeleteHighlighted(menu.Items, kind, e);
        menu.Opened += (_, _) => Keyboard.Focus(menu); // so Delete reaches it
        menu.IsOpen = true;
    }

    private void FillMachineSubmenu(object sender, RoutedEventArgs e, LaunchKind kind)
    {
        // Raised again by the items inside: only the parent opening counts.
        if (ReferenceEquals(e.OriginalSource, sender) && sender is MenuItem { DataContext: MachineTile tile } parent)
        {
            FillLaunchMenu(parent.Items, kind, () => MachinesOf(tile));
        }
    }

    // targets is asked at the click, so the toolbar's menu reaches the
    // machines in the room at that moment.
    private void FillLaunchMenu(ItemCollection items, LaunchKind kind, Func<IReadOnlyList<KnownMachine>> targets)
    {
        items.Clear();
        foreach (var entry in SavedMenu.Load(MenuPath(kind)))
        {
            // A "_" in a name would otherwise be taken for an access key.
            var item = new MenuItem { Header = entry.Name.Replace("_", "__"), Tag = entry, ToolTip = entry.Target };
            item.Click += (_, _) => LaunchSaved(kind, entry, targets());
            items.Add(item);
        }

        var hasEntries = items.Count > 0;
        if (hasEntries)
        {
            items.Add(new Separator());
        }
        var other = new MenuItem { Header = kind == LaunchKind.Website ? "Outro site..." : "Outro programa..." };
        other.Click += (_, _) => ShowLaunchWindow(kind, targets());
        items.Add(other);
        if (hasEntries)
        {
            items.Add(new MenuItem
            {
                Header = "Para tirar um item do menu, passe o mouse sobre ele e aperte Delete.",
                Tag = MenuHintTag,
                IsEnabled = false,
                FontSize = 11,
            });
        }
    }

    private async void LaunchSaved(LaunchKind kind, MenuEntry entry, IReadOnlyList<KnownMachine> targets)
    {
        if (!SupportedTargets(ref targets, LaunchAction(kind), out var leftOut))
        {
            return;
        }
        LaunchResult[] results;
        if (kind == LaunchKind.Website)
        {
            // An item can hold several sites, saved together.
            if (!WebsiteAddress.TryParseMany(entry.Target, out var addresses, out var problem))
            {
                StatusTextBlock.Text = $"{entry.Name}: {problem}";
                return;
            }
            StatusTextBlock.Text = WithNotice($"Abrindo {entry.Name}...", leftOut);
            results = await RemoteLaunch.OpenWebsites(targets, _masterCertificate, addresses);
        }
        else
        {
            if (!TypedText.TryParseCommands(entry.Target, out var commands, out var problem, out _))
            {
                StatusTextBlock.Text = $"{entry.Name}: {problem}";
                return;
            }
            StatusTextBlock.Text = WithNotice($"Abrindo {entry.Name}...", leftOut);
            results = await RemoteLaunch.OpenPrograms(targets, _masterCertificate, commands);
        }
        StatusTextBlock.Text = WithNotice(LaunchText.Summary(entry.Name, results), leftOut);
    }

    private void DeleteHighlighted(ItemCollection items, LaunchKind kind, KeyEventArgs e)
    {
        if (e.Key != Key.Delete
            || items.OfType<MenuItem>().FirstOrDefault(item => item.IsHighlighted && item.Tag is MenuEntry) is not { Tag: MenuEntry entry } highlighted)
        {
            return;
        }
        e.Handled = true;

        try
        {
            SavedMenu.Save(MenuPath(kind), SavedMenu.Remove(SavedMenu.Load(MenuPath(kind)), entry.Name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowWarning($"Não foi possível tirar \"{entry.Name}\" do menu: {ErrorText.Describe(ex)}");
            return;
        }

        items.Remove(highlighted);
        if (!items.OfType<MenuItem>().Any(item => item.Tag is MenuEntry))
        {
            foreach (var extra in items.Cast<object>()
                         .Where(item => item is Separator || item is MenuItem { Tag: MenuHintTag })
                         .ToList())
            {
                items.Remove(extra);
            }
        }
        StatusTextBlock.Text = $"\"{entry.Name}\" saiu do menu.";
    }

    private void ShowLaunchWindow(LaunchKind kind, IReadOnlyList<KnownMachine> targets)
    {
        var action = LaunchAction(kind);
        if (!SupportedTargets(ref targets, action))
        {
            return;
        }
        ShowOnce(kind == LaunchKind.Website ? "site" : "programa", targets, () =>
        {
            Window window = kind == LaunchKind.Website
                ? new OpenWebsiteWindow(targets, IsWholeClass(targets, action), _masterCertificate)
                : new OpenProgramWindow(targets, IsWholeClass(targets, action), _masterCertificate);
            window.Owner = this;
            return window;
        });
    }

    private static ToolbarAction LaunchAction(LaunchKind kind) =>
        kind == LaunchKind.Website ? ToolbarAction.OpenWebsite : ToolbarAction.OpenProgram;

    private static string MenuPath(LaunchKind kind) =>
        kind == LaunchKind.Website ? SavedMenu.WebsitesPath : SavedMenu.ProgramsPath;
}
