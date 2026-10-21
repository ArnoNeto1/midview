using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Agent;
using Midview.Master;
using Midview.Protocol;
using Midview.Session;
using Xunit;

// Drives ClassFolderClient against a real SessionServer with a temp
// ClassFolder and a real AgentServer with a ClassLessonHandler (a lesson with
// no logged-on student to prepare a folder for). The scripted Sessions, one
// from before the feature and one a student rewrote, are in
// ClassFolderClientScriptedTests. Never a lab machine, the real Desktop or the
// real registry.
public sealed class ClassFolderClientTests : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    private readonly X509Certificate2 _masterCert = SelfSignedCertificate.Create("master-under-test");
    private readonly List<Action> _cleanup = new();

    public void Dispose()
    {
        foreach (var step in _cleanup)
        {
            try { step(); } catch { }
        }
        _masterCert.Dispose();
    }

    private static Task<T> Run<T>(Func<T> call) => Task.Run(call).WaitAsync(Limit);

    // ---- the Agent: the lesson ----

    [Fact]
    public async Task SetLesson_ThenAgentHasIt()
    {
        var (machine, port, handler) = StartAgent(withHandler: true);
        var lesson = new ClassLesson(100, Guid.NewGuid(), "3º A", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Ended: false);

        var (succeeded, detail) = await Run(() => ClassFolderClient.SetLesson(machine, _masterCert, lesson, port));

        Assert.True(succeeded);
        Assert.Equal("aula do 3º A", detail);
        Assert.Equal(lesson, handler!.Current);
    }

    [Fact]
    public async Task SetLesson_ANewerOneOnTheAgent_IsKept_AndSaysSo()
    {
        var (machine, port, handler) = StartAgent(withHandler: true);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var newer = new ClassLesson(200, Guid.NewGuid(), "3º B", now, Ended: false);
        await Run(() => ClassFolderClient.SetLesson(machine, _masterCert, newer, port));

        var (succeeded, detail) = await Run(() => ClassFolderClient.SetLesson(
            machine, _masterCert, new ClassLesson(100, Guid.NewGuid(), "3º A", now, Ended: false), port));

        Assert.True(succeeded);
        Assert.Contains("mantida a aula mais nova", detail);
        Assert.Equal(newer, handler!.Current);
    }

    [Fact]
    public async Task SetLesson_OldAgent_SaysMidviewAntigo()
    {
        var (machine, port, _) = StartAgent(withHandler: false);
        var lesson = new ClassLesson(100, Guid.NewGuid(), "3º A", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Ended: false);

        var (succeeded, detail) = await Run(() => ClassFolderClient.SetLesson(machine, _masterCert, lesson, port));

        Assert.False(succeeded);
        Assert.StartsWith("Midview antigo", detail);
    }

    [Fact]
    public void SetLesson_WithoutTheAgentsIdentity_Throws()
    {
        var machine = new KnownMachine("Aluno-1", "127.0.0.1", "ANY", null);
        var lesson = ClassLesson.None(1);

        Assert.Throws<InvalidOperationException>(() => ClassFolderClient.SetLesson(machine, _masterCert, lesson, 1));
    }

    // ---- the Session: list, pull, push, delete, rename, clear ----

    [Fact]
    public async Task List_Pull_Push_Delete_Rename_ClearSetAside_RoundTrip()
    {
        using var rig = new ClassRig(_masterCert);
        var big = Enumerable.Range(0, FileTransfer.ChunkSize * 2 + 5).Select(i => (byte)(i * 7)).ToArray();
        rig.WriteLessonFile("a.txt", new byte[] { 1, 2, 3 });
        rig.WriteLessonFile("sub/grande.bin", big);
        rig.WriteLessonFile("vazio.txt", Array.Empty<byte>());
        var finished = Guid.NewGuid();
        rig.WriteSetAsideFile(finished, "velho.docx", new byte[] { 9, 9 });

        // List: the current lesson's files, then the set-aside ones.
        var listing = await Run(() => ClassFolderClient.List(rig.Machine, _masterCert, rig.Port));
        Assert.False(listing.OldSession);
        Assert.Null(listing.Problem);
        Assert.Equal(rig.LessonId, listing.CurrentLessonId);
        Assert.Equal(
            new[] { "atual|a.txt|3", $"atual|sub/grande.bin|{big.Length}", "atual|vazio.txt|0", "guardada|velho.docx|2" }.Order(),
            listing.Entries.Select(e => $"{(e.LessonId == rig.LessonId ? "atual" : "guardada")}|{e.Path}|{e.Size}").Order());
        Assert.Equal(finished, listing.Entries.Single(e => e.Path == "velho.docx").LessonId);
        Assert.Equal(
            File.GetLastWriteTimeUtc(rig.LessonPath("a.txt")).Ticks, listing.Entries.Single(e => e.Path == "a.txt").LastWriteTicks);

        // Pull: the same bytes, a few chunks of them, an empty file, and a set-aside one.
        Assert.Equal(big, await PullAll(rig, rig.LessonId, "sub/grande.bin"));
        Assert.Equal(new byte[] { 1, 2, 3 }, await PullAll(rig, rig.LessonId, "a.txt"));
        Assert.Empty(await PullAll(rig, rig.LessonId, "vazio.txt"));
        Assert.Equal(new byte[] { 9, 9 }, await PullAll(rig, finished, "velho.docx"));

        // Push: into a subfolder that doesn't exist yet, with the teacher's time.
        var source = Path.Combine(rig.Root, "origem.bin");
        File.WriteAllBytes(source, big);
        var time = new DateTime(2026, 3, 4, 10, 20, 30, DateTimeKind.Utc).Ticks;
        var pushed = await Run(() => ClassFolderClient.Push(
            rig.Machine, _masterCert, rig.LessonId, "novas/copia.bin", source, time, rig.Port));
        Assert.Equal((true, string.Empty), pushed);
        Assert.Equal(big, File.ReadAllBytes(rig.LessonPath("novas/copia.bin")));
        Assert.Equal(time, File.GetLastWriteTimeUtc(rig.LessonPath("novas/copia.bin")).Ticks);
        Assert.DoesNotContain(Directory.GetFiles(rig.LessonFolder, "*", SearchOption.AllDirectories), f => f.Contains(".midview-"));

        // Rename, then Delete (to the Recycle Bin).
        var renamed = await Run(() => ClassFolderClient.Rename(rig.Machine, _masterCert, rig.LessonId, "a.txt", "b.txt", rig.Port));
        Assert.Equal((true, string.Empty), renamed);
        Assert.False(File.Exists(rig.LessonPath("a.txt")));
        Assert.True(File.Exists(rig.LessonPath("b.txt")));
        var deleted = await Run(() => ClassFolderClient.Delete(rig.Machine, _masterCert, rig.LessonId, "b.txt", rig.Port));
        Assert.Equal((true, string.Empty), deleted);
        Assert.False(File.Exists(rig.LessonPath("b.txt")));
        Assert.Single(Directory.GetFileSystemEntries(rig.BinFolder));

        // ClearSetAside: the finished lesson's files are gone from the next listing.
        var cleared = await Run(() => ClassFolderClient.ClearSetAside(rig.Machine, _masterCert, finished, rig.Port));
        Assert.Equal((true, string.Empty), cleared);
        var after = await Run(() => ClassFolderClient.List(rig.Machine, _masterCert, rig.Port));
        Assert.DoesNotContain(after.Entries, e => e.LessonId == finished);
        Assert.Contains(after.Entries, e => e.Path == "novas/copia.bin");
    }

    [Fact]
    public async Task Refusals_AreReturnedWithTheirReason_AndNothingChanges()
    {
        using var rig = new ClassRig(_masterCert);
        rig.WriteLessonFile("a.txt", new byte[] { 1 });
        rig.WriteLessonFile("b.txt", new byte[] { 2 });
        var source = Path.Combine(rig.Root, "origem.txt");
        File.WriteAllBytes(source, new byte[] { 7 });
        var other = Guid.NewGuid();

        // Only the lesson that is on takes files.
        var push = await Run(() => ClassFolderClient.Push(rig.Machine, _masterCert, other, "x.txt", source, DateTime.UtcNow.Ticks, rig.Port));
        Assert.Equal((false, "aula diferente"), push);
        Assert.False(File.Exists(rig.LessonPath("x.txt")));

        var missing = new MemoryStream();
        var pull = await Run(() => ClassFolderClient.Pull(rig.Machine, _masterCert, rig.LessonId, "nao-existe.txt", missing, rig.Port));
        Assert.Equal((false, "arquivo não encontrado"), pull);
        Assert.Equal(0, missing.Length);

        var rename = await Run(() => ClassFolderClient.Rename(rig.Machine, _masterCert, rig.LessonId, "a.txt", "b.txt", rig.Port));
        Assert.Equal((false, "já existe um arquivo com esse nome"), rename);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(rig.LessonPath("a.txt")));

        var delete = await Run(() => ClassFolderClient.Delete(rig.Machine, _masterCert, rig.LessonId, "nao-existe.txt", rig.Port));
        Assert.Equal((false, "arquivo não encontrado"), delete);
    }

    [Fact]
    public async Task Push_WhenTheFileCantBeRead_FailsWithoutConnecting()
    {
        // Nothing listens on port 1: reaching the network would throw instead.
        var machine = new KnownMachine("Aluno-1", "127.0.0.1", "ANY", null);
        var missing = Path.Combine(Path.GetTempPath(), $"midview-nada-{Guid.NewGuid():N}.txt");

        var (succeeded, detail) = await Run(() => ClassFolderClient.Push(
            machine, _masterCert, Guid.NewGuid(), "a.txt", missing, DateTime.UtcNow.Ticks, 1));

        Assert.False(succeeded);
        Assert.NotEmpty(detail);
    }

    [Fact]
    public async Task APathTheWireCantCarry_FailsWithoutConnecting()
    {
        var machine = new KnownMachine("Aluno-1", "127.0.0.1", "ANY", null);
        var tooLong = new string('a', FileTransfer.MaxNameBytes) + ".txt";

        var rename = await Run(() => ClassFolderClient.Rename(machine, _masterCert, Guid.NewGuid(), "a.txt", tooLong, 1));
        var delete = await Run(() => ClassFolderClient.Delete(machine, _masterCert, Guid.NewGuid(), "", 1));
        var pull = await Run(() => ClassFolderClient.Pull(machine, _masterCert, Guid.NewGuid(), tooLong, new MemoryStream(), 1));

        Assert.False(rename.Succeeded);
        Assert.False(delete.Succeeded);
        Assert.False(pull.Succeeded);
    }

    [Fact]
    public async Task ASessionWithoutAClassFolder_RefusesEach_AndTheListingSaysWhy()
    {
        // The real server, without a class folder, answers one refusal to everything.
        using var rig = new ClassRig(_masterCert, withClassFolder: false);

        var listing = await Run(() => ClassFolderClient.List(rig.Machine, _masterCert, rig.Port));
        var delete = await Run(() => ClassFolderClient.Delete(rig.Machine, _masterCert, Guid.NewGuid(), "a.txt", rig.Port));

        Assert.False(listing.OldSession);
        Assert.Equal("esta máquina não tem pasta da turma", listing.Problem);
        Assert.Empty(listing.Entries);
        Assert.Equal(Guid.Empty, listing.CurrentLessonId);
        Assert.Equal((false, "esta máquina não tem pasta da turma"), delete);
    }

    // No limit on the files: a folder bigger than one listing message holds arrives whole, in parts.
    [Fact]
    public async Task List_MoreFilesThanOneListingHolds_ArriveWhole()
    {
        using var rig = new ClassRig(_masterCert, listingPartSize: 3);
        for (var i = 0; i < 7; i++)
        {
            rig.WriteLessonFile($"f{i}.txt", new[] { (byte)i });
        }
        var finished = Guid.NewGuid();
        rig.WriteSetAsideFile(finished, "velho.docx", new byte[] { 9, 9 });

        var listing = await Run(() => ClassFolderClient.List(rig.Machine, _masterCert, rig.Port));

        Assert.Null(listing.Problem);
        Assert.Equal(rig.LessonId, listing.CurrentLessonId);
        Assert.Equal(
            Enumerable.Range(0, 7).Select(i => $"f{i}.txt").Append("velho.docx"),
            listing.Entries.Select(entry => entry.Path));
        Assert.Equal(finished, listing.Entries[^1].LessonId);
    }

    [Fact]
    public async Task List_AListingWithAProblem_IsReturnedAsIs()
    {
        using var rig = new ClassRig(_masterCert, lessonFolder: false);

        var listing = await Run(() => ClassFolderClient.List(rig.Machine, _masterCert, rig.Port));

        Assert.Equal("sem pasta", listing.Problem);
        Assert.Equal(rig.LessonId, listing.CurrentLessonId);
        Assert.Empty(listing.Entries);
    }

    // ---- scaffolding ----

    private static async Task<byte[]> PullAll(ClassRig rig, Guid lessonId, string path)
    {
        var destination = new MemoryStream();
        var result = await Run(() => ClassFolderClient.Pull(rig.Machine, rig.Master, lessonId, path, destination, rig.Port));
        Assert.Equal((true, string.Empty), result);
        return destination.ToArray();
    }

    private (KnownMachine Machine, int Port, ClassLessonHandler? Handler) StartAgent(bool withHandler)
    {
        ClassLessonHandler? handler = null;
        if (withHandler)
        {
            var statePath = Path.Combine(Path.GetTempPath(), $"midview-lesson-{Guid.NewGuid():N}.json");
            // No student logged on: the handler keeps the lesson and touches no folder or registry.
            handler = new ClassLessonHandler(
                statePath, () => null, _ => null, new AppBlockEnforcer(new EmptyInspector()),
                System.Security.Principal.WindowsIdentity.GetCurrent().User!, () => DateTime.Now);
            _cleanup.Add(() => File.Delete(statePath));
        }

        var agentCert = SelfSignedCertificate.Create("agent-under-test");
        _cleanup.Add(agentCert.Dispose);
        var siteBlock = new SiteBlockCommandHandler(new SiteBlockState(), "unused", "unused-hosts", new FakeFirewall());
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new AgentServer(
            listener, agentCert, new TrustStore(new[] { _masterCert.Thumbprint }), siteBlock, classLesson: handler);
        var stop = new CancellationTokenSource();
        var served = Task.Run(() => server.Run(stop.Token));
        _cleanup.Add(() => { stop.Cancel(); try { served.Wait(TimeSpan.FromSeconds(5)); } catch { } });

        return (new KnownMachine("Aluno-Teste", "127.0.0.1", "unused-session-thumb", agentCert.Thumbprint), port, handler);
    }

    // A real SessionServer (a loopback one) over a temp Desktop with the
    // folder of a lesson that is on, and a place for finished lessons' files.
    private sealed class ClassRig : IDisposable
    {
        private readonly X509Certificate2 _sessionCert = SelfSignedCertificate.Create("session-under-test");
        private readonly ThumbnailPollerTests.LoopbackSession _session;
        private readonly ClassLesson _lesson = new(1, Guid.NewGuid(), "3º A", 1, false);

        public ClassRig(
            X509Certificate2 master, bool lessonFolder = true, bool withClassFolder = true,
            int listingPartSize = ClassFolderWire.MaxEntries)
        {
            Master = master;
            Root = Path.Combine(Path.GetTempPath(), $"midview-classclient-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(Root, "Desktop"));
            BinFolder = Path.Combine(Root, "lixeira");
            if (lessonFolder)
            {
                Directory.CreateDirectory(LessonFolder);
            }

            var folder = withClassFolder
                ? new ClassFolder(
                    Path.Combine(Root, "Desktop"), Path.Combine(Root, "guardada"), () => _lesson,
                    new MovingBin(BinFolder), Path.Combine(Root, "session.json"), listingPartSize)
                : null;
            _session = new ThumbnailPollerTests.LoopbackSession(0, _sessionCert, master, classFolder: folder);
        }

        public X509Certificate2 Master { get; }

        public string Root { get; }

        public string BinFolder { get; }

        public Guid LessonId => _lesson.LessonId;

        public int Port => _session.Port;

        public KnownMachine Machine => new("Aluno-1", "127.0.0.1", _sessionCert.Thumbprint, null);

        public string LessonFolder => Path.Combine(Root, "Desktop", _lesson.FolderName);

        public string LessonPath(string relativePath) => Path.Combine(LessonFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));

        public void WriteLessonFile(string relativePath, byte[] content) => Write(LessonPath(relativePath), content);

        public void WriteSetAsideFile(Guid lessonId, string relativePath, byte[] content) =>
            Write(Path.Combine(Root, "guardada", lessonId.ToString("N"), relativePath.Replace('/', Path.DirectorySeparatorChar)), content);

        private static void Write(string path, byte[] content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
        }

        public void Dispose()
        {
            _session.Dispose();
            _sessionCert.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }

    // The Recycle Bin of a test: a folder.
    private sealed class MovingBin : IRecycleBin
    {
        private readonly string _folder;

        public MovingBin(string folder)
        {
            _folder = folder;
            Directory.CreateDirectory(folder);
        }

        public void Recycle(string path)
        {
            var target = Path.Combine(_folder, $"{Guid.NewGuid():N}-{Path.GetFileName(path)}");
            if (Directory.Exists(path))
            {
                Directory.Move(path, target);
            }
            else
            {
                File.Move(path, target);
            }
        }
    }

    private sealed class EmptyInspector : IProcessInspector
    {
        public IReadOnlyList<RunningProcess> ListInteractive() => Array.Empty<RunningProcess>();
        public ProcessFacts? Inspect(RunningProcess process) => null;
        public bool End(RunningProcess process) => true;
        public void Notify(int sessionId, string message) { }
    }

    private sealed class FakeFirewall : IFirewallController
    {
        public bool ApplyBlock() => true;
        public void ApplyUnblock() { }
    }
}
