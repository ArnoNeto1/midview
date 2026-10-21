using Midview.Protocol;
using Midview.Session;
using Xunit;

// SessionServer: the class folder ("Pasta da turma") messages the teacher's Master sends.
public partial class SessionServerTests
{
    [Fact]
    public async Task ListClassFolder_ReturnsTheListing()
    {
        using var rig = new ClassFolderRig();
        WriteFile(rig.LessonFolder, "a.txt", new byte[] { 1, 2, 3 });
        WriteFile(rig.LessonFolder, "site/b.txt", new byte[] { 4 });
        var finished = Guid.NewGuid();
        WriteFile(rig.SetAsideFolder(finished), "velho.txt", new byte[] { 9, 9 });
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);
        using var teacher = harness.ConnectMaster();

        var (current, problem, entries) = await Task.Run(teacher.ListClassFolder).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(rig.LessonId, current);
        Assert.Null(problem);
        Assert.Equal(
            new[] { (rig.LessonId, "a.txt", 3L), (rig.LessonId, "site/b.txt", 1L), (finished, "velho.txt", 2L) },
            entries.Select(entry => (entry.LessonId, entry.Path, entry.Size)));
        Assert.Equal(File.GetLastWriteTimeUtc(rig.PathIn("a.txt")).Ticks, entries[0].LastWriteTicks);
    }

    [Fact]
    public async Task ListClassFolder_MoreFilesThanOnePartHolds_SendsThemAllInParts()
    {
        using var rig = new ClassFolderRig(listingPartSize: 2);
        for (var i = 0; i < 5; i++)
        {
            WriteFile(rig.LessonFolder, $"f{i}.txt", new[] { (byte)i });
        }
        var finished = Guid.NewGuid();
        WriteFile(rig.SetAsideFolder(finished), "velho.txt", new byte[] { 9 });
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);
        using var teacher = harness.ConnectMaster();

        var parts = await Task.Run(teacher.ListClassFolderInParts).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { 2, 2, 2 }, parts.Select(part => part.Entries.Count));
        Assert.Equal(new[] { true, true, false }, parts.Select(part => part.More));
        Assert.All(parts, part => Assert.Equal(rig.LessonId, part.CurrentLessonId));
        Assert.Equal(
            new[] { "f0.txt", "f1.txt", "f2.txt", "f3.txt", "f4.txt", "velho.txt" },
            parts.SelectMany(part => part.Entries).Select(entry => entry.Path));
    }

    [Fact]
    public async Task ListClassFolder_WhenTheLessonsFolderIsntThere_SaysSo()
    {
        using var rig = new ClassFolderRig(lessonFolder: false);
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);
        using var teacher = harness.ConnectMaster();

        var (current, problem, entries) = await Task.Run(teacher.ListClassFolder).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(rig.LessonId, current);
        Assert.Equal("sem pasta", problem);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task PullClassFile_StreamsTheFile()
    {
        using var rig = new ClassFolderRig();
        var content = Enumerable.Range(0, FileTransfer.ChunkSize * 2 + 10).Select(i => (byte)i).ToArray();
        WriteFile(rig.LessonFolder, "site/index.html", content);
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);
        using var teacher = harness.ConnectMaster();

        var (name, received, result) = await Task.Run(() => teacher.PullClassFile(rig.LessonId, "site/index.html"))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("site/index.html", name);
        Assert.Equal(content, received);
        Assert.Equal((true, string.Empty), result);
    }

    [Fact]
    public async Task PullClassFile_AnEmptyFile_SendsTheHeaderAndNoChunk()
    {
        using var rig = new ClassFolderRig();
        WriteFile(rig.LessonFolder, "vazio.txt", Array.Empty<byte>());
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);
        using var teacher = harness.ConnectMaster();

        var (name, received, result) = await Task.Run(() => teacher.PullClassFile(rig.LessonId, "vazio.txt"))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("vazio.txt", name);
        Assert.Empty(received!);
        Assert.Equal((true, string.Empty), result);
    }

    [Fact]
    public async Task PullClassFile_FromAFinishedLesson_ReadsWhatWasSetAside()
    {
        using var rig = new ClassFolderRig();
        var finished = Guid.NewGuid();
        WriteFile(rig.SetAsideFolder(finished), "trabalhos/ana.txt", new byte[] { 5, 6 });
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);
        using var teacher = harness.ConnectMaster();

        var (name, received, result) = await Task.Run(() => teacher.PullClassFile(finished, "trabalhos/ana.txt"))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("trabalhos/ana.txt", name);
        Assert.Equal(new byte[] { 5, 6 }, received);
        Assert.True(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(rig.SetAsideFolder(finished), "trabalhos", "ana.txt"))); // reading takes nothing
    }

    [Fact]
    public async Task PullClassFile_WhatIsntThere_IsRefusedInOneAnswer()
    {
        using var rig = new ClassFolderRig();
        WriteFile(rig.LessonFolder, "a.txt", new byte[] { 1 });
        File.WriteAllText(Path.Combine(rig.Desktop, "segredo.txt"), "fora da pasta");
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);

        foreach (var (lessonId, path) in new[]
                 {
                     (rig.LessonId, "nao-existe.txt"),
                     (rig.LessonId, "../segredo.txt"),
                     (Guid.NewGuid(), "a.txt"),
                 })
        {
            using var teacher = harness.ConnectMaster();
            var (name, received, result) = await Task.Run(() => teacher.PullClassFile(lessonId, path))
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Null(name);
            Assert.Null(received);
            Assert.Equal((false, "arquivo não encontrado"), result);
        }
    }

    // Word keeps a document open for writing, and lets others read it.
    [Fact]
    public async Task PullClassFile_AFileAProgramHasOpen_IsStillRead()
    {
        using var rig = new ClassFolderRig();
        var open = WriteAndReturn(rig, "aberto.docx", new byte[] { 1, 2 });
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);
        using var teacher = harness.ConnectMaster();

        using (new FileStream(open, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            var (_, content, result) = await Task.Run(() => teacher.PullClassFile(rig.LessonId, "aberto.docx"))
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(new byte[] { 1, 2 }, content);
            Assert.Equal((true, string.Empty), result);
        }
    }

    [Fact]
    public async Task PullClassFile_AFileLockedAgainstReading_IsRefusedInOneAnswer()
    {
        using var rig = new ClassFolderRig();
        var locked = WriteAndReturn(rig, "trancado.docx", new byte[] { 3 });
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);
        using var teacher = harness.ConnectMaster();

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var (name, content, result) = await Task.Run(() => teacher.PullClassFile(rig.LessonId, "trancado.docx"))
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Null(name);
            Assert.Null(content);
            Assert.False(result.Succeeded);
            Assert.Contains("em uso", result.Detail);
        }
    }

    // A file that is still being written can't be sent whole: what the Master
    // would get is the start of it, under a size that is no longer the file's.
    // The connection is dropped instead, which the Master discards.
    [Fact]
    public async Task PullClassFile_AFileThatGrowsWhileSent_DropsTheConnection_InsteadOfEndingWell()
    {
        using var rig = new ClassFolderRig();
        var path = WriteAndReturn(rig, "grande.bin", new byte[64 * 1024 * 1024]); // more than the sockets hold
        using var harness = new ServerHarness(new CountingScreenCapture(), classFolder: rig.Folder);
        using var teacher = harness.ConnectMaster();
        teacher.Channel.Send(new Message(MessageType.PullClassFile, ClassFolderWire.EncodeFileRef(rig.LessonId, "grande.bin")));
        var header = await Task.Run(() => teacher.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(MessageType.SendFile, header.Type);

        using (var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            writer.WriteByte(1);
        }

        var answered = await Task.Run(() =>
        {
            try
            {
                while (true)
                {
                    if (teacher.Channel.Receive().Type == MessageType.CommandAck)
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                return false; // the connection ended without an answer
            }
        }).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(answered);
    }

    private static (long Size, long LastWriteTicks) VersionOf(string path)
    {
        var file = new FileInfo(path);
        return (file.Length, file.LastWriteTimeUtc.Ticks);
    }

    private static string WriteAndReturn(ClassFolderRig rig, string relativePath, byte[] content)
    {
        WriteFile(rig.LessonFolder, relativePath, content);
        return rig.PathIn(relativePath);
    }

    // The class folder as the student's Session sees it: a Desktop with the
    // folder of the lesson that is on, and where finished lessons' files wait.
    private sealed class ClassFolderRig : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"midview-classserver-{Guid.NewGuid():N}");

        public ClassFolderRig(bool lessonFolder = true, int listingPartSize = ClassFolderWire.MaxEntries)
        {
            Lesson = new ClassLesson(1, Guid.NewGuid(), "3º A", 1, false);
            Desktop = Path.Combine(_root, "Desktop");
            SetAside = Path.Combine(_root, "guardada");
            Bin = new FakeRecycleBin(Path.Combine(_root, "lixeira"));
            Directory.CreateDirectory(Desktop);
            if (lessonFolder)
            {
                Directory.CreateDirectory(LessonFolder);
            }
            Folder = new ClassFolder(
                Desktop, SetAside, () => Lesson, Bin, Path.Combine(_root, "session.json"), listingPartSize: listingPartSize);
        }

        public ClassLesson Lesson { get; }

        public Guid LessonId => Lesson.LessonId;

        public string Desktop { get; }

        public string SetAside { get; }

        public FakeRecycleBin Bin { get; }

        public ClassFolder Folder { get; }

        public string LessonFolder => Path.Combine(Desktop, Lesson.FolderName);

        public string SetAsideFolder(Guid lessonId) => Path.Combine(SetAside, lessonId.ToString("N"));

        public string PathIn(string relativePath) => Path.Combine(LessonFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
