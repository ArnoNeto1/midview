using Midview.Master;
using Midview.Protocol;
using Xunit;

public class OpenProgramsAggregatorTests
{
    private static IReadOnlyList<OpenProgram> Machine(params (string Exe, string Name)[] programs) =>
        programs.Select(program => new OpenProgram(program.Exe, program.Name, 1)).ToList();

    [Fact]
    public void CountsMachines_NotWindows_AndSortsByCountThenName()
    {
        var rows = OpenProgramsAggregator.Aggregate(new[]
        {
            Machine(("chrome.exe", "Google Chrome"), ("winword.exe", "Word")),
            Machine(("chrome.exe", "Google Chrome")),
            Machine(("chrome.exe", "Google Chrome"), ("calc.exe", "Calculadora")),
        });

        Assert.Equal(new[] { "chrome.exe", "calc.exe", "winword.exe" }, rows.Select(row => row.ExeName));
        Assert.Equal(3, rows[0].MachineCount);
        Assert.Equal(1, rows[1].MachineCount);
    }

    [Fact]
    public void AForgedLabel_DoesNotRelabelAProgramMostMachinesAgreeOn()
    {
        var rows = OpenProgramsAggregator.Aggregate(new[]
        {
            Machine(("chrome.exe", "Google Chrome")),
            Machine(("chrome.exe", "Google Chrome")),
            Machine(("chrome.exe", "Antivirus do colégio")),
        });

        Assert.Equal("Google Chrome", Assert.Single(rows).DisplayName);
    }

    [Fact]
    public void ATieIsBrokenTheSameWayEveryTime()
    {
        var a = OpenProgramsAggregator.Aggregate(new[] { Machine(("x.exe", "B")), Machine(("x.exe", "A")) });
        var b = OpenProgramsAggregator.Aggregate(new[] { Machine(("x.exe", "A")), Machine(("x.exe", "B")) });

        Assert.Equal(a.Single().DisplayName, b.Single().DisplayName);
    }

    [Fact]
    public void NoAnswers_NoRows() =>
        Assert.Empty(OpenProgramsAggregator.Aggregate(Array.Empty<IReadOnlyList<OpenProgram>>()));
}

public class AppBlockFileTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"midview-appblock-master-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        foreach (var file in new[] { _path, _path + ".tmp", _path + ".corrupt" })
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void RoundTrips()
    {
        AppBlockFile.Save(_path, new AppBlockPolicy(1234, AppBlockMode.AllowOnly, new[] { "winword.exe", "excel.exe" }));

        var loaded = AppBlockFile.Load(_path)!;

        Assert.Equal(1234, loaded.SavedAt);
        Assert.Equal(AppBlockMode.AllowOnly, loaded.Mode);
        Assert.Equal(new[] { "winword.exe", "excel.exe" }, loaded.Programs);
    }

    [Fact]
    public void ALiftedPolicy_IsKept_SoMachinesThatWereOffGetTheLiftToo()
    {
        AppBlockFile.Save(_path, AppBlockPolicy.Lifted(99));

        var loaded = AppBlockFile.Load(_path);

        Assert.NotNull(loaded);
        Assert.False(loaded!.IsActive);
        Assert.Equal(99, loaded.SavedAt);
    }

    [Fact]
    public void Missing_IsNull() => Assert.Null(AppBlockFile.Load(_path));

    [Fact]
    public void Corrupt_IsMovedAside()
    {
        File.WriteAllText(_path, "{ nope");

        Assert.Null(AppBlockFile.Load(_path));
        Assert.True(File.Exists(_path + ".corrupt"));
    }
}

public class AppBlockTextTests
{
    [Fact]
    public void AnOldAgentsEmptyAnswer_BecomesTheOldVersionText() =>
        Assert.Equal(AppBlockText.OldAgent, AppBlockText.ApplyLine(false, string.Empty));

    [Fact]
    public void AnAnswerWithDetail_IsShownAsIs() =>
        Assert.Equal("bloqueando 2 programa(s)", AppBlockText.ApplyLine(true, "bloqueando 2 programa(s)"));

    [Fact]
    public void DescribesEachMode()
    {
        Assert.Equal("bloqueando 1 programa(s)", AppBlockText.Describe(new AppBlockPolicy(1, AppBlockMode.BlockList, new[] { "a.exe" })));
        Assert.Equal("permitindo só o Windows e 1 programa(s)", AppBlockText.Describe(new AppBlockPolicy(1, AppBlockMode.AllowOnly, new[] { "a.exe" })));
        Assert.Equal("permitindo só o Windows", AppBlockText.Describe(new AppBlockPolicy(1, AppBlockMode.AllowOnly, Array.Empty<string>())));
        Assert.Equal("nenhum programa bloqueado", AppBlockText.Describe(AppBlockPolicy.Lifted(1)));
    }

    [Fact]
    public void TheSuggestions_AreAllValidAndNeverProtected()
    {
        foreach (var (_, programs) in AppBlockText.Suggestions)
        {
            foreach (var program in programs)
            {
                Assert.Equal(program, AppBlockPolicy.NormalizeExeName(program));
                Assert.False(ProtectedPrograms.IsProtected(program));
            }
        }
    }
}
