using Midview.Agent;
using Xunit;

// The programs kept for the students who weren't logged on, and the logon
// pass that removes them -- with a fake clock, a fake console and a fake
// uninstaller, so nothing real is read or removed.
public sealed class PendingUninstallsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"midview-pending-{Guid.NewGuid():N}.json");
    private DateTime _now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        foreach (var file in new[] { _path, _path + ".tmp", _path + ".corrupt" })
        {
            File.Delete(file);
        }
    }

    private PendingUninstalls Create() => new(_path, () => _now);

    [Fact]
    public void Added_AreCurrent_InOrder_EachOnce()
    {
        var pending = Create();

        pending.Add(new[] { "Roblox Player", "Discord", " discord ", "" });

        Assert.Equal(new[] { "Discord", "Roblox Player" }, pending.Current());
    }

    [Fact]
    public void TheyLast_TheirDays_AndThenGo()
    {
        var pending = Create();
        pending.Add(new[] { "Discord" });

        _now += PendingUninstalls.KeepFor - TimeSpan.FromMinutes(1);
        Assert.Equal(new[] { "Discord" }, pending.Current());

        _now += TimeSpan.FromMinutes(2);
        Assert.Empty(pending.Current());
    }

    [Fact]
    public void AskingAgain_StartsTheDaysOver()
    {
        var pending = Create();
        pending.Add(new[] { "Discord" });
        _now += TimeSpan.FromDays(10);

        pending.Add(new[] { "Discord" });
        _now += TimeSpan.FromDays(10);

        Assert.Equal(new[] { "Discord" }, pending.Current());
    }

    [Fact]
    public void TheySurviveARestart()
    {
        Create().Add(new[] { "Discord" });

        Assert.Equal(new[] { "Discord" }, Create().Current());
    }

    [Fact]
    public void ABadFile_MeansNone_AndIsKeptAside()
    {
        File.WriteAllText(_path, "not json");

        Assert.Empty(Create().Current());
        Assert.True(File.Exists(_path + ".corrupt"));
    }

    [Fact]
    public void LogonPass_RemovesThemOncePerLogon()
    {
        var pending = Create();
        pending.Add(new[] { "Discord" });
        string? student = null;
        var asked = new List<string>();
        var worker = new PendingUninstallWorker(() => student, pending, names => { asked.Add($"{student}: {string.Join(",", names)}"); return names; });

        worker.RunOnce(); // nobody logged on
        student = "S-1-5-21-aluno1";
        worker.RunOnce();
        worker.RunOnce(); // same student, same logon: not again
        student = "S-1-5-21-aluno2";
        worker.RunOnce();
        student = null;
        worker.RunOnce();
        student = "S-1-5-21-aluno2"; // logged off and back on
        worker.RunOnce();

        Assert.Equal(new[] { "S-1-5-21-aluno1: Discord", "S-1-5-21-aluno2: Discord", "S-1-5-21-aluno2: Discord" }, asked);
    }

    [Fact]
    public void LogonPass_NothingPending_RunsNothing()
    {
        var asked = 0;
        var worker = new PendingUninstallWorker(() => "S-1-5-21-aluno1", Create(), names => { asked++; return names; });

        worker.RunOnce();

        Assert.Equal(0, asked);
    }

    [Fact]
    public void LogonPass_AFailure_IsNotRetriedForTheSameLogon()
    {
        var pending = Create();
        pending.Add(new[] { "Discord" });
        var asked = 0;
        var worker = new PendingUninstallWorker(() => "S-1-5-21-aluno1", pending, _ => { asked++; throw new IOException("in use"); });

        Assert.Throws<IOException>(worker.RunOnce);
        worker.RunOnce();

        Assert.Equal(1, asked);
    }

    [Theory]
    [InlineData("Opera GX Stable 112.0.5197.60", "Opera GX Stable")]
    [InlineData("7-Zip 23.01 (x64)", "7-Zip (x64)")]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.40.33810", "Microsoft Visual C++ 2015-2022 Redistributable (x64)")]
    [InlineData("Notepad++ (8.6.2)", "Notepad++")]
    [InlineData("Mozilla Firefox v131.0.3 (x64 pt-BR)", "Mozilla Firefox (x64 pt-BR)")]
    [InlineData("Microsoft Office 2016", "Microsoft Office 2016")]
    [InlineData("Java 8 Update 431", "Java 8 Update 431")]
    [InlineData("Node.js", "Node.js")]
    [InlineData("Discord", "Discord")]
    [InlineData("1.2.3", "")]
    public void WithoutVersion_TakesOutOnlyDottedNumbers(string displayName, string expected)
    {
        Assert.Equal(expected, ProgramUninstaller.WithoutVersion(displayName));
    }
}
