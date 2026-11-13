using System;
using System.Collections.Generic;
using System.Linq;
using Midview.Master;
using Xunit;

namespace Midview.Master.Tests;

// PhoneHomeScreen against a fake MIUI launcher: "Com Gaveta de apps", the
// home pages emptied, the class's apps in the dock, the layout locked.
public sealed class PhoneHomeScreenTests
{
    private const string Lock = PhoneHomeScreen.LockSetting;

    private static (bool Done, TimeSpan Took) Run(FakeMiuiLauncher launcher)
    {
        var elapsed = TimeSpan.Zero;
        var done = new PhoneHomeScreen(launcher.Shell, pause => elapsed += pause, () => elapsed).Run();
        return (done, elapsed);
    }

    private static List<string> Drags(FakeMiuiLauncher launcher) =>
        launcher.Commands.Where(c => c.StartsWith("input draganddrop", StringComparison.Ordinal)).ToList();

    private static List<string> LongPresses(FakeMiuiLauncher launcher) =>
        launcher.Commands.Where(c => c.StartsWith("input swipe", StringComparison.Ordinal) && c.EndsWith(" 1200", StringComparison.Ordinal)).ToList();

    [Fact]
    public void A_classic_phone_ends_in_drawer_mode_with_empty_pages_the_class_dock_and_the_layout_locked()
    {
        var launcher = new FakeMiuiLauncher
        {
            Pages = new()
            {
                new() { FakeMiuiLauncher.Item.Clock(), FakeMiuiLauncher.Item.Icon("Clima"), FakeMiuiLauncher.Item.Folder("Anotações", "Mi Vídeo", "Música") },
                new() { FakeMiuiLauncher.Item.Icon("Play Store") },
            },
            Dock = new() { "Telefone", "Chrome" },
        };

        Assert.True(Run(launcher).Done);

        Assert.True(launcher.Drawer);
        Assert.True(launcher.Empty);
        Assert.Equal(FakeMiuiLauncher.ClassApps, launcher.Dock);
        Assert.Equal("1", launcher.System[Lock]);
        // Nothing taken off before the launcher had its drawer.
        Assert.Empty(launcher.RemovedInClassic);
        // Unlocked first, locked only at the end; the drawer opens with a swipe up.
        Assert.Equal("settings put system " + Lock + " 0", launcher.Commands.First(c => c.Contains(Lock, StringComparison.Ordinal)));
        Assert.Contains("settings put system QSB_SCROLL_ENTRANCE_STATUS 0", launcher.Commands);
        Assert.Equal("rm -f " + PhoneHomeScreen.DumpFile, launcher.Commands[^1]);
    }

