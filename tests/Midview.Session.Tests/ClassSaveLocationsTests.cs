using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using Midview.Protocol;
using Midview.Session;
using Xunit;

public sealed partial class ClassSaveLocationsTests : IDisposable
{
    private const string Original = @"%USERPROFILE%\Downloads";

    private static readonly Guid Lesson1 = Guid.NewGuid();
    private static readonly Guid Lesson2 = Guid.NewGuid();

    private readonly string _rootPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";
    private readonly RegistryKey _software;
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "midview-csl-" + Guid.NewGuid().ToString("N"));
    private readonly string _desktop;
    private readonly string _statePath;
    private readonly FakeShell _shell;
    private readonly List<string> _links = new();
    private ClassLesson? _lesson;
    private readonly List<string> _log = new();

    public ClassSaveLocationsTests()
    {
        _software = Registry.CurrentUser.CreateSubKey(_rootPath, writable: true);
        _desktop = Path.Combine(_temp, "Desktop");
        _statePath = Path.Combine(_temp, "state", ClassSaveLocationsFile.FileName);
        Directory.CreateDirectory(_desktop);
        Directory.CreateDirectory(Folder("7A"));
        _software.CreateSubKey(ClassSaveTargets.OfficeRoot).Dispose();
        using (var key = _software.CreateSubKey(ClassSaveTargets.DownloadsKey))
        {
            key.SetValue(ClassSaveTargets.DownloadsName, Original, RegistryValueKind.ExpandString);
        }
        _shell = new FakeShell(_software);
        _lesson = Lesson(1, "7A");
    }

    public void Dispose()
    {
        foreach (var link in _links)
        {
            try { Directory.Delete(link); } catch { }
        }
        _software.Dispose();
        try { Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false); } catch { }
        try { Directory.Delete(_temp, recursive: true); } catch { }
    }

    private static ClassLesson Lesson(long savedAt, string turma) =>
        new(savedAt, turma == "8B" ? Lesson2 : Lesson1, turma, 1, Ended: false);

    private string Folder(string turma) => Path.Combine(_desktop, $"Pasta da turma – {turma}");

    private ClassSaveLocations New() => new(_desktop, () => _lesson, _statePath, _software, _shell, _log.Add);

    private string? Value(string key, string name)
    {
        using var k = _software.OpenSubKey(key);
        return k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    private byte[]? Bytes(string name)
    {
        using var k = _software.OpenSubKey(ClassSaveTargets.MruKey);
        return k?.GetValue(name) as byte[];
    }

    private string? Downloads() => Value(ClassSaveTargets.DownloadsKey, ClassSaveTargets.DownloadsName);

    private int? PreferCloud()
    {
        using var k = _software.OpenSubKey(ClassSaveTargets.PreferCloudKey);
        return k?.GetValue(ClassSaveTargets.PreferCloudName) as int?;
    }

    private string? Office(int index, string name) => Value(ClassSaveTargets.OfficeFolders[index].Key, name);

    private static byte[] IdList(string folder) =>
        Encoding.Unicode.GetBytes(folder).Concat(new byte[] { 0, 0 }).ToArray();

    private void AssertAllPointAt(string folder)
    {
        Assert.Equal(folder, Downloads());
        Assert.Equal(folder, Office(0, "DOC-PATH"));
        Assert.Equal(folder, Office(1, "DefaultPath"));
        Assert.Equal(folder, Office(2, "Default"));
        Assert.Equal(0, PreferCloud());
        Assert.Equal(LastVisitedMru.Entry("notepad.exe", IdList(folder)), Bytes("0"));
        Assert.Equal(LastVisitedMru.Entry("mspaint.exe", IdList(folder)), Bytes("1"));
    }

    private int[]? Order() =>
        Bytes(LastVisitedMru.OrderName) is { } data ? LastVisitedMru.ParseOrder(data)?.ToArray() : null;

    private void StudentSetsDownloads(string path)
    {
        using var key = _software.OpenSubKey(ClassSaveTargets.DownloadsKey, writable: true)!;
        key.SetValue(ClassSaveTargets.DownloadsName, path, RegistryValueKind.ExpandString);
    }

    [Fact]
    public void LessonOn_PointsEverythingAtTheClassFolder()
    {
        New().Follow();

        AssertAllPointAt(Folder("7A"));
        Assert.Equal(new[] { 1, 0 }, Order());
        var state = ClassSaveLocationsFile.Read(_statePath, out _);
        Assert.Equal(Lesson1, state.LessonId);
        Assert.Equal(7, state.Settings.Count);
    }

    [Fact]
    public void LessonEnded_PutsEverythingBack()
    {
        var follower = New();
        follower.Follow();
        _lesson = ClassLesson.None(2);

        follower.Follow();

        Assert.Equal(Original, Downloads());
        Assert.Equal(Environment.ExpandEnvironmentVariables(Original), _shell.DownloadsCalls.Last());
        Assert.Null(Office(0, "DOC-PATH"));
        Assert.Null(Office(1, "DefaultPath"));
        Assert.Null(Office(2, "Default"));
        Assert.Null(PreferCloud());
        Assert.Null(Bytes("0"));
        Assert.Null(Bytes("1"));
        var state = ClassSaveLocationsFile.Read(_statePath, out _);
        Assert.Equal(Guid.Empty, state.LessonId);
        Assert.Empty(state.Settings);
    }

    [Fact]
    public void FolderNotThereYet_DoesNothing_ThenAppliesWhenItIs()
    {
        _lesson = Lesson(1, "9C");
        var follower = New();

        follower.Follow();
        Assert.Equal(Original, Downloads());
        Assert.False(File.Exists(_statePath));

        Directory.CreateDirectory(Folder("9C"));
        follower.Follow();
        Assert.Equal(Folder("9C"), Downloads());
    }

    [Fact]
    public void FolderIsALink_DoesNothing()
    {
        var target = Path.Combine(_temp, "elsewhere");
        Directory.CreateDirectory(target);
        var link = Folder("LK");
        using (var run = Process.Start(new ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!)
        {
            run.WaitForExit();
        }
        Assert.True(Directory.Exists(link));
        _links.Add(link);
        _lesson = Lesson(1, "LK");

        New().Follow();

        Assert.Equal(Original, Downloads());
        Assert.Empty(_shell.DownloadsCalls);
        Assert.False(File.Exists(_statePath));
    }

    [Fact]
    public void UnknownLesson_DoesNothing()
    {
        var follower = New();
        follower.Follow();
        var calls = _shell.DownloadsCalls.Count;

        _lesson = null;
        follower.Follow();

        Assert.Equal(Folder("7A"), Downloads());
        Assert.Equal(calls, _shell.DownloadsCalls.Count);
        Assert.Equal(Lesson1, ClassSaveLocationsFile.Read(_statePath, out _).LessonId);
    }

    [Fact]
    public void AppliedOncePerLesson()
    {
        var follower = New();
        follower.Follow();
        StudentSetsDownloads(@"D:\Meus downloads");

        follower.Follow();
        follower.Follow();
        Assert.Equal(@"D:\Meus downloads", Downloads());

        _lesson = ClassLesson.None(2);
        follower.Follow();
        Assert.Equal(@"D:\Meus downloads", Downloads());
    }

    [Fact]
    public void NewInstance_SameLesson_DoesNotApplyAgain()
    {
        New().Follow();
        var calls = _shell.DownloadsCalls.Count;
        StudentSetsDownloads(@"D:\Meus downloads");

        New().Follow();

        Assert.Equal(calls, _shell.DownloadsCalls.Count);
        Assert.Equal(@"D:\Meus downloads", Downloads());
    }

    [Fact]
    public void NewInstance_LessonEnded_StillUndoes()
    {
        New().Follow();
        _lesson = ClassLesson.None(2);

        New().Follow();

        Assert.Equal(Original, Downloads());
        Assert.Null(Bytes("0"));
        Assert.Equal(Guid.Empty, ClassSaveLocationsFile.Read(_statePath, out _).LessonId);
    }

    [Fact]
    public void OtherTurma_RePointsWithTheOriginalBefore()
    {
        Directory.CreateDirectory(Folder("8B"));
        var follower = New();
        follower.Follow();

        _lesson = Lesson(3, "8B");
        follower.Follow();

        AssertAllPointAt(Folder("8B"));
        Assert.Equal(Lesson2, ClassSaveLocationsFile.Read(_statePath, out _).LessonId);

        _lesson = ClassLesson.None(4);
        follower.Follow();
        Assert.Equal(Original, Downloads());
        Assert.Null(Bytes("0"));
        Assert.Null(Office(0, "DOC-PATH"));
    }

    [Fact]
    public void NoOffice_SkipsOffice()
    {
        _software.DeleteSubKeyTree(ClassSaveTargets.OfficeRoot);

        New().Follow();

        Assert.Equal(Folder("7A"), Downloads());
        Assert.Null(_software.OpenSubKey(ClassSaveTargets.OfficeRoot));
        Assert.Equal(3, ClassSaveLocationsFile.Read(_statePath, out _).Settings.Count);
    }

    [Fact]
    public void MalformedMruOrder_SkipsNotepadAndPaint_AppliesTheRest()
    {
        var bad = new byte[] { 1, 2, 3 };
        using (var key = _software.CreateSubKey(ClassSaveTargets.MruKey))
        {
            key.SetValue(LastVisitedMru.OrderName, bad, RegistryValueKind.Binary);
        }

        New().Follow();

        Assert.Equal(Folder("7A"), Downloads());
        Assert.Equal(Folder("7A"), Office(0, "DOC-PATH"));
        Assert.Null(Bytes("0"));
        Assert.Null(Bytes("1"));
        Assert.Equal(bad, Bytes(LastVisitedMru.OrderName));
        Assert.Equal(5, ClassSaveLocationsFile.Read(_statePath, out _).Settings.Count);
    }

    [Fact]
    public void ExistingNotepadEntry_KeepsItsNumber_AndPutsItBack()
    {
        var old = LastVisitedMru.Entry("notepad.exe", new byte[] { 9, 9, 0, 0 });
        using (var key = _software.CreateSubKey(ClassSaveTargets.MruKey))
        {
            key.SetValue("3", old, RegistryValueKind.Binary);
            key.SetValue(LastVisitedMru.OrderName, LastVisitedMru.FormatOrder(new[] { 3 }), RegistryValueKind.Binary);
        }
        var follower = New();

        follower.Follow();

        Assert.Equal(LastVisitedMru.Entry("notepad.exe", IdList(Folder("7A"))), Bytes("3"));
        Assert.Equal(LastVisitedMru.Entry("mspaint.exe", IdList(Folder("7A"))), Bytes("0"));
        Assert.Equal(new[] { 0, 3 }, Order());

        _lesson = ClassLesson.None(2);
        follower.Follow();

        Assert.Equal(old, Bytes("3"));
        Assert.Null(Bytes("0"));
        Assert.Equal(new[] { 3 }, Order());
    }

    private void StudentSavesInNotepad(string name, byte[] idList)
    {
        using var key = _software.OpenSubKey(ClassSaveTargets.MruKey, writable: true)!;
        key.SetValue(name, LastVisitedMru.Entry("notepad.exe", idList), RegistryValueKind.Binary);
    }

    // Saving once rewrites the program's entry (a real ID list carries timestamps),
    // but while it still leads to the class folder it is put back at the end.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StudentSavedInNotepad_EntryStillPutBack(bool hadEntry)
    {
        var old = LastVisitedMru.Entry("notepad.exe", new byte[] { 9, 9, 0, 0 });
        if (hadEntry)
        {
            using var key = _software.CreateSubKey(ClassSaveTargets.MruKey);
            key.SetValue("3", old, RegistryValueKind.Binary);
            key.SetValue(LastVisitedMru.OrderName, LastVisitedMru.FormatOrder(new[] { 3 }), RegistryValueKind.Binary);
        }
        var follower = New();
        follower.Follow();
        var notepad = hadEntry ? "3" : "0";
        // Other bytes, the same folder.
        StudentSavesInNotepad(notepad, IdList(Folder("7A").ToUpperInvariant() + @"\"));

        _lesson = ClassLesson.None(2);
        follower.Follow();

        Assert.Equal(hadEntry ? old : null, Bytes(notepad));
        Assert.Equal(hadEntry ? new[] { 3 } : Array.Empty<int>(), Order());
    }

    [Fact]
    public void StudentSavedInNotepadElsewhere_EntryStaysTheirs()
    {
        var follower = New();
        follower.Follow();
        var elsewhere = LastVisitedMru.Entry("notepad.exe", IdList(@"D:\Trabalhos"));
        StudentSavesInNotepad("0", IdList(@"D:\Trabalhos"));

        _lesson = ClassLesson.None(2);
        follower.Follow();

        Assert.Equal(elsewhere, Bytes("0"));
        Assert.Null(Bytes("1"));
        Assert.Equal(new[] { 0 }, Order());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{")]
    public void LostStateMidLesson_DoesNotPutTheClassFolderBack(string? corrupt)
    {
        New().Follow();
        if (corrupt is null)
        {
            File.Delete(_statePath);
        }
        else
        {
            File.WriteAllText(_statePath, corrupt);
        }

        var follower = New();
        follower.Follow();
        _lesson = ClassLesson.None(2);
        follower.Follow();

        Assert.Equal(Original, Downloads());
        Assert.Null(Office(0, "DOC-PATH"));
        Assert.Null(Office(1, "DefaultPath"));
        Assert.Null(Office(2, "Default"));
        Assert.Null(Bytes("0"));
        Assert.Null(Bytes("1"));
        Assert.Equal(0, PreferCloud() ?? 0);
        Assert.DoesNotContain("Pasta da turma", Downloads());
    }

    [Fact]
    public void StateCannotBeSaved_WritesNothing()
    {
        Directory.CreateDirectory(_statePath);

        New().Follow();

        Assert.Equal(Original, Downloads());
        Assert.Empty(_shell.DownloadsCalls);
        Assert.Null(Office(0, "DOC-PATH"));
        Assert.Null(Bytes("0"));
        Assert.Null(PreferCloud());
    }

    // Stores Downloads in the throwaway key, as the real shell would in the student's.
    // Its ID lists are the path in UTF-16 and a zero USHORT, and it decodes its own.
    private sealed class FakeShell(RegistryKey software) : IShellFolders
    {
        public List<string> DownloadsCalls { get; } = new();

        // What is stored for a path, when the shell stores another form of it.
        public Func<string, string> Stores { get; set; } = path => path;

        public Func<string, byte[]?> IdLists { get; set; } = IdList;

        public void SetDownloads(string path)
        {
            DownloadsCalls.Add(path);
            using var key = software.CreateSubKey(ClassSaveTargets.DownloadsKey);
            key.SetValue(ClassSaveTargets.DownloadsName, Stores(path), RegistryValueKind.ExpandString);
        }

        public byte[]? IdListOf(string folder) => IdLists(folder);

        public string? PathOf(byte[] idList) =>
            idList.Length >= 2 && idList.Length % 2 == 0 && idList[^1] == 0 && idList[^2] == 0
                ? Encoding.Unicode.GetString(idList, 0, idList.Length - 2)
                : null;
    }
}
