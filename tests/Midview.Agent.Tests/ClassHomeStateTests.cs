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

    [Fact]
    public void DefaultStatePath_IsInTheAgentsDataFolder()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Midview", "class-home.json");

        Assert.Equal(expected, ClassHomeState.DefaultStatePath);
    }

    [Fact]
    public void NoFile_NoHome_AnyoneMayStart()
    {
        var state = Create();

        Assert.Null(state.Current);
        Assert.True(state.MayStart(Home));
        Assert.True(state.MayStart(Other));
    }

    [Fact]
    public void FirstClaim_RecordsTheClaimer_AndKeepsIt()
    {
        var state = Create();

        var (ok, detail) = state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"));

        Assert.True(ok);
        Assert.Equal("este PC guarda as pastas", detail);
        var expected = new ClassHome(Home, "PROF-1", "10.0.0.5", ClassFolderRemote.Port);
        Assert.Equal(expected, state.Current);
        Assert.Equal(expected, Create().Current);
        Assert.True(state.MayStart(Home));
        Assert.True(state.MayStart(Home.ToLowerInvariant()));
        Assert.False(state.MayStart(Other));
    }

    // Review Focus 2: the home's address changed (DHCP) between lessons.
    [Fact]
    public void ClaimBySameIdentity_RefreshesAddressAndPort_KeepsTheName()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"));

        var (ok, detail) = state.Claim(Home.ToLowerInvariant(), "10.0.0.9", new ClassHomeClaim("prof-1", 28904, false));

        Assert.True(ok);
        Assert.Equal("este PC guarda as pastas", detail);
        var expected = new ClassHome(Home, "PROF-1", "10.0.0.9", 28904);
        Assert.Equal(expected, state.Current);
        Assert.Equal(expected, Create().Current);
    }

    [Fact]
    public void ClaimByAnotherIdentity_IsRefused_NamingTheHome()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"));
        var before = File.ReadAllBytes(_statePath);

        var (ok, detail) = state.Claim(Other, "10.0.0.7", ClaimOf("PROF-2"));

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
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"));

        var (ok, detail) = state.Claim(Other, "10.0.0.7", ClaimOf("PROF-2", takeOver: true));

        Assert.True(ok);
        Assert.Equal("as pastas passaram para este PC", detail);
        var expected = new ClassHome(Other, "PROF-2", "10.0.0.7", ClassFolderRemote.Port);
        Assert.Equal(expected, state.Current);
        Assert.Equal(expected, Create().Current);
        Assert.False(state.MayStart(Home));
        Assert.True(state.MayStart(Other));
    }

    // Review Focus 1: two teacher PCs share one teacher identity (one
    // certificate); only the claimed name is the home.
    [Fact]
    public void SameThumbprint_OtherName_IsRefused()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"));

        var (ok, detail) = state.Claim(Home, "10.0.0.7", ClaimOf("PROF-2"));

        Assert.False(ok);
        Assert.Equal("As pastas das turmas ficam no PC PROF-1", detail);
        Assert.Equal(new ClassHome(Home, "PROF-1", "10.0.0.5", ClassFolderRemote.Port), state.Current);
    }

    // Decision 4: a Master from part 1 (no claim) started a lesson; its next
    // claim, as a part 2 Master with the same certificate, names the home.
    [Fact]
    public void RecordedWithNoName_TheNextSameThumbprintClaim_FillsTheName()
    {
        var state = Create();
        state.RecordStarter(Home, "10.0.0.5");
        Assert.Equal(new ClassHome(Home, "", "10.0.0.5", ClassFolderRemote.Port), state.Current);

        var (ok, detail) = state.Claim(Home, "10.0.0.6", ClaimOf("PROF-1"));

        Assert.True(ok);
        Assert.Equal("este PC guarda as pastas", detail);
        var expected = new ClassHome(Home, "PROF-1", "10.0.0.6", ClassFolderRemote.Port);
        Assert.Equal(expected, state.Current);
        Assert.Equal(expected, Create().Current);
    }

    [Fact]
    public void RecordedWithNoName_AnotherIdentity_IsRefused_AsAnotherTeacherPc()
    {
        var state = Create();
        state.RecordStarter(Home, "10.0.0.5");

        var (ok, detail) = state.Claim(Other, "10.0.0.7", ClaimOf("PROF-2"));

        Assert.False(ok);
        Assert.Equal(ClassHomeClaim.OtherHomeText + "outro PC de professor", detail);
    }

    [Fact]
    public void RecordStarter_WithAHome_ChangesNothing()
    {
        var state = Create();
        state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"));

        state.RecordStarter(Other, "10.0.0.7");
        state.RecordStarter(Home, "10.0.0.8");

        Assert.Equal(new ClassHome(Home, "PROF-1", "10.0.0.5", ClassFolderRemote.Port), state.Current);
    }

    [Fact]
    public void ASenderThatIsNoThumbprintOrAddress_IsRefused_AndNothingRecorded()
    {
        var state = Create();

        Assert.Equal((false, "pedido inválido"), state.Claim("", "10.0.0.5", ClaimOf("PROF-1")));
        Assert.Equal((false, "pedido inválido"), state.Claim(Home, "não é endereço", ClaimOf("PROF-1")));
        state.RecordStarter("", "10.0.0.5");
        state.RecordStarter(Home, "");

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

            var (ok, detail) = state.Claim(Home, "10.0.0.5", ClaimOf("PROF-1"));

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
