using System;
using System.Collections.Generic;
using System.Linq;
using Midview.Master;
using Xunit;

namespace Midview.Master.Tests;

// PhoneLauncher3Home against a fake Launcher3 (the school's Multilaser M10):
// the home pages emptied, the class's apps in the dock, "Adicionar ícone à
// tela inicial" and "Mostrar Google App" off.
public sealed class PhoneLauncher3HomeTests
{
    private static (bool Done, TimeSpan Took) Run(FakeLauncher3 launcher)
    {
        var elapsed = TimeSpan.Zero;
        var done = new PhoneLauncher3Home(launcher.Shell, pause => elapsed += pause, () => elapsed).Run();
        return (done, elapsed);
    }

    private static List<string> Drags(FakeLauncher3 launcher) =>
        launcher.Commands.Where(c => c.StartsWith("input draganddrop", StringComparison.Ordinal)).ToList();

    // The drags to the top of the screen, where "Remover" shows.
    private static List<string> Removals(FakeLauncher3 launcher) =>
        Drags(launcher).Where(c => c.EndsWith(" 64 2500", StringComparison.Ordinal)).ToList();

    private static FakeLauncher3.Item Folder(string label) => new() { Label = label, Folder = true };

    private static FakeLauncher3.Item Widget(string label) => new() { Label = label, Widget = true };

    [Fact]
    public void A_messy_tablet_ends_with_empty_pages_the_class_dock_and_both_settings_off()
    {
        var launcher = new FakeLauncher3
        {
            Pages = new()
            {
                new() { FakeLauncher3.Item.Icon("Contatos"), Folder("Google"), Widget("Dinossauro do Chrome") },
                new() { FakeLauncher3.Item.Icon("Planilhas") },
            },
            Dock = new() { "Play Store", "Chrome", "Fotos", "Relógio" },
        };

        var (done, took) = Run(launcher);

        Assert.True(done);
        Assert.True(launcher.Empty);
        Assert.Equal(FakeLauncher3.ClassApps, launcher.Dock);
        Assert.False(launcher.AddIcon);
        Assert.False(launcher.GoogleApp);
        Assert.Empty(launcher.UninstallAsked);
        Assert.True(took < PhoneLauncher3Home.Limit);
        // The settings first, before anything is dragged; the date is never dragged.
        Assert.True(launcher.Commands.FindIndex(c => c.StartsWith("am start", StringComparison.Ordinal))
                    < launcher.Commands.FindIndex(c => c.StartsWith("input draganddrop", StringComparison.Ordinal)));
        Assert.DoesNotContain(Drags(launcher), d => d.StartsWith("input draganddrop 400 211 ", StringComparison.Ordinal));
        // Four on the pages, three in the dock: Chrome stays once it leads it.
        Assert.Equal(7, Removals(launcher).Count);
        Assert.Equal(4, Drags(launcher).Count(d => d.EndsWith(" 680 1115 2500", StringComparison.Ordinal)));
        Assert.Equal("rm -f " + PhoneLauncher3Home.DumpFile, launcher.Commands[^1]);
    }

    [Fact]
    public void A_dock_that_starts_with_the_class_apps_keeps_them()
    {
        var launcher = new FakeLauncher3 { Dock = new() { "Chrome", "Câmera", "Relógio" } };

        Assert.True(Run(launcher).Done);

        Assert.Equal(FakeLauncher3.ClassApps, launcher.Dock);
        // Only Relógio taken off (the third in the dock); Fotos, Files and Calculadora dragged in.
        Assert.Equal(new[] { "input draganddrop 400 1115 400 64 2500" }, Removals(launcher));
        Assert.Equal(3, Drags(launcher).Count(d => d.EndsWith(" 680 1115 2500", StringComparison.Ordinal)));
    }

    [Fact]
    public void An_arranged_tablet_is_only_read()
    {
        var launcher = new FakeLauncher3 { Dock = FakeLauncher3.ClassApps.ToList(), AddIcon = false, GoogleApp = false };

        Assert.True(Run(launcher).Done);

        Assert.Empty(Drags(launcher));
        Assert.DoesNotContain(launcher.Commands, c => c.StartsWith("input tap", StringComparison.Ordinal));
    }

    [Fact]
    public void An_app_the_student_installed_goes_to_remover_on_the_left_never_to_desinstalar()
    {
        var game = FakeLauncher3.Item.Icon("Jogo");
        game.UserApp = true;
        var launcher = new FakeLauncher3 { Pages = new() { new() { game } }, Dock = FakeLauncher3.ClassApps.ToList() };

        Assert.True(Run(launcher).Done);

        Assert.True(launcher.Empty);
        Assert.Empty(launcher.UninstallAsked);
        // Aimed at the middle first (where "Remover" shows alone), then at the left quarter.
        Assert.Equal(new[] { "input draganddrop 144 375 400 64 2500", "input draganddrop 144 375 200 64 2500" }, Removals(launcher));
        Assert.DoesNotContain(Drags(launcher), d => d.Contains(" 600 64 ", StringComparison.Ordinal));
    }