    [Fact]
    public void The_dock_is_filled_in_the_class_order_by_dragging_from_the_drawer()
    {
        var launcher = new FakeMiuiLauncher { Drawer = true };

        Assert.True(Run(launcher).Done);

        Assert.Equal(FakeMiuiLauncher.ClassApps, launcher.Dock);
        var drags = Drags(launcher);
        Assert.Equal(5, drags.Count);
        // Every app dropped at the dock's end (x = its right edge - 40, y = its middle).
        Assert.All(drags, drag => Assert.EndsWith(" 664 1463 2500", drag, StringComparison.Ordinal));
        // Chrome (third in the drawer) first, Calculadora (first in the drawer) last.
        Assert.StartsWith("input draganddrop 444 276 ", drags[0], StringComparison.Ordinal);
        Assert.StartsWith("input draganddrop 108 276 ", drags[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_class_apps_on_the_phone_go_in_the_dock()
    {
        var launcher = new FakeMiuiLauncher { Drawer = true };
        launcher.Packages.Remove("com.miui.gallery");
        launcher.Packages.Remove("com.miui.calculator");

        Assert.True(Run(launcher).Done);

        Assert.Equal(new[] { "Chrome", "Câmera", "Gerenciador de Arquivos" }, launcher.Dock);
        Assert.Equal(3, Drags(launcher).Count);
    }

    [Fact]
    public void A_dock_already_right_is_left_alone()
    {
        var launcher = new FakeMiuiLauncher
        {
            Drawer = true,
            Pages = new() { new() { FakeMiuiLauncher.Item.Icon("Clima") } },
            Dock = FakeMiuiLauncher.ClassApps.ToList(),
        };

        Assert.True(Run(launcher).Done);

        Assert.True(launcher.Empty);
        Assert.Equal(FakeMiuiLauncher.ClassApps, launcher.Dock);
        Assert.Empty(Drags(launcher));
        Assert.Single(LongPresses(launcher));
    }

    [Fact]
    public void A_dock_with_other_apps_is_emptied_then_filled()
    {
        var launcher = new FakeMiuiLauncher
        {
            Drawer = true,
            Dock = new() { "Chrome", "Galeria", "Telefone" },
        };

        Assert.True(Run(launcher).Done);

        Assert.Equal(FakeMiuiLauncher.ClassApps, launcher.Dock);
        Assert.Equal(5, Drags(launcher).Count);
    }

    [Fact]
    public void A_folder_drawn_without_its_preview_is_opened_from_its_menu_and_emptied()
    {
        var folder = FakeMiuiLauncher.Item.Folder("Anotações", "Clima");
        folder.NoPreview = true;
        var launcher = new FakeMiuiLauncher
        {
            Drawer = true,
            Pages = new() { new() { folder } },
            Dock = FakeMiuiLauncher.ClassApps.ToList(),
        };

        Assert.True(Run(launcher).Done);

        Assert.True(launcher.Empty);
        // "Editar pasta" is never tapped: the menu is closed and the folder opened.
        Assert.DoesNotContain("input tap 532 248", launcher.Commands.Take(launcher.Commands.IndexOf("input keyevent KEYCODE_BACK")));
    }

    [Fact]
    public void An_app_further_down_the_drawer_is_scrolled_to()
    {
        var launcher = new FakeMiuiLauncher { Drawer = true };
        launcher.DrawerApps = Enumerable.Range(1, 40).Select(i => $"App {i:00}").Concat(FakeMiuiLauncher.ClassApps).ToList();

        Assert.True(Run(launcher).Done);

        Assert.Equal(FakeMiuiLauncher.ClassApps, launcher.Dock);
        Assert.Contains(launcher.Commands, c => c == "input swipe 360 1212 360 361 500");
    }

    [Fact]
    public void A_launcher_that_stays_classic_is_left_untouched()
    {
        var launcher = new FakeMiuiLauncher { DrawerTapWorks = false, Dock = new() { "Telefone" } };

        Assert.False(Run(launcher).Done);

        Assert.False(launcher.Drawer);
        Assert.Empty(LongPresses(launcher));
        Assert.Empty(Drags(launcher));
        Assert.Empty(launcher.RemovedInClassic);
        Assert.Equal("0", launcher.System[Lock]);
        Assert.Equal("input keyevent KEYCODE_HOME", launcher.Commands[^2]);
    }

    [Fact]
    public void Settings_that_do_not_open_stop_it_before_any_tap()
    {
        var launcher = new FakeMiuiLauncher { StartFails = true };

        Assert.False(Run(launcher).Done);

        Assert.DoesNotContain(launcher.Commands, c => c.StartsWith("input tap", StringComparison.Ordinal));
        Assert.DoesNotContain(launcher.Commands, c => c.StartsWith("uiautomator", StringComparison.Ordinal));
    }

    [Fact]
    public void Unreadable_screens_end_it_in_time_without_locking()
    {
        var launcher = new FakeMiuiLauncher { DumpsFail = true };

        var (done, took) = Run(launcher);

        Assert.False(done);
        Assert.True(took <= PhoneHomeScreen.Limit);
        Assert.Equal("0", launcher.System[Lock]);
    }

    [Fact]
    public void An_item_without_remover_is_never_uninstalled_and_ends_it()
    {
        var odd = FakeMiuiLauncher.Item.Icon("Jogo");
        odd.NoRemove = true;
        var launcher = new FakeMiuiLauncher { Drawer = true, Pages = new() { new() { odd } } };

        Assert.False(Run(launcher).Done);

        Assert.DoesNotContain("input tap 532 248", launcher.Commands);
        Assert.Single(launcher.Pages[0]);
        Assert.Equal("0", launcher.System[Lock]);
    }

    [Fact]
    public void App_names_match_without_case_or_accents()
    {
        Assert.Equal(HomeScreenNode.Plain("Câmera"), HomeScreenNode.Plain(" camera "));
        Assert.Equal(HomeScreenNode.Plain("Gerenciador de Arquivos"), HomeScreenNode.Plain("gerenciador de arquivos"));
        Assert.NotEqual(HomeScreenNode.Plain("Galeria"), HomeScreenNode.Plain("Gallery Go"));
        Assert.Equal(5, PhoneHomeScreen.DockApps.Count);
        Assert.Equal("com.android.chrome", PhoneHomeScreen.DockApps[0].Package);
        Assert.Equal("com.miui.calculator", PhoneHomeScreen.DockApps[^1].Package);
    }

    [Fact]
    public void A_dump_that_is_not_xml_reads_as_none()
    {
        Assert.Null(HomeScreenNode.Parse("ERROR: could not get idle state."));
        Assert.Null(HomeScreenNode.Parse("<hierarchy><node bounds=\"[0,0][1,1]\"></hierarchy>"));
    }
}
