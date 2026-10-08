using Microsoft.Win32;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

public sealed class LabConfigCommandHandlerTests : IDisposable
{
    private const string ChromePath = @"Policies\Google\Chrome";

    private readonly string _rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
    private readonly RegistryKey _root;
    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"midview-labcfg-{Guid.NewGuid():N}.json");
    private readonly string _usersRoot = Path.Combine(Path.GetTempPath(), $"midview-users-{Guid.NewGuid():N}");
    private readonly string _layoutPath = Path.Combine(Path.GetTempPath(), $"midview-tb-{Guid.NewGuid():N}.xml");
    private readonly FakeInspector _inspector = new();

    public LabConfigCommandHandlerTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_rootPath, writable: true);
        Directory.CreateDirectory(_usersRoot);
    }

    public void Dispose()
    {
        _root.Dispose();
        try { Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false); } catch { }
        try { File.Delete(_statePath); } catch { }
        try { File.Delete(_statePath + ".tmp"); } catch { }
        try { Directory.Delete(_usersRoot, recursive: true); } catch { }
        try { File.Delete(_layoutPath); } catch { }
    }

    private AppBlockEnforcer _enforcer = null!;

    private LabConfigCommandHandler Create()
    {
        _enforcer = new AppBlockEnforcer(_inspector);
        var writer = new LabPolicyWriter(Registry.CurrentUser.CreateSubKey(_rootPath, writable: true), _layoutPath);
        return new LabConfigCommandHandler(_statePath, writer, _enforcer, _usersRoot);
    }

    private object? Read(string subPath, string name)
    {
        using var key = _root.OpenSubKey(subPath);
        return key?.GetValue(name);
    }

    [Fact]
    public void Handle_ANewerConfig_IsAppliedAndSaved()
    {
        var handler = Create();

        var (ok, _) = handler.Handle((LabConfig.Off(100) with { BlockDownloads = true }).Encode());

        Assert.True(ok);
        Assert.Equal(3, Read(ChromePath, "DownloadRestriction"));
        Assert.True(File.Exists(_statePath));
        Assert.True(handler.Current.BlockDownloads);
    }

    [Fact]
    public void Handle_AnOlderConfig_IsKept()
    {
        var handler = Create();
        handler.Handle((LabConfig.Off(100) with { Lockdown = true }).Encode());

        var (ok, detail) = handler.Handle((LabConfig.Off(50) with { ForceGuest = true }).Encode());

        Assert.True(ok);
        Assert.Contains("mantida a configuração mais nova", detail);
        Assert.True(handler.Current.Lockdown);
        Assert.False(handler.Current.ForceGuest);
    }

    [Fact]
    public void BlockEdge_MakesTheEnforcerEndEdge()
    {
        var handler = Create();
        handler.Handle((LabConfig.Off(100) with { BlockEdge = true }).Encode());

        var edge = _inspector.Start("msedge.exe", "Microsoft Edge");
        _inspector.Start("winword.exe");
        _enforcer.EnforceOnce();

        Assert.Equal(new[] { edge }, _inspector.Ended);
    }

    [Fact]
    public void Reset_RemovesEverything_AndStopsEndingEdge()
    {
        var handler = Create();
        handler.Handle(new LabConfig(100, true, true, true, true, true, true, true).Encode());

        var (ok, _) = handler.Reset();

        Assert.True(ok);
        using (var chrome = _root.OpenSubKey(ChromePath))
        {
            Assert.Null(chrome);
        }
        Assert.False(handler.Current.IsAnyOn);

        _inspector.Start("msedge.exe");
        _enforcer.EnforceOnce();
        Assert.Empty(_inspector.Ended);
    }

    [Fact]
    public void ClearChromeProfiles_ClosesChrome_AndDeletesEveryUsersProfile()
    {
        var handler = Create();
        foreach (var user in new[] { "aluno", "professor" })
        {
            var userData = Path.Combine(_usersRoot, user, "AppData", "Local", "Google", "Chrome", "User Data");
            Directory.CreateDirectory(Path.Combine(userData, "Default"));
            File.WriteAllText(Path.Combine(userData, "Default", "Cookies"), "x");
        }
        var chrome = _inspector.Start("chrome.exe", "Google Chrome");

        var (ok, detail) = handler.ClearChromeProfiles();

        Assert.True(ok);
        Assert.Contains("2 conta(s)", detail);
        Assert.Equal(new[] { chrome }, _inspector.Ended);
        Assert.False(Directory.Exists(Path.Combine(_usersRoot, "aluno", "AppData", "Local", "Google", "Chrome", "User Data")));
        Assert.True(Directory.Exists(Path.Combine(_usersRoot, "aluno"))); // only the Chrome data went
    }

    [Fact]
    public void ClearChromeProfiles_WhenChromeFolderIsAJunction_RefusesAndLeavesTheTargetAlone()
    {
        var handler = Create();
        // A target outside the profile, with real contents that must survive.
        var outside = Path.Combine(Path.GetTempPath(), $"midview-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(outside, "User Data", "Default"));
        File.WriteAllText(Path.Combine(outside, "User Data", "Default", "precious.txt"), "keep me");

        // The student's profile where ...\Google\Chrome is a junction to the target.
        var chromeParent = Path.Combine(_usersRoot, "aluno", "AppData", "Local", "Google");
        Directory.CreateDirectory(chromeParent);
        var mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "cmd.exe", $"/c mklink /J \"{Path.Combine(chromeParent, "Chrome")}\" \"{outside}\"")
        { UseShellExecute = false, CreateNoWindow = true })!;
        mk.WaitForExit();
        Assert.Equal(0, mk.ExitCode); // junctions need no admin; if this ever fails, the test is moot

        try
        {
            var (ok, detail) = handler.ClearChromeProfiles();

            Assert.True(File.Exists(Path.Combine(outside, "User Data", "Default", "precious.txt")),
                "the delete followed the junction and destroyed the outside target");
            Assert.DoesNotContain("1 conta", detail); // it did not count the junctioned profile as cleared
            Assert.True(ok); // the action itself still succeeds (nothing real to clear)
        }
        finally
        {
            try { Directory.Delete(outside, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ClearChromeProfiles_NothingToClear_SaysSo()
    {
        var handler = Create();

        var (ok, detail) = handler.ClearChromeProfiles();

        Assert.True(ok);
        Assert.Equal("nenhum perfil do Chrome para limpar", detail);
    }

    [Fact]
    public void ReconcileAtStartup_ReappliesTheSavedConfig()
    {
        Create().Handle((LabConfig.Off(100) with { Lockdown = true }).Encode());
        // A fresh handler (as if the Agent restarted) reads the saved state.
        var afterRestart = Create();
        using (var chrome = _root.OpenSubKey(ChromePath, writable: true))
        {
            chrome!.DeleteValue("IncognitoModeAvailability", throwOnMissingValue: false); // something cleared it meanwhile
        }

        afterRestart.ReconcileAtStartup();

        Assert.Equal(1, Read(ChromePath, "IncognitoModeAvailability"));
    }

    private sealed class FakeInspector : IProcessInspector
    {
        private readonly Dictionary<RunningProcess, ProcessFacts?> _processes = new();
        private int _nextId = 100;

        public List<RunningProcess> Ended { get; } = new();
        public List<string> Notices { get; } = new();

        public RunningProcess Start(string imageName, string? displayName = null)
        {
            var process = new RunningProcess(_nextId++, _nextId, SessionId: 1);
            _processes[process] = new ProcessFacts(
                process.ProcessId, DateTime.UnixEpoch, 1, true, false, imageName, null, false, true,
                FileOwnerKind.Trusted, displayName ?? imageName);
            return process;
        }

        public IReadOnlyList<RunningProcess> ListInteractive() =>
            _processes.Keys.Where(process => !Ended.Contains(process)).ToList();

        public ProcessFacts? Inspect(RunningProcess process) => _processes[process];

        public bool End(RunningProcess process)
        {
            Ended.Add(process);
            return true;
        }

        public void Notify(int sessionId, string message) => Notices.Add(message);
    }
}
