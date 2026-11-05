using System.Diagnostics;
using Microsoft.Win32;
using Midview.Protocol;
using Xunit;

public sealed class ClassSaveLocationsFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "midview-csl-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_root, ClassSaveLocationsFile.FileName);

    public ClassSaveLocationsFileTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        // A junction is removed on its own first, so nothing behind it is touched.
        foreach (var directory in Directory.GetDirectories(_root))
        {
            if (ClassFolderRules.IsLink(directory))
                Directory.Delete(directory);
        }
        Directory.Delete(_root, recursive: true);
    }

    private static SavedSetting Downloads(string written = @"C:\Turma") =>
        new(ClassSaveTargets.DownloadsKey, ClassSaveTargets.DownloadsName,
            RegistryValueKind.ExpandString, @"%USERPROFILE%\Downloads", written);

    private static SavedSetting Policies() =>
        new(@"Policies\Microsoft\Windows\System", "Anything", RegistryValueKind.String, null, "x");

    private static ClassSaveLocationsState State(params SavedSetting[] settings) =>
        new(Guid.NewGuid(), @"C:\Turma", settings);

    [Fact]
    public void Read_Missing_IsNoneWithoutProblem()
    {
        var state = ClassSaveLocationsFile.Read(FilePath, out var problem);

        Assert.Same(ClassSaveLocationsState.None, state);
        Assert.Null(problem);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData("{\"LessonId\":\"not a guid\",\"Folder\":\"x\",\"Settings\":[]}")]
    [InlineData("{\"LessonId\":\"" + "00000000-0000-0000-0000-000000000001" + "\",\"Settings\":[{\"Kind\":\"nope\"}]}")]
    public void Read_Malformed_IsNoneWithProblem(string json)
    {
        File.WriteAllText(FilePath, json);

        var state = ClassSaveLocationsFile.Read(FilePath, out var problem);

        Assert.Same(ClassSaveLocationsState.None, state);
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Fact]
    public void Read_TooBig_IsNoneWithProblem()
    {
        File.WriteAllBytes(FilePath, new byte[ClassSaveLocationsFile.MaxBytes + 1]);

        var state = ClassSaveLocationsFile.Read(FilePath, out var problem);

        Assert.Same(ClassSaveLocationsState.None, state);
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Fact]
    public void Read_Unreadable_IsNoneWithProblem()
    {
        // A folder where the file should be cannot be read as one.
        Directory.CreateDirectory(FilePath);

        var state = ClassSaveLocationsFile.Read(FilePath, out var problem);

        Assert.Same(ClassSaveLocationsState.None, state);
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Fact]
    public void Read_DropsDisallowed()
    {
        var lesson = Guid.NewGuid();
        ClassSaveLocationsFile.Save(FilePath, new ClassSaveLocationsState(lesson, @"C:\Turma", new[] { Downloads(), Policies() }));

        var state = ClassSaveLocationsFile.Read(FilePath, out var problem);

        Assert.Null(problem);
        Assert.Equal(lesson, state.LessonId);
        Assert.Equal(Downloads(), Assert.Single(state.Settings));
    }

    [Fact]
    public void Read_HandCraftedJsonNeverThrows()
    {
        // Missing Settings, null entries, null strings and an unknown Kind number.
        var json = "{\"LessonId\":\"00000000-0000-0000-0000-000000000001\",\"Folder\":null,\"Settings\":["
            + "null,{\"Key\":null,\"Name\":null,\"Kind\":1,\"Before\":null,\"Written\":null},"
            + "{\"Key\":\"" + ClassSaveTargets.DownloadsKey.Replace("\\", "\\\\") + "\",\"Name\":\"" + ClassSaveTargets.DownloadsName
            + "\",\"Kind\":4242,\"Before\":null,\"Written\":\"x\"}]}";
        File.WriteAllText(FilePath, json);

        var state = ClassSaveLocationsFile.Read(FilePath, out _);

        Assert.Empty(state.Settings);
        Assert.Equal("", state.Folder);

        File.WriteAllText(FilePath, "{\"LessonId\":\"00000000-0000-0000-0000-000000000001\"}");
        Assert.Empty(ClassSaveLocationsFile.Read(FilePath, out _).Settings);
    }

    [Fact]
    public void Save_ThenRead_RoundTrips()
    {
        var mru = new SavedSetting(ClassSaveTargets.MruKey, "3", RegistryValueKind.Binary, null,
            Convert.ToBase64String(LastVisitedMru.Entry("notepad.exe", new byte[] { 1, 2, 3 })));
        var saved = new ClassSaveLocationsState(Guid.NewGuid(), @"C:\Pasta da turma – 7A", new[] { Downloads(), mru });

        // The folder is created, and a second save replaces the first.
        var path = Path.Combine(_root, "sub", ClassSaveLocationsFile.FileName);
        ClassSaveLocationsFile.Save(path, State(Downloads(@"C:\Outra")));
        ClassSaveLocationsFile.Save(path, saved);

        var read = ClassSaveLocationsFile.Read(path, out var problem);

        Assert.Null(problem);
        Assert.Equal(saved.LessonId, read.LessonId);
        Assert.Equal(saved.Folder, read.Folder);
        Assert.Equal(saved.Settings, read.Settings);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Read_LinkBelowRoot_IsRefused()
    {
        var elsewhere = Path.Combine(_root, "elsewhere");
        var profile = Path.Combine(_root, "profile");
        Directory.CreateDirectory(elsewhere);
        Directory.CreateDirectory(profile);
        ClassSaveLocationsFile.Save(Path.Combine(elsewhere, ClassSaveLocationsFile.FileName), State(Downloads()));
        var link = Path.Combine(profile, "Midview");
        MakeJunction(link, elsewhere);
        try
        {
            var path = Path.Combine(link, ClassSaveLocationsFile.FileName);

            var refused = ClassSaveLocationsFile.Read(path, out var problem, linkFreeBelow: profile);
            Assert.Same(ClassSaveLocationsState.None, refused);
            Assert.False(string.IsNullOrEmpty(problem));

            var allowed = ClassSaveLocationsFile.Read(path, out var noProblem);
            Assert.Null(noProblem);
            Assert.Single(allowed.Settings);
        }
        finally
        {
            // Not recursive: only the link goes.
            Directory.Delete(link);
        }
    }

    [Fact]
    public void Read_NoLinkBelowRoot_IsRead()
    {
        var folder = Path.Combine(_root, "profile", "Midview");
        var path = Path.Combine(folder, ClassSaveLocationsFile.FileName);
        ClassSaveLocationsFile.Save(path, State(Downloads()));

        var state = ClassSaveLocationsFile.Read(path, out var problem, linkFreeBelow: Path.Combine(_root, "profile"));

        Assert.Null(problem);
        Assert.Single(state.Settings);
    }

    private static void MakeJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