    [Fact]
    public void The_uninstall_question_is_never_answered_and_ends_it()
    {
        var odd = FakeLauncher3.Item.Icon("Jogo");
        odd.UninstallOnly = true;
        var launcher = new FakeLauncher3 { Pages = new() { new() { odd } }, Dock = FakeLauncher3.ClassApps.ToList() };

        Assert.False(Run(launcher).Done);

        // Closed with Back right after each drop, and no more than twice.
        Assert.InRange(launcher.UninstallAsked.Count, 1, 2);
        var drags = launcher.Commands.Select((c, i) => (c, i)).Where(p => p.c.StartsWith("input draganddrop", StringComparison.Ordinal)).ToList();
        Assert.All(drags, drag => Assert.Equal(
            "input keyevent KEYCODE_BACK", launcher.Commands.Skip(drag.i + 1).First(c => c.StartsWith("input", StringComparison.Ordinal))));
        Assert.DoesNotContain(launcher.Commands.Skip(drags[0].i), c => c.StartsWith("input tap", StringComparison.Ordinal));
        Assert.Single(launcher.Pages[0]);
    }

    [Fact]
    public void Only_the_class_apps_on_the_tablet_go_in_the_dock()
    {
        var launcher = new FakeLauncher3();
        launcher.Packages.Remove("com.google.android.apps.photos");
        launcher.Packages.Remove("com.google.android.apps.nbu.files");

        Assert.True(Run(launcher).Done);

        Assert.Equal(new[] { "Chrome", "Câmera", "Calculadora" }, launcher.Dock);
    }

    [Fact]
    public void An_app_further_down_the_drawer_is_scrolled_to()
    {
        var launcher = new FakeLauncher3();
        launcher.DrawerApps = Enumerable.Range(1, 40).Select(i => $"App {i:00}").Concat(FakeLauncher3.ClassApps).ToList();

        Assert.True(Run(launcher).Done);

        Assert.Equal(FakeLauncher3.ClassApps, launcher.Dock);
        Assert.Contains(launcher.Commands, c => c == "input swipe 400 1040 400 268 500");
    }

    [Fact]
    public void A_launcher_without_the_google_switch_needs_only_the_other()
    {
        var launcher = new FakeLauncher3 { HasGoogleRow = false };

        Assert.True(Run(launcher).Done);

        Assert.False(launcher.AddIcon);
    }

    [Fact]
    public void A_switch_that_stays_on_ends_it_before_anything_is_dragged()
    {
        var launcher = new FakeLauncher3 { SwitchesStuck = true, Pages = new() { new() { FakeLauncher3.Item.Icon("Contatos") } } };

        Assert.False(Run(launcher).Done);

        Assert.True(launcher.AddIcon);
        Assert.Empty(Drags(launcher));
        Assert.Equal("input keyevent KEYCODE_HOME", launcher.Commands[^2]);
    }

    [Fact]
    public void Settings_that_do_not_open_stop_it_before_any_tap()
    {
        var launcher = new FakeLauncher3 { StartFails = true };

        Assert.False(Run(launcher).Done);

        Assert.DoesNotContain(launcher.Commands, c => c.StartsWith("input tap", StringComparison.Ordinal));
        Assert.DoesNotContain(launcher.Commands, c => c.StartsWith("uiautomator", StringComparison.Ordinal));
    }

    [Fact]
    public void Unreadable_screens_end_it_in_time()
    {
        var launcher = new FakeLauncher3 { DumpsFail = true };

        var (done, took) = Run(launcher);

        Assert.False(done);
        Assert.True(took <= PhoneLauncher3Home.Limit);
        Assert.Empty(Drags(launcher));
    }

    [Theory]
    [InlineData("priority=0 preferredOrder=0 match=0x108000 specificIndex=-1 isDefault=true\ncom.android.launcher3/com.android.searchlauncher.SearchLauncher\n", true)]
    [InlineData("com.android.launcher3/.Launcher\n", true)]
    [InlineData("priority=0 preferredOrder=0 match=0x108000 specificIndex=-1 isDefault=false\nandroid/com.android.internal.app.ResolverActivity\n", false)]
    [InlineData("com.miui.home/.launcher.Launcher\n", false)]
    [InlineData("com.google.android.apps.nexuslauncher/.NexusLauncherActivity\n", false)]
    [InlineData("", false)]
    public void The_home_is_launcher3_only_when_it_resolves_to_it(string resolved, bool launcher3)
    {
        Assert.Equal(launcher3, PhoneLauncher3Home.IsHome(resolved));
    }

    [Fact]
    public void The_class_apps_are_the_tablets()
    {
        Assert.Equal(
            new[] { "com.android.chrome", "com.android.camera2", "com.google.android.apps.photos", "com.google.android.apps.nbu.files", "com.google.android.calculator" },
            PhoneLauncher3Home.DockApps.Select(app => app.Package));
    }
}
