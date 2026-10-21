using Midview.Agent;
using Midview.Protocol;
using Xunit;

// Against a temp state file, never %ProgramData%\Midview.
public sealed class ClassHomeStateTests : IDisposable
{
    private const string Home = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Other = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

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

    private static ClassHomeClaim ClaimOf(string name, bool takeOver = false) =>
        new(name, ClassFolderRemote.Port, takeOver);

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
    public void FirstClaim_RecordsTheClaimer_AndKeepsIt()
    {
        var state = Create();

        var (ok, detail) = state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);

        Assert.True(ok);
        Assert.Equal("este PC guarda as pastas", detail);
        var expected = new ClassHome(Home, "PROF-1", "10.0.0.5", ClassFolderRemote.Port);
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
            Home.ToLowerInvariant(), "10.0.0.9", new ClassHomeClaim("prof-1", 28904, false), lessonOn: true);

        Assert.True(ok);
        Assert.Equal("este PC guarda as pastas", detail);
        var expected = new ClassHome(Home, "PROF-1", "10.0.0.9", 28904);
        Assert.Equal(expected, state.Current);
        Assert.Equal(expected, Create().Current);
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

    // Ruling 5: the home's own PC with another certificate (Midview
    // reinstalled, paired again, or another Windows account on it).
    [Fact]
    public void ClaimWithTheHomesName_AndAnotherThumbprint_IsTheSamePc()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);

        var (ok, detail) = state.Claim(Other, "10.0.0.6", ClaimOf("prof-1"), lessonOn: true);

        Assert.True(ok);
        Assert.Equal("este PC guarda as pastas", detail);
        var expected = new ClassHome(Other, "PROF-1", "10.0.0.6", ClassFolderRemote.Port);
        Assert.Equal(expected, state.Current);
        Assert.Equal(expected, Create().Current);
        Assert.True(TryStart(state, Other));
        Assert.False(TryStart(state, Home));
    }

    [Fact]
    public void ClaimByAnotherIdentity_IsRefused_NamingTheHome()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);
        var before = File.ReadAllBytes(_statePath);

        var (ok, detail) = state.Claim(Other, "10.0.0.7", ClaimOf("PROF-2"), lessonOn: false);

        Assert.False(ok);
        Assert.Equal("As pastas das turmas ficam no PC PROF-1", detail);
        Assert.StartsWith(ClassHomeClaim.OtherHomeText, detail);
        Assert.Equal(new ClassHome(Home, "PROF-1", "10.0.0.5", ClassFolderRemote.Port), state.Current);
        Assert.Equal(before, File.ReadAllBytes(_statePath));
    }

    [Fact]
    public void TakeOver_ReplacesTheHome()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);

        var (ok, detail) = state.Claim(Other, "10.0.0.7", ClaimOf("PROF-2", takeOver: true), lessonOn: false);

        Assert.True(ok);
        Assert.Equal("as pastas passaram para este PC", detail);
        var expected = new ClassHome(Other, "PROF-2", "10.0.0.7", ClassFolderRemote.Port);
        Assert.Equal(expected, state.Current);
        Assert.Equal(expected, Create().Current);
        Assert.False(TryStart(state, Home));
        Assert.True(TryStart(state, Other));
    }

    // Plan decision 7: the folders move only while no lesson is on.
    [Fact]
    public void TakeOver_WhileALessonIsOn_IsRefused()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);

        var (ok, detail) = state.Claim(Other, "10.0.0.7", ClaimOf("PROF-2", takeOver: true), lessonOn: true);

        Assert.False(ok);
        Assert.Equal("Encerre a aula antes de passar as pastas.", detail);
        Assert.Equal(new ClassHome(Home, "PROF-1", "10.0.0.5", ClassFolderRemote.Port), state.Current);
    }

    // Review Focus 1: two teacher PCs share one teacher identity (one
    // certificate); only the claimed name is the home.
    [Fact]
    public void SameThumbprint_OtherName_IsRefused()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false);

        var (ok, detail) = state.Claim(Home, "10.0.0.7", ClaimOf("PROF-2"), lessonOn: false);

        Assert.False(ok);
        Assert.Equal("As pastas das turmas ficam no PC PROF-1", detail);
        Assert.Equal(new ClassHome(Home, "PROF-1", "10.0.0.5", ClassFolderRemote.Port), state.Current);
    }

    [Fact]
    public void NoHome_TryStart_RecordsTheStarterWithNoName()
    {
        var state = Create();

        Assert.True(state.TryStart(Home, "10.0.0.5", out var refusal));

        Assert.Equal("", refusal);
        var expected = new ClassHome(Home, "", "10.0.0.5", ClassFolderRemote.Port);
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
        Assert.Equal(new ClassHome(Home, "PROF-1", "10.0.0.5", ClassFolderRemote.Port), state.Current);
    }

    // Decision 4: a Master from part 1 (no claim) started a lesson; its next
    // claim, as a part 2 Master with the same certificate, names the home.
    [Fact]
    public void RecordedWithNoName_TheNextSameThumbprintClaim_FillsTheName()
    {
        var state = Create();
        state.TryStart(Home, "10.0.0.5", out _);

        var (ok, detail) = state.Claim(Home, "10.0.0.6", ClaimOf("PROF-1"), lessonOn: true);

        Assert.True(ok);
        Assert.Equal("este PC guarda as pastas", detail);
        var expected = new ClassHome(Home, "PROF-1", "10.0.0.6", ClassFolderRemote.Port);
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

    [Fact]
    public void ASenderOrClaimThatIsntOne_IsRefused_AndNothingRecorded()
    {
        var state = Create();

        Assert.Equal((false, "pedido inválido"), state.Claim("", "10.0.0.5", ClaimOf("PROF-1"), lessonOn: false));
        Assert.Equal((false, "pedido inválido"), state.Claim(Home, "não é endereço", ClaimOf("PROF-1"), lessonOn: false));
        Assert.Equal((false, "pedido inválido"), state.Claim(Home, "10.0.0.5", ClaimOf(""), lessonOn: false));
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

    [Fact]
    public void WellFormedJson_WithABadThumbprint_IsCorrupt()
    {
        File.WriteAllText(_statePath, "{\"Thumbprint\":\"xyz\",\"Name\":\"PROF-1\",\"Address\":\"10.0.0.5\",\"Port\":28903}");

        var state = Create();

        Assert.Null(state.Current);
        Assert.True(File.Exists(_statePath + ".corrupt"));
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
