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

public class AppBlockStateTextTests
{
    private static MachineBlockState State(string name, AppBlockPolicy? policy, bool oldAgent = false) =>
        new(name, policy is null && !oldAgent ? null : new AppBlockClient.StatusResult(policy, oldAgent));

    private static AppBlockPolicy Block(long savedAt, params string[] programs) => new(savedAt, AppBlockMode.BlockList, programs);

    private static AppBlockPolicy Allow(long savedAt, params string[] programs) => new(savedAt, AppBlockMode.AllowOnly, programs);

    [Fact]
    public void Summarize_GroupsMachinesByWhatTheyFollow_AndNamesTheProgramsAndWhenItWasSaved()
    {
        var text = AppBlockText.Summarize(new[]
        {
            State("A1", Block(1_700_000_000, "chrome.exe", "steam.exe")),
            State("A2", Block(1_700_000_000, "steam.exe", "chrome.exe")),
            State("A3", AppBlockPolicy.Lifted(5)),
            State("A4", null, oldAgent: true),
            State("A5", null),
        });

        var lines = text.Split('\n');
        Assert.StartsWith("Bloqueando chrome.exe, steam.exe em 2 máquinas (salvo em ", lines[0]);
        Assert.Equal("Nenhum programa bloqueado em 1 máquina", lines[1]);
        Assert.Equal("Midview de versão anterior (atualize para bloquear): A4", lines[2]);
        Assert.Equal("Sem resposta: A5", lines[3]);
    }

    [Fact]
    public void Summarize_AllowOnly_SaysWindowsAndTheListed()
    {
        var text = AppBlockText.Summarize(new[] { State("A1", Allow(1_700_000_000, "winword.exe")), State("A2", Allow(1_700_000_000)) });

        Assert.Contains("Permitindo só o Windows e winword.exe em 1 máquina", text);
        Assert.Contains("Permitindo só o Windows em 1 máquina", text);
    }

    [Fact]
    public void Summarize_LongListsAreCut()
    {
        var text = AppBlockText.Summarize(new[]
        {
            State("A1", Block(1, "a.exe", "b.exe", "c.exe", "d.exe", "e.exe", "f.exe")),
        });

        Assert.Contains("a.exe, b.exe, c.exe, d.exe e mais 2", text);
    }

    [Fact]
    public void Summarize_NobodyAnswered()
    {
        Assert.Equal("Nenhuma máquina respondeu.", AppBlockText.Summarize(Array.Empty<MachineBlockState>()));
        Assert.Equal("Sem resposta: A1, A2", AppBlockText.Summarize(new[] { State("A1", null), State("A2", null) }));
    }

    [Fact]
    public void NextSavedAt_BeatsEveryReportedBlock_EvenWhenThisClockIsBehind()
    {
        var reported = new[] { State("A1", Block(2000, "x.exe")), State("A2", AppBlockPolicy.Lifted(3000)), State("A3", null) };

        Assert.Equal(3001, AppBlockText.NextSavedAt(1000, reported));
        Assert.Equal(5000, AppBlockText.NextSavedAt(5000, reported));
        Assert.Equal(1000, AppBlockText.NextSavedAt(1000, Array.Empty<MachineBlockState>()));
    }

    [Fact]
    public void CommonActivePolicy_IsTheBlockEveryAnsweringMachineFollows()
    {
        var common = AppBlockText.CommonActivePolicy(new[]
        {
            State("A1", Block(10, "a.exe", "b.exe")),
            State("A2", Block(20, "b.exe", "a.exe")),
            State("A3", null),
            State("A4", null, oldAgent: true),
        });

        Assert.NotNull(common);
        Assert.Equal(20, common!.SavedAt);
        Assert.True(common.SameRulesAs(Block(0, "a.exe", "b.exe")));
    }

    [Fact]
    public void CommonActivePolicy_IsNullWhenMachinesDisagree_OrNothingIsBlocked()
    {
        Assert.Null(AppBlockText.CommonActivePolicy(new[] { State("A1", Block(1, "a.exe")), State("A2", Block(1, "b.exe")) }));
        Assert.Null(AppBlockText.CommonActivePolicy(new[] { State("A1", Block(1, "a.exe")), State("A2", Allow(1, "a.exe")) }));
        Assert.Null(AppBlockText.CommonActivePolicy(new[] { State("A1", Block(1, "a.exe")), State("A2", AppBlockPolicy.Lifted(1)) }));
        Assert.Null(AppBlockText.CommonActivePolicy(new[] { State("A1", AppBlockPolicy.Lifted(1)) }));
        Assert.Null(AppBlockText.CommonActivePolicy(new[] { State("A1", null) }));
    }

    [Theory]
    [InlineData("mantido o bloqueio mais novo, salvo em 01/10 10:00: bloqueando 1 programa(s)", true, true)]
    [InlineData("bloqueando 1 programa(s)", true, false)]
    [InlineData("mantido o bloqueio mais novo, salvo em 01/10 10:00: x", false, false)]
    public void KeptNewer_IsAnAckThatDidNotApplyWhatWasAsked(string detail, bool succeeded, bool expected) =>
        Assert.Equal(expected, AppBlockText.KeptNewer(succeeded, detail));

    [Fact]
    public void Badge_SaysWhatTheMachineFollows()
    {
        string Badge(AppBlockPolicy? policy, bool oldAgent = false, bool classBlock = false) =>
            AppBlockText.Badge(new AppBlockClient.StatusResult(policy, oldAgent), classBlock);

        Assert.Equal("🔒 3 bloqueados", Badge(Block(1, "a.exe", "b.exe", "c.exe")));
        Assert.Equal("🔒 1 bloqueado", Badge(Block(1, "a.exe")));
        Assert.Equal("🔒 só 2 permitidos", Badge(Allow(1, "a.exe", "b.exe")));
        Assert.Equal("🔒 só o Windows", Badge(Allow(1)));
        Assert.Equal(string.Empty, Badge(AppBlockPolicy.Lifted(1)));
        Assert.Equal(string.Empty, AppBlockText.Badge(null, true));
    }

    [Fact]
    public void Badge_WarnsAboutAnOldMidview_OnlyWhenABlockIsExpected()
    {
        var old = new AppBlockClient.StatusResult(null, true);

        Assert.StartsWith("⚠ Midview antigo", AppBlockText.Badge(old, classBlockExpected: true));
        Assert.Equal(string.Empty, AppBlockText.Badge(old, classBlockExpected: false));
    }
}

