using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

// Against a temp folder standing in for the student's Desktop and a
// throwaway HKCU subtree standing in for their HKEY_USERS\<SID>\Software, as
// ProtectedStudentFoldersTests and DownloadFolderPolicyTests do: whoever runs
// the tests is both the student and the folders' owner, and the real Chrome
// and Edge policy keys are never touched. Chrome is a fake process.
public sealed class ClassLessonHandlerTests : IDisposable
{
    private static readonly SecurityIdentifier Me = WindowsIdentity.GetCurrent().User!;

    private readonly string _desktop = Path.Combine(Path.GetTempPath(), $"midview-desktop-{Guid.NewGuid():N}");
    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"midview-lesson-{Guid.NewGuid():N}.json");
    private readonly string _rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
    private readonly RegistryKey _software;
    private readonly FakeInspector _inspector = new();
    private readonly List<string> _log = new();
    private DateTime _now = DateTime.Now;

    public ClassLessonHandlerTests()
    {
        Directory.CreateDirectory(_desktop);
        _software = Registry.CurrentUser.CreateSubKey(_rootPath, writable: true);
    }

    public void Dispose()
    {
        _software.Dispose();
        try { Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false); } catch { }
        foreach (var path in new[] { _statePath, _statePath + ".tmp", _statePath + ".corrupt" })
        {
            try { File.Delete(path); } catch { }
        }
        try
        {
            MakeDeletable();
            Directory.Delete(_desktop, recursive: true);
        }
        catch
        {
        }
    }

    // Undoes the protection, so the test can clean up.
    private void MakeDeletable()
    {
        foreach (var directory in Directory.EnumerateDirectories(_desktop).Append(_desktop))
        {
            var security = new DirectorySecurity();
            security.AddAccessRule(new FileSystemAccessRule(Me, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(security);
        }
    }

    private ClassLessonHandler Create(
        Func<(string Desktop, SecurityIdentifier Student)?>? loggedOnStudent = null,
        Func<SecurityIdentifier, RegistryKey?>? openUserSoftware = null) =>
        new(_statePath, loggedOnStudent ?? (() => (_desktop, Me)),
            openUserSoftware ?? (_ => Registry.CurrentUser.CreateSubKey(_rootPath, writable: true)),
            new AppBlockEnforcer(_inspector), Me, () => _now, _log.Add);

    private ClassLesson Lesson(string turma, long savedAt) =>
        new(savedAt, Guid.NewGuid(), turma, new DateTimeOffset(_now).ToUnixTimeSeconds(), Ended: false);

    private string FolderOf(string turma) => Path.Combine(_desktop, "Pasta da turma – " + turma);

    private static bool IsProtected(string folder) => new DirectoryInfo(folder).GetAccessControl().AreAccessRulesProtected;

    private object? Policy(string browserKey)
    {
        using var key = _software.OpenSubKey(browserKey);
        return key?.GetValue("DownloadDirectory");
    }

    [Fact]
    public void Handle_StartsALesson_ProtectsTheFolder_WritesThePolicy_ClosesChrome()
    {
        var handler = Create();
        var chrome = _inspector.Start("chrome.exe");
        var edge = _inspector.Start("msedge.exe");
        _inspector.Start("winword.exe");
        var lesson = Lesson("3º A", 100);

        var (ok, detail) = handler.Handle(lesson.Encode());

        Assert.True(ok);
        Assert.Equal("aula do 3º A", detail);
        Assert.Equal(lesson, handler.Current);
        Assert.Equal(lesson, ClassLessonFile.Read(_statePath));
        var folder = FolderOf("3º A");
        Assert.True(Directory.Exists(folder));
        Assert.True(IsProtected(folder));
        Assert.Equal(folder, Policy(DownloadFolderPolicy.ChromeKey));
        Assert.Equal(folder, Policy(DownloadFolderPolicy.EdgeKey));
        Assert.Equal(new[] { chrome, edge }, _inspector.Ended);
        Assert.Equal(new[] { LabConfigCommandHandler.ChromeClosedNotice }, _inspector.Notices);
    }

    // The Master resends the lesson to a PC that comes back: the folder Chrome
    // saves into hasn't changed, so the student's Chrome stays open.
    [Fact]
    public void Handle_TheSameLessonAgain_LeavesChromeOpen()
    {
        var handler = Create();
        var lesson = Lesson("3º A", 100);
        handler.Handle(lesson.Encode());
        _inspector.Start("chrome.exe");

        var (ok, detail) = handler.Handle(lesson.Encode());
        handler.Reconcile();

        Assert.True(ok);
        Assert.Equal("aula do 3º A", detail);
        Assert.Empty(_inspector.Ended);
    }

    [Fact]
    public void Handle_OlderSavedAt_IsKept()
    {
        var handler = Create();
        var newer = Lesson("3º A", 200);
        handler.Handle(newer.Encode());

        var (ok, detail) = handler.Handle(Lesson("3º B", 100).Encode());

        Assert.True(ok);
        Assert.StartsWith("mantida a aula mais nova", detail);
        Assert.Contains("3º A", detail);
        Assert.Equal(newer, handler.Current);
        Assert.Equal(newer, ClassLessonFile.Read(_statePath));
        Assert.False(Directory.Exists(FolderOf("3º B")));
        Assert.Equal(FolderOf("3º A"), Policy(DownloadFolderPolicy.ChromeKey));
    }

    [Fact]
    public void Handle_End_RemovesThePolicy_AndReleasesTheFolder()
    {
        var handler = Create();
        var lesson = Lesson("3º A", 100);
        handler.Handle(lesson.Encode());
        var download = Path.Combine(FolderOf("3º A"), "mapa.png");
        File.WriteAllText(download, "x");
        var chrome = _inspector.Start("chrome.exe");

        var (ok, detail) = handler.Handle((lesson with { SavedAt = 200, Ended = true }).Encode());

        Assert.True(ok);
        Assert.Equal("aula encerrada", detail);
        Assert.False(handler.Current.IsOn);
        Assert.False(ClassLessonFile.Read(_statePath)!.IsOn);
        Assert.Null(Policy(DownloadFolderPolicy.ChromeKey));
        Assert.Null(Policy(DownloadFolderPolicy.EdgeKey));
        Assert.False(IsProtected(FolderOf("3º A")));
        Assert.True(File.Exists(download)); // the Agent never touches what's inside
        Assert.Equal(new[] { chrome }, _inspector.Ended);
    }

    [Fact]
    public void Handle_ChangeOfClass_MovesThePolicy_AndReleasesTheOldFolder()
    {
        var handler = Create();
        handler.Handle(Lesson("3º A", 100).Encode());
        var chrome = _inspector.Start("chrome.exe");

        var (ok, detail) = handler.Handle(Lesson("3º B", 200).Encode());

        Assert.True(ok);
        Assert.Equal("aula do 3º B", detail);
        Assert.Equal(FolderOf("3º B"), Policy(DownloadFolderPolicy.ChromeKey));
        Assert.Equal(FolderOf("3º B"), Policy(DownloadFolderPolicy.EdgeKey));
        Assert.True(IsProtected(FolderOf("3º B")));
        Assert.False(IsProtected(FolderOf("3º A")));
        Assert.Equal(new[] { chrome }, _inspector.Ended);
    }

    // The folder of a class whose lesson ended stays on the Desktop if the
    // Session couldn't take it away yet, still owned by the Agent: the
    // class's next lesson protects it again all the same.
    [Fact]
    public void Handle_TheSameClassAgain_ProtectsItsFolderAgain()
    {
        var handler = Create();
        var first = Lesson("3º A", 100);
        handler.Handle(first.Encode());
        handler.Handle((first with { SavedAt = 200, Ended = true }).Encode());
        Assert.False(IsProtected(FolderOf("3º A")));

        handler.Handle(Lesson("3º A", 300).Encode());

        Assert.True(IsProtected(FolderOf("3º A")));
        Assert.Equal(FolderOf("3º A"), Policy(DownloadFolderPolicy.ChromeKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("3º A.")]
    [InlineData("3º A ")]
    [InlineData(" 3º A")]
    [InlineData("1/2")]
    [InlineData("Turma: A")]
    [InlineData("CON")]
    [InlineData("${user_home}")]
    public void Handle_ATurmaThatIsNoFolderName_IsRefused(string turma)
    {
        var handler = Create();

        var (ok, detail) = handler.Handle(Lesson(turma, 100).Encode());

        Assert.False(ok);
        Assert.Equal("nome de turma inválido", detail);
        Assert.False(handler.Current.IsOn);
        Assert.False(File.Exists(_statePath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_desktop));
        Assert.Null(Policy(DownloadFolderPolicy.ChromeKey));
    }

    [Fact]
    public void Handle_AMalformedPayload_IsRefused()
    {
        var (ok, detail) = Create().Handle(new byte[] { 1, 2, 3 });

        Assert.False(ok);
        Assert.Equal("pedido inválido", detail);
        Assert.False(File.Exists(_statePath));
    }

    [Fact]
    public void Reconcile_NextDay_EndsTheLessonKeepingFiles()
    {
        var handler = Create();
        var lesson = Lesson("3º A", 100);
        handler.Handle(lesson.Encode());
        var download = Path.Combine(FolderOf("3º A"), "mapa.png");
        File.WriteAllText(download, "x");

        _now = _now.AddDays(1);
        handler.Reconcile();

        var ended = lesson with { SavedAt = 101, Ended = true };
        Assert.Equal(ended, handler.Current);
        Assert.Equal(ended, ClassLessonFile.Read(_statePath));
        Assert.Null(Policy(DownloadFolderPolicy.ChromeKey));
        Assert.False(IsProtected(FolderOf("3º A")));
        Assert.True(File.Exists(download));

        // The Master resending yesterday's lesson doesn't bring it back.
        var (ok, detail) = handler.Handle(lesson.Encode());
        Assert.True(ok);
        Assert.StartsWith("mantida a aula mais nova", detail);
        Assert.False(handler.Current.IsOn);
    }

    [Fact]
    public void Reconcile_NoStudentLoggedOn_DoesNothing()
    {
        var opened = 0;
        var handler = Create(() => null, _ => { opened++; return null; });
        _inspector.Start("chrome.exe");

        var (ok, detail) = handler.Handle(Lesson("3º A", 100).Encode());
        handler.Reconcile();

        Assert.True(ok);
        Assert.Equal("aula do 3º A", detail);
        Assert.True(handler.Current.IsOn);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_desktop));
        Assert.Equal(0, opened);
        Assert.Empty(_inspector.Ended);
        Assert.Empty(_log);
    }

    // Review Focus 5: the student's registry isn't loaded. The folder is still
    // theirs to use; the download folder waits, retried on every poll, and
    // the problem is logged once.
    [Fact]
    public void Reconcile_HiveNotLoaded_LogsAndSkips()
    {
        var handler = Create(openUserSoftware: _ => null);
        _inspector.Start("chrome.exe");

        var (ok, detail) = handler.Handle(Lesson("3º A", 100).Encode());
        _log.Clear();
        handler.Reconcile();
        handler.Reconcile();

        Assert.True(ok);
        Assert.StartsWith("aula do 3º A; não foi possível preparar a pasta agora: ", detail);
        Assert.Contains("registro", detail);
        Assert.True(IsProtected(FolderOf("3º A")));
        Assert.Empty(_inspector.Ended);
        Assert.Contains("registro", Assert.Single(_log));
    }

    // Review Focus 5: no Desktop folder where Windows says it is.
    [Fact]
    public void Reconcile_NoDesktopFolder_LogsAndSkips()
    {
        var missing = _desktop + "-nao-existe";
        var handler = Create(() => (missing, Me));

        var (ok, detail) = handler.Handle(Lesson("3º A", 100).Encode());
        handler.Reconcile();

        Assert.True(ok);
        Assert.StartsWith("aula do 3º A; não foi possível preparar a pasta agora: ", detail);
        Assert.False(Directory.Exists(missing));
        Assert.Null(Policy(DownloadFolderPolicy.ChromeKey));
        Assert.Contains(missing, Assert.Single(_log));
    }

    [Fact]
    public void Handle_WhenThePolicyCantBeWritten_KeepsTheLesson_AndSaysWhy()
    {
        var handler = Create(openUserSoftware: _ => Registry.CurrentUser.OpenSubKey(_rootPath, writable: false));

        var (ok, detail) = handler.Handle(Lesson("3º A", 100).Encode());
        handler.Reconcile();

        Assert.True(ok);
        Assert.StartsWith("aula do 3º A; não foi possível preparar a pasta agora: ", detail);
        Assert.True(handler.Current.IsOn);
        Assert.True(IsProtected(FolderOf("3º A")));
        Assert.Null(Policy(DownloadFolderPolicy.ChromeKey));
    }

    [Fact]
    public void Reconcile_NeverThrows()
    {
        var handler = Create(() => throw new InvalidOperationException("sem token"));

        handler.Reconcile();
        handler.Reconcile();
        handler.ReconcileAtStartup();

        Assert.Contains("sem token", Assert.Single(_log));
    }

    // A class folder left from before (an earlier lesson, a PC that was off
    // when the lesson ended) is given back to the student.
    [Fact]
    public void Reconcile_WithNoLesson_ReleasesEveryClassFolder_AndRemovesThePolicy()
    {
        var old = FolderOf("2º B");
        ProtectedStudentFolders.Protect(_desktop, Me, Me, new[] { Path.GetFileName(old) });
        DownloadFolderPolicy.Apply(_software, old);
        var handler = Create();

        handler.Reconcile();

        Assert.False(IsProtected(old));
        Assert.Null(Policy(DownloadFolderPolicy.ChromeKey));
        Assert.Null(Policy(DownloadFolderPolicy.EdgeKey));
    }

    [Fact]
    public void ReconcileAtStartup_AppliesTheSavedLesson_WithoutClosingChrome()
    {
        var lesson = Lesson("3º A", 100);
        ClassLessonFile.Write(_statePath, lesson);
        var handler = Create();
        _inspector.Start("chrome.exe");

        handler.ReconcileAtStartup();

        Assert.Equal(lesson, handler.Current);
        Assert.True(IsProtected(FolderOf("3º A")));
        Assert.Equal(FolderOf("3º A"), Policy(DownloadFolderPolicy.ChromeKey));
        Assert.Empty(_inspector.Ended);
    }

    // The service's first poll may come before ReconcileAtStartup: that
    // first look is the startup one, and doesn't close Chrome either.
    [Fact]
    public void Reconcile_TheFirstOne_DoesNotCloseChrome_TheNextOnesDo()
    {
        var lesson = Lesson("3º A", 100);
        ClassLessonFile.Write(_statePath, lesson);
        var handler = Create();
        _inspector.Start("chrome.exe");

        handler.Reconcile();
        Assert.Empty(_inspector.Ended);

        _now = _now.AddDays(1);
        handler.Reconcile();
        Assert.Single(_inspector.Ended);
    }

    [Fact]
    public void CorruptStateFile_StartsWithNoLesson()
    {
        File.WriteAllText(_statePath, "{ isto não é uma aula");

        var handler = Create();
        handler.Reconcile();

        Assert.False(handler.Current.IsOn);
        Assert.True(File.Exists(_statePath + ".corrupt"));
        Assert.False(File.Exists(_statePath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_desktop));
    }

    private sealed class FakeInspector : IProcessInspector
    {
        private readonly Dictionary<RunningProcess, ProcessFacts?> _processes = new();
        private int _nextId = 100;

        public List<RunningProcess> Ended { get; } = new();
        public List<string> Notices { get; } = new();

        public RunningProcess Start(string imageName)
        {
            var process = new RunningProcess(_nextId++, _nextId, SessionId: 1);
            _processes[process] = new ProcessFacts(
                process.ProcessId, DateTime.UnixEpoch, 1, true, false, imageName, null, false, true,
                FileOwnerKind.Trusted, imageName);
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
