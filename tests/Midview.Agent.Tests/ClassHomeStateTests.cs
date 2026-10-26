using Midview.Agent;
using Midview.Protocol;
using Xunit;

// Against a temp state file, never %ProgramData%\Midview.
public sealed class ClassHomeStateTests : IDisposable
{
    private const string Home = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Other = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    // When the home PC became the home (ClassHome.Generation); a takeover needs a newer one.
    private const long Generation = 100;
    private const long Newer = 200;

    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"midview-home-{Guid.NewGuid():N}.json");
    private readonly List<string> _log = new();

    public void Dispose()
    {
        foreach (var path in new[] { _statePath, _statePath + ".tmp", _statePath + ".corrupt" })
        {
            try { File.Delete(path); } catch { }
        }
    }

    private ClassHomeState Create() => new(_statePath, _log.Add);

    private static ClassHomeClaim ClaimOf(string name, bool takeOver = false, long generation = Generation) =>
        new(name, ClassFolderRemote.Port, takeOver, generation);

    private static ClassHome HomeOf(string thumbprint, string name, string address, long generation = Generation) =>
        new(thumbprint, name, address, ClassFolderRemote.Port, generation);

    private static bool TryStart(ClassHomeState state, string thumbprint, string address = "10.0.0.9") =>
        state.TryStart(thumbprint, address, out _);

    [Fact]
    public void DefaultStatePath_IsInTheAgentsDataFolder()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Midview", "class-home.json");

        Assert.Equal(expected, ClassHomeState.DefaultStatePath);
    }

    [Fact]
    public void FirstClaim_RecordsTheClaimer_WithItsGeneration_AndKeepsIt()
    {
        var state = Create();

        var (ok, detail) = state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);

        Assert.True(ok);
        Assert.Equal("este PC guarda as pastas", detail);
        var expected = HomeOf(Home, "PROF-1", "10.0.0.5");
        Assert.Equal(expected, state.Current);
        Assert.Equal(expected, Create().Current);
        Assert.True(TryStart(state, Home));
        Assert.True(TryStart(state, Home.ToLowerInvariant()));
        Assert.False(TryStart(state, Other));
        Assert.Equal(expected, state.Current);
    }

    // Review Focus 2: the home's address changed (DHCP) between lessons.
    [Fact]
    public void ClaimBySameIdentity_RefreshesAddressAndPort_KeepsTheName()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);

        var (ok, detail) = state.Claim(
            Home.ToLowerInvariant(), "10.0.0.9", new ClassHomeClaim("prof-1", 28904, false, Generation), lessonOn: true);

        Assert.True(ok);
        Assert.Equal("este PC guarda as pastas", detail);
        var expected = new ClassHome(Home, "PROF-1", "10.0.0.9", 28904, Generation);
        Assert.Equal(expected, state.Current);
        Assert.Equal(expected, Create().Current);
    }

    [Fact]
    public void ClaimBySamePc_KeepsTheNewerGeneration()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);

        Assert.True(state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1", generation: Generation - 50), lessonOn: false).Ok);
        Assert.Equal(Generation, state.Current!.Generation);

        Assert.True(state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1", generation: Newer), lessonOn: false).Ok);
        Assert.Equal(Newer, state.Current!.Generation);
        Assert.Equal(Newer, Create().Current!.Generation);
    }

    [Fact]
    public void ClaimBySameIdentity_WithNothingChanged_WritesNothing()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);
        var longAgo = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(_statePath, longAgo);
        var logged = _log.Count;

        var (ok, detail) = state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);

        Assert.True(ok);
        Assert.Equal("este PC guarda as pastas", detail);
        Assert.Equal(longAgo, File.GetLastWriteTimeUtc(_statePath));
        Assert.Equal(logged, _log.Count);
    }

    // The home's own PC (its name) with another certificate (Midview
    // reinstalled, paired again, or another Windows account on it).
    [Fact]
    public void ClaimWithTheHomesName_AndAnotherThumbprint_IsTheSamePc()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);

        var (ok, detail) = state.Claim(Other, "10.0.0.6", ClaimOf("prof-1", generation: Newer), lessonOn: true);

        Assert.True(ok);
        Assert.Equal("este PC guarda as pastas", detail);
        var expected = HomeOf(Other, "PROF-1", "10.0.0.6", Newer);
        Assert.Equal(expected, state.Current);
        Assert.Equal(expected, Create().Current);
        Assert.True(TryStart(state, Other));
        Assert.False(TryStart(state, Home));
    }

    // The home PC renamed keeps its certificate and its generation.
    [Fact]
    public void SameThumbprintAndGeneration_UnderANewName_IsTheHomeRenamed()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);

        var (ok, detail) = state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1-NOVO"), lessonOn: true);

        Assert.True(ok);
        Assert.Equal("este PC guarda as pastas", detail);
        var expected = HomeOf(Home, "PROF-1-NOVO", "10.0.0.5");
        Assert.Equal(expected, state.Current);
        Assert.Equal(expected, Create().Current);
    }

    [Fact]
    public void ClaimByAnotherIdentity_IsRefused_NamingTheHome()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);
        var before = File.ReadAllBytes(_statePath);

        var (ok, detail) = state.Claim(Other, "10.0.0.7", ClaimOf("PROF-2", generation: Newer), lessonOn: false);

        Assert.False(ok);
        Assert.Equal("As pastas das turmas ficam no PC PROF-1", detail);
        Assert.StartsWith(ClassHomeClaim.OtherHomeText, detail);
        Assert.Equal(HomeOf(Home, "PROF-1", "10.0.0.5"), state.Current);
        Assert.Equal(before, File.ReadAllBytes(_statePath));
    }

    [Fact]
    public void TakeOver_WithANewerGeneration_ReplacesTheHome()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);

        var (ok, detail) = state.Claim(Other, "10.0.0.7", ClaimOf("PROF-2", takeOver: true, generation: Newer), lessonOn: false);

        Assert.True(ok);
        Assert.Equal("as pastas passaram para este PC", detail);
        var expected = HomeOf(Other, "PROF-2", "10.0.0.7", Newer);
        Assert.Equal(expected, state.Current);
        Assert.Equal(expected, Create().Current);
        Assert.False(TryStart(state, Home));
        Assert.True(TryStart(state, Other));
    }

    // An older home (the one the folders were taken from) can't take them back by itself.
    [Theory]
    [InlineData(Generation)]
    [InlineData(Generation - 1)]
    public void TakeOver_WithoutANewerGeneration_IsRefused_NamingTheHome(long generation)
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);
        var before = File.ReadAllBytes(_statePath);

        var free = state.Claim(Other, "10.0.0.7", ClaimOf("PROF-2", takeOver: true, generation: generation), lessonOn: false);
        var during = state.Claim(Other, "10.0.0.7", ClaimOf("PROF-2", takeOver: true, generation: generation), lessonOn: true);

        Assert.Equal((false, "As pastas das turmas ficam no PC PROF-1"), free);
        Assert.Equal((false, "As pastas das turmas ficam no PC PROF-1"), during);
        Assert.Equal(HomeOf(Home, "PROF-1", "10.0.0.5"), state.Current);
        Assert.Equal(before, File.ReadAllBytes(_statePath));
    }

    // A generation past the last tick a clock can show (long.MaxValue, say)
    // would put every later take-over out of reach on this PC for good:
    // refused, from any PC, whatever it claims, and nothing recorded. The last
    // tick itself is taken.
    [Fact]
    public void AClaimPastTheLastPossibleGeneration_IsRefused_AndNothingRecorded()
    {
        var state = Create();

        foreach (var generation in new[] { long.MaxValue, ClassHome.MaxGeneration + 1 })
        {
            var first = state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1", generation: generation), lessonOn: false);
            Assert.Equal((false, ClassHomeState.GenerationTooLargeText), first);
        }
        Assert.Null(state.Current);
        Assert.False(File.Exists(_statePath));

        Assert.True(state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false).Ok);
        var recorded = state.Current;
        foreach (var (thumbprint, name) in new[] { (Home, "PROF-1"), (Other, "PROF-2") })
        {
            var again = state.Claim(thumbprint, "10.0.0.7", ClaimOf(name, takeOver: true, generation: long.MaxValue), lessonOn: false);
            Assert.Equal((false, ClassHomeState.GenerationTooLargeText), again);
        }
        Assert.Equal(recorded, state.Current);
        Assert.Equal(recorded, Create().Current);

        var last = state.Claim(Other, "10.0.0.7", ClaimOf("PROF-2", takeOver: true, generation: ClassHome.MaxGeneration), lessonOn: false);
        Assert.True(last.Ok, last.Detail);
        Assert.Equal(ClassHome.MaxGeneration, state.Current!.Generation);
    }

    // Generations years ahead of any clock (a teacher PC's clock once set
    // wrong) still take newer take-overs: nothing here goes by a clock.
    [Fact]
    public void ARoomFarAheadOfTheClocks_StillTakesANewerTakeOver()
    {
        var ahead = new DateTimeOffset(new DateTime(2040, 1, 1)).UtcTicks;
        File.WriteAllText(_statePath, "{\"Thumbprint\":\"" + Home + "\",\"Name\":\"PROF-1\",\"Address\":\"10.0.0.5\",\"Port\":28903,"
            + $"\"Generation\":{ahead}}}");
        var state = Create();

        var taken = state.Claim(Other, "10.0.0.7", ClaimOf("PROF-2", takeOver: true, generation: ahead + 1), lessonOn: false);

        Assert.True(taken.Ok, taken.Detail);
        Assert.Equal(HomeOf(Other, "PROF-2", "10.0.0.7", ahead + 1), state.Current);
    }

    // Plan decision 7: the folders move only while no lesson is on.
    [Fact]
    public void TakeOver_WhileALessonIsOn_IsRefused()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);

        var (ok, detail) = state.Claim(Other, "10.0.0.7", ClaimOf("PROF-2", takeOver: true, generation: Newer), lessonOn: true);

        Assert.False(ok);
        Assert.Equal("Encerre a aula antes de passar as pastas.", detail);
        Assert.Equal(HomeOf(Home, "PROF-1", "10.0.0.5"), state.Current);
    }

    // Review Focus 1: two teacher PCs share one teacher identity (one
    // certificate); only the claimed name is the home.
    [Fact]
    public void SameThumbprint_OtherNameAndGeneration_IsRefused()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);

        var (ok, detail) = state.Claim(Home, "10.0.0.7", ClaimOf("PROF-2", generation: Newer), lessonOn: false);

        Assert.False(ok);
        Assert.Equal("As pastas das turmas ficam no PC PROF-1", detail);
        Assert.Equal(HomeOf(Home, "PROF-1", "10.0.0.5"), state.Current);
    }

    [Fact]
    public void NoHome_TryStart_RecordsTheStarterWithNoNameAndNoGeneration()
    {
        var state = Create();

        Assert.True(state.TryStart(Home, "10.0.0.5", out var refusal));

        Assert.Equal("", refusal);
        var expected = HomeOf(Home, "", "10.0.0.5", generation: 0);
        Assert.Equal(expected, state.Current);
        Assert.Equal(expected, Create().Current);
    }

    [Fact]
    public void WithAHome_TryStart_ChangesNothing_AndNamesTheHome()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);

        Assert.False(state.TryStart(Other, "10.0.0.7", out var refusal));
        Assert.True(state.TryStart(Home, "10.0.0.8", out _));

        Assert.Equal("As pastas das turmas ficam no PC PROF-1", refusal);
        Assert.Equal(HomeOf(Home, "PROF-1", "10.0.0.5"), state.Current);
    }

    // Decision 4: a Master from part 1 (no claim) started a lesson; its next
    // claim, as a part 2 Master with the same certificate, names the home.
    [Fact]
    public void RecordedWithNoName_TheNextSameThumbprintClaim_FillsTheNameAndGeneration()
    {
        var state = Create();
        state.TryStart(Home, "10.0.0.5", out _);

        var (ok, detail) = state.Claim(Home, "10.0.0.6", ClaimOf("PROF-1"), lessonOn: true);

        Assert.True(ok);
        Assert.Equal("este PC guarda as pastas", detail);
        var expected = HomeOf(Home, "PROF-1", "10.0.0.6");
        Assert.Equal(expected, state.Current);
        Assert.Equal(expected, Create().Current);
    }

    [Fact]
    public void RecordedWithNoName_AnotherIdentity_IsRefused_AsAnotherTeachersPc()
    {
        var state = Create();
        state.TryStart(Home, "10.0.0.5", out _);

        var (ok, detail) = state.Claim(Other, "10.0.0.7", ClaimOf("PROF-2"), lessonOn: false);
        var started = state.TryStart(Other, "10.0.0.7", out var refusal);

        Assert.False(ok);
        Assert.Equal("As pastas das turmas ficam no PC de outro professor", detail);
        Assert.False(started);
        Assert.Equal(detail, refusal);
    }

    // Any claim's generation is newer than a home recorded with none.
    [Fact]
    public void RecordedWithNoName_ATakeOverByAnotherPc_IsTaken()
    {
        var state = Create();
        state.TryStart(Home, "10.0.0.5", out _);

        var (ok, detail) = state.Claim(Other, "10.0.0.7", ClaimOf("PROF-2", takeOver: true), lessonOn: false);

        Assert.True(ok);
        Assert.Equal("as pastas passaram para este PC", detail);
        Assert.Equal(HomeOf(Other, "PROF-2", "10.0.0.7"), state.Current);
    }

    [Fact]
    public void ASenderOrClaimThatIsntOne_IsRefused_AndNothingRecorded()
    {
        var state = Create();

        Assert.Equal((false, "pedido inválido"), state.Claim("", "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false));
        Assert.Equal((false, "pedido inválido"), state.Claim(Home, "não é endereço", ClaimOf("PROF-1"), lessonOn: false));
        Assert.Equal((false, "pedido inválido"), state.Claim(Home, "10.0.0.5", ClaimOf(""), lessonOn: false));
        Assert.Equal((false, "pedido inválido"), state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1", generation: 0), lessonOn: false));
        // With no home anyone may start, but a sender with no identity isn't recorded.
        Assert.True(state.TryStart("", "10.0.0.5", out _));
        Assert.True(state.TryStart(Home, "", out _));

        Assert.Null(state.Current);
        Assert.False(File.Exists(_statePath));
    }

    [Fact]
    public void CorruptFile_NoHome_KeptAside()
    {
        File.WriteAllText(_statePath, "{ isto não é uma casa");

        var state = Create();

        Assert.Null(state.Current);
        Assert.True(File.Exists(_statePath + ".corrupt"));
        Assert.False(File.Exists(_statePath));
    }

    [Theory]
    [InlineData("{\"Thumbprint\":\"xyz\",\"Name\":\"PROF-1\",\"Address\":\"10.0.0.5\",\"Port\":28903,\"Generation\":1}")]
    [InlineData("{\"Thumbprint\":\"" + Home + "\",\"Name\":\"PROF-1\",\"Address\":\"10.0.0.5\",\"Port\":28903,\"Generation\":-1}")]
    public void WellFormedJson_ThatIsntAHome_IsCorrupt(string json)
    {
        File.WriteAllText(_statePath, json);

        var state = Create();

        Assert.Null(state.Current);
        Assert.True(File.Exists(_statePath + ".corrupt"));
    }

    // A file kept from before the latest generation existed (long.MaxValue,
    // say) is read at that generation: no Master's claim can go past it.
    [Fact]
    public void AFileWithAGenerationPastTheLatest_IsReadAtTheLatest()
    {
        File.WriteAllText(_statePath, "{\"Thumbprint\":\"" + Home + "\",\"Name\":\"PROF-1\",\"Address\":\"10.0.0.5\",\"Port\":28903,"
            + $"\"Generation\":{long.MaxValue}}}");

        Assert.Equal(HomeOf(Home, "PROF-1", "10.0.0.5", ClassHome.MaxGeneration), Create().Current);
    }

    [Fact]
    public void AFileWithNoGeneration_IsAHomeWithNone()
    {
        File.WriteAllText(_statePath, "{\"Thumbprint\":\"" + Home + "\",\"Name\":\"PROF-1\",\"Address\":\"10.0.0.5\",\"Port\":28903}");

        Assert.Equal(HomeOf(Home, "PROF-1", "10.0.0.5", generation: 0), Create().Current);
    }

    [Fact]
    public void WhenTheFileCantBeWritten_TheClaimFails_AndNothingChanges()
    {
        // A file where the state's folder should be.
        var blocker = Path.Combine(Path.GetTempPath(), $"midview-home-blocker-{Guid.NewGuid():N}");
        File.WriteAllText(blocker, "");
        try
        {
            var state = new ClassHomeState(Path.Combine(blocker, "class-home.json"), _log.Add);

            var (ok, detail) = state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);

            Assert.False(ok);
            Assert.StartsWith("não foi possível gravar", detail);
            Assert.Null(state.Current);
        }
        finally
        {
            File.Delete(blocker);
        }
    }
}
