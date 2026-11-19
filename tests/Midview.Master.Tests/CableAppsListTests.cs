using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Midview.Master;
using Xunit;

namespace Midview.Master.Tests;

// The apps the cable's "Midview otimizado" removes (PhoneCable.Lighten) are
// the ones the agent's "Otimizar" over the network removes too: the Android
// agent keeps a copy of the lists (optimize/CableApps.kt), and the class's
// dock apps (home/Launcher3Home.kt) are PhoneLauncher3Home's. These fail as
// soon as one side changes without the other.
public sealed class CableAppsListTests
{
    private static readonly string[] Agent = { "src", "Midview.Android", "app", "src", "main", "java", "io", "github", "arnoneto1", "midview", "agent" };

    [Theory]
    [InlineData("REMOVED", nameof(PhoneCable.RemovedApps))]
    [InlineData("TABLET", nameof(PhoneCable.TabletRemovedApps))]
    [InlineData("PHONE", nameof(PhoneCable.PhoneRemovedApps))]
    public void The_agent_removes_the_cables_apps(string kotlinName, string cableName)
    {
        var cable = cableName switch
        {
            nameof(PhoneCable.RemovedApps) => PhoneCable.RemovedApps,
            nameof(PhoneCable.TabletRemovedApps) => PhoneCable.TabletRemovedApps,
            _ => PhoneCable.PhoneRemovedApps,
        };

        Assert.Equal(cable, KotlinList(Read("optimize", "CableApps.kt"), kotlinName));
    }

    [Fact]
    public void The_agent_docks_the_cables_class_apps()
    {
        var kotlin = Read("home", "Launcher3Home.kt");
        var docked = Regex.Matches(kotlin, @"DockApp\(""([^""]+)"",\s*listOf\(([^)]*)\)\)")
            .Select(m => (m.Groups[1].Value, Strings(m.Groups[2].Value)))
            .ToList();

        Assert.Equal(PhoneLauncher3Home.DockApps.Select(app => app.Package), docked.Select(app => app.Item1));
        Assert.Equal(PhoneLauncher3Home.DockApps.Select(app => app.Names), docked.Select(app => app.Item2));
        Assert.Contains($"LAUNCHER = \"{PhoneLauncher3Home.Launcher}\"", kotlin, StringComparison.Ordinal);
        Assert.Contains($"SETTINGS_ACTION = \"{PhoneLauncher3Home.SettingsAction}\"", kotlin, StringComparison.Ordinal);
    }

    // The quoted strings of "val NAME: List<String> = listOf( ... )", line
    // comments left out.
    private static string[] KotlinList(string kotlin, string name)
    {
        var start = kotlin.IndexOf($"val {name}: List<String> = listOf(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{name} is not in CableApps.kt");
        var end = kotlin.IndexOf("\n    )", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var body = string.Join('\n', kotlin[start..end].Split('\n').Skip(1).Select(line => line.Split("//")[0]));
        return Strings(body);
    }

    private static string[] Strings(string text) => Regex.Matches(text, "\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToArray();

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(Agent).Concat(path).ToArray()));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Midview.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
