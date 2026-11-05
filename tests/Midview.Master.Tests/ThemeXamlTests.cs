using System.Text.RegularExpressions;
using System.Windows;
using Midview.Master;
using Xunit;

public class ThemeXamlTests
{
    // Keys the code puts into the resources while the app runs.
    private static readonly HashSet<string> RuntimeKeys = new() { "TileWidth", "TileHeight" };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Midview.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<string> DefinedKeys(string text) =>
        Regex.Matches(text, "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value);

    [Fact]
    public void EveryResourceKeyUsedInXamlIsDefined()
    {
        var masterDir = Path.Combine(RepoRoot(), "src", "Midview.Master");
        var sep = Path.DirectorySeparatorChar;
        var files = Directory.GetFiles(masterDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .ToList();
        Assert.NotEmpty(files);

        var themeKeys = new HashSet<string>();
        var themesDir = Path.Combine(masterDir, "Themes");
        if (Directory.Exists(themesDir))
            foreach (var theme in Directory.GetFiles(themesDir, "*.xaml"))
                foreach (var key in DefinedKeys(File.ReadAllText(theme)))
                    themeKeys.Add(key);

        var missing = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var local = new HashSet<string>(DefinedKeys(text));
            var used = Regex.Matches(text, @"\{(?:StaticResource|DynamicResource)\s+([^\s}]+)\s*\}")
                .Select(m => m.Groups[1].Value)
                .Where(k => !k.StartsWith("{"))
                .Distinct();
            foreach (var key in used)
                if (!themeKeys.Contains(key) && !local.Contains(key) && !RuntimeKeys.Contains(key))
                    missing.Add($"{Path.GetRelativePath(masterDir, file)}: {key}");
        }

        Assert.True(missing.Count == 0, "Undefined resource keys:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryWindowIsAMidviewWindow()
    {
        var offenders = typeof(MainWindow).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Window).IsAssignableFrom(t))
            .Where(t => t != typeof(MidviewWindow) && !typeof(MidviewWindow).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offenders.Count == 0, "Windows not derived from MidviewWindow:\n" + string.Join("\n", offenders));
    }
}
