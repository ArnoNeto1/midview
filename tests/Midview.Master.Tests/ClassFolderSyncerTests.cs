using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Midview.Session;
using Xunit;

// The whole "Pasta da turma" over real loopback Sessions: two machines, the
// teacher's copy, and the lesson file.
public class ClassFolderSyncerTests : IDisposable
{
    private static readonly long Monday = new DateTime(2026, 10, 19, 13, 0, 0, DateTimeKind.Utc).Ticks;

    private readonly string _work = Path.Combine(Path.GetTempPath(), $"midview-classsync-{Guid.NewGuid():N}");
    private readonly X509Certificate2 _masterCertificate = SelfSignedCertificate.Create("master-under-test");
    private readonly Dictionary<string, int> _ports = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IDisposable> _sessions = new();

    public ClassFolderSyncerTests()
    {
        Directory.CreateDirectory(_work);
    }

    public void Dispose()
    {
        foreach (var session in _sessions)
        {
            session.Dispose();
        }
        Directory.Delete(_work, recursive: true);
    }

    private string ClassesDirectory => Path.Combine(_work, "Turmas");

    private string LessonPath => Path.Combine(_work, "aula-em-andamento.json");

    private string BinDirectory => Path.Combine(_work, "Lixeira");

    [Fact]
    public async Task TheFirstRound_LoadsTheCopyOntoEveryMachine()
    {
        var syncer = Syncer(out var store);
        WriteIn(store.DirectoryPath, "exercício.docx", new byte[] { 1, 2, 3 }, Monday);
        WriteIn(store.DirectoryPath, "site/index.html", new byte[] { 4 }, Monday + 1);
        var (one, oneFolder) = Machine("Aluno-1");
        var (two, twoFolder) = Machine("Aluno-2");

        var results = await syncer.SyncNowAsync(new[] { one, two });

        Assert.All(results, result => Assert.True(result.Succeeded, result.Detail));
        foreach (var folder in new[] { oneFolder, twoFolder })
        {
            Assert.Equal(
                new[] { new ClassFolderEntry("exercício.docx", 3, Monday), new ClassFolderEntry("site/index.html", 1, Monday + 1) },
                folder.ListFiles().Select(file => file.Entry));
        }
        Assert.Equal(2, syncer.Lesson.Machines[one.SessionThumbprint].Count);
        Assert.True(File.Exists(LessonPath));
    }

    [Fact]
    public async Task WhatAStudentAddsOrChanges_IsSavedToTheCopy()
    {
        var syncer = Syncer(out var store);
        WriteIn(store.DirectoryPath, "notas.txt", new byte[] { 1 }, Monday);
        var (one, folder) = Machine("Aluno-1");
        await syncer.SyncNowAsync(new[] { one });
        WriteIn(folder.DirectoryPath, "notas.txt", new byte[] { 1, 1 }, Monday + 10);
        WriteIn(folder.DirectoryPath, "desenhos/casa.png", new byte[] { 9, 9, 9 }, Monday + 20);

        var results = await syncer.SyncNowAsync(new[] { one });

        Assert.True(results[0].Succeeded, results[0].Detail);
        Assert.Equal(new byte[] { 1, 1 }, File.ReadAllBytes(PathIn(store.DirectoryPath, "notas.txt")));
        Assert.Equal(new DateTime(Monday + 20, DateTimeKind.Utc), File.GetLastWriteTimeUtc(PathIn(store.DirectoryPath, "desenhos/casa.png")));
        Assert.Equal(new[] { "desenhos/casa.png" }, syncer.Lesson.Added);
        Assert.Empty(Directory.GetFiles(store.StagingDirectory));

        // Nothing changed since: nothing is sent again.
        var again = await syncer.SyncNowAsync(new[] { one });
        Assert.Equal("sem mudanças", again[0].Detail);
    }

    [Fact]
    public async Task AFileAStudentDeletes_GoesToTheTeachersRecycleBin()
    {
        var syncer = Syncer(out var store);
        WriteIn(store.DirectoryPath, "pasta/velho.txt", new byte[] { 1 }, Monday);
        var (one, folder) = Machine("Aluno-1");
        await syncer.SyncNowAsync(new[] { one });
        File.Delete(PathIn(folder.DirectoryPath, "pasta/velho.txt"));

        await syncer.SyncNowAsync(new[] { one });

        Assert.False(Directory.Exists(PathIn(store.DirectoryPath, "pasta")));
        Assert.True(File.Exists(Path.Combine(BinDirectory, "velho.txt")));
    }

    [Fact]
    public async Task TwoMachinesChangingTheSameFile_KeepBothVersions()
    {
        var syncer = Syncer(out var store);
        WriteIn(store.DirectoryPath, "notas.txt", new byte[] { 0 }, Monday);
        var (one, oneFolder) = Machine("Aluno-1");
        var (two, twoFolder) = Machine("Aluno-2");
        await syncer.SyncNowAsync(new[] { one, two });
        WriteIn(oneFolder.DirectoryPath, "notas.txt", new byte[] { 1 }, Monday + 1);
        WriteIn(twoFolder.DirectoryPath, "notas.txt", new byte[] { 2, 2 }, Monday + 2);

        await syncer.SyncNowAsync(new[] { one });
        await syncer.SyncNowAsync(new[] { two });

        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(PathIn(store.DirectoryPath, "notas.txt")));
        Assert.Equal(new byte[] { 2, 2 }, File.ReadAllBytes(PathIn(store.DirectoryPath, "notas (Aluno-2).txt")));

        // The second machine changing it again updates its own copy.
        WriteIn(twoFolder.DirectoryPath, "notas.txt", new byte[] { 3, 3, 3 }, Monday + 3);
        await syncer.SyncNowAsync(new[] { two });
        Assert.Equal(new byte[] { 3, 3, 3 }, File.ReadAllBytes(PathIn(store.DirectoryPath, "notas (Aluno-2).txt")));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(PathIn(store.DirectoryPath, "notas.txt")));
    }

    [Fact]
    public async Task AFileDeletedOnOneMachine_StaysWhenAnotherChangedIt()
    {
        var syncer = Syncer(out var store);
        WriteIn(store.DirectoryPath, "notas.txt", new byte[] { 0 }, Monday);
        var (one, oneFolder) = Machine("Aluno-1");
        var (two, twoFolder) = Machine("Aluno-2");
        await syncer.SyncNowAsync(new[] { one, two });
        WriteIn(oneFolder.DirectoryPath, "notas.txt", new byte[] { 1, 1 }, Monday + 1);
        await syncer.SyncNowAsync(new[] { one });
        File.Delete(PathIn(twoFolder.DirectoryPath, "notas.txt"));

        await syncer.SyncNowAsync(new[] { two });

        Assert.Equal(new byte[] { 1, 1 }, File.ReadAllBytes(PathIn(store.DirectoryPath, "notas.txt")));
    }

    [Fact]
    public async Task AMachineThatLostTheLesson_IsLoadedAgain_WithoutTouchingTheCopy()
    {
        var syncer = Syncer(out var store);
        WriteIn(store.DirectoryPath, "notas.txt", new byte[] { 5 }, Monday);
        var (one, folder) = Machine("Aluno-1");
        await syncer.SyncNowAsync(new[] { one });
        folder.Clear(); // as if another teacher's program ended a lesson on it

        var results = await syncer.SyncNowAsync(new[] { one });

        Assert.True(results[0].Succeeded, results[0].Detail);
        Assert.StartsWith("pasta carregada", results[0].Detail);
        Assert.True(File.Exists(PathIn(store.DirectoryPath, "notas.txt")));
        Assert.Equal(new byte[] { 5 }, File.ReadAllBytes(PathIn(folder.DirectoryPath, "notas.txt")));
    }

    [Fact]
    public async Task TheLessonCarriesOn_AfterTheTeachersProgramRestarts()
    {
        var syncer = Syncer(out var store);
        WriteIn(store.DirectoryPath, "notas.txt", new byte[] { 5 }, Monday);
        var (one, folder) = Machine("Aluno-1");
        await syncer.SyncNowAsync(new[] { one });
        WriteIn(folder.DirectoryPath, "novo.txt", new byte[] { 6 }, Monday + 1);

        var lesson = ClassLessonFile.Load(LessonPath)!;
        var restarted = new ClassFolderSyncer(
            lesson, new ClassStore(ClassesDirectory, lesson.ClassName), _masterCertificate, LessonPath,
            new MovingRecycleBin(BinDirectory), () => Array.Empty<KnownMachine>(), port: PortOf);
        var results = await restarted.SyncNowAsync(new[] { one });

        Assert.Equal("1 arquivo(s) salvo(s)", results[0].Detail);
        Assert.True(File.Exists(PathIn(store.DirectoryPath, "novo.txt")));
        Assert.True(File.Exists(PathIn(folder.DirectoryPath, "notas.txt")));
    }

    [Fact]
    public async Task EndingTheLesson_CanTakeOutJustWhatItAdded_AndEmptiesTheMachines()
    {
        var syncer = Syncer(out var store);
        WriteIn(store.DirectoryPath, "antigo.txt", new byte[] { 1 }, Monday);
        var (one, folder) = Machine("Aluno-1");
        await syncer.SyncNowAsync(new[] { one });
        WriteIn(folder.DirectoryPath, "de hoje/foto.jpg", new byte[] { 2 }, Monday + 1);
        WriteIn(folder.DirectoryPath, "antigo.txt", new byte[] { 3 }, Monday + 2);
        await syncer.SyncNowAsync(new[] { one });

        var (recycled, problems) = syncer.RecycleAdded();
        var cleared = await ClassFolderSyncer.ClearAsync(new[] { one }, _masterCertificate, PortOf);
        syncer.Forget();

        Assert.Equal((1, 0), (recycled, problems.Count));
        Assert.Equal(new[] { "antigo.txt" }, store.ListFiles().Select(file => file.Entry.Path));
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(PathIn(store.DirectoryPath, "antigo.txt")));
        Assert.True(cleared[0].Succeeded, cleared[0].Detail);
        Assert.Empty(folder.ListFiles());
        Assert.Null(folder.CurrentLesson());
        Assert.False(File.Exists(LessonPath));
    }

    [Fact]
    public async Task DeletingEverything_EmptiesTheCopy_ButKeepsTheClass()
    {
        var syncer = Syncer(out var store);
        WriteIn(store.DirectoryPath, "a.txt", new byte[] { 1 }, Monday);
        WriteIn(store.DirectoryPath, "fotos/b.jpg", new byte[] { 2 }, Monday);
        var (one, _) = Machine("Aluno-1");
        await syncer.SyncNowAsync(new[] { one });

        var (recycled, problems) = syncer.RecycleAll();

        Assert.Equal((2, 0), (recycled, problems.Count));
        Assert.Empty(store.ListFiles());
        Assert.Equal(new[] { "7º A" }, ClassStore.ListClasses(ClassesDirectory));
        Assert.True(File.Exists(Path.Combine(BinDirectory, "b.jpg")) || Directory.Exists(Path.Combine(BinDirectory, "fotos")));
    }

    [Fact]
    public async Task AColleaguesLesson_IsNotLoadedOverOneStillGoingOn()
    {
        var syncer = Syncer(out var store);
        WriteIn(store.DirectoryPath, "nosso.txt", new byte[] { 1 }, Monday);
        var (one, folder) = Machine("Aluno-1");
        await syncer.SyncNowAsync(new[] { one });
        var colleague = new ClassFolderSyncer(
            ClassLesson.Start("8º B", DateTime.UtcNow), new ClassStore(ClassesDirectory, "8º B"), _masterCertificate,
            Path.Combine(_work, "colega.json"), new MovingRecycleBin(BinDirectory), () => Array.Empty<KnownMachine>(), port: PortOf);

        var results = await colleague.SyncNowAsync(new[] { one });

        Assert.False(results[0].Succeeded);
        Assert.Contains("outra aula", results[0].Detail);
        Assert.Equal(new[] { "nosso.txt" }, folder.ListFiles().Select(file => file.Entry.Path));
        Assert.Equal(syncer.Lesson.Id, folder.CurrentLesson()!.Id);
    }

    [Fact]
    public async Task AHostileMachine_CantWriteOutsideTheCopy_NorSendWhatItDidntList()
    {
        var syncer = Syncer(out var store);
        var (one, _) = Machine("Aluno-1");
        await syncer.SyncNowAsync(new[] { one });
        using var hostile = new HostileSession(_masterCertificate, channel =>
        {
            var listing = new[] { new ClassFolderEntry("ok.txt", 1, Monday) };
            channel.Send(new Message(MessageType.ClassFolderListing, ClassFolderSync.EncodeListing(listing)));
            channel.Send(new Message(MessageType.SendFile, FileTransfer.EncodeHeader("../../fora.txt", 1)));
            channel.Send(new Message(MessageType.FileChunk, new byte[] { 9 }));
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(true, string.Empty)));
        });
        var impostor = one with { SessionThumbprint = hostile.Thumbprint };
        _ports[impostor.SessionThumbprint] = hostile.Port;
        syncer.Lesson.Machines[impostor.SessionThumbprint] = new List<ClassFolderEntry>();

        var results = await syncer.SyncNowAsync(new[] { impostor });

        Assert.False(results[0].Succeeded);
        Assert.Empty(Directory.GetFiles(_work, "fora.txt", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(store.StagingDirectory));
    }

    [Fact]
    public void Store_ListsClassesInNaturalOrder_WithoutItsOwnFolders()
    {
        foreach (var name in new[] { "10º A", "6º B", "6º A", ".recebendo" })
        {
            Directory.CreateDirectory(Path.Combine(ClassesDirectory, name));
        }

        Assert.Equal(new[] { "6º A", "6º B", "10º A" }, ClassStore.ListClasses(ClassesDirectory));
    }

    [Theory]
    [InlineData("notas.txt", "PC-05", "notas (PC-05).txt")]
    [InlineData("site/index.html", "Aluno 3", "site/index (Aluno 3).html")]
    [InlineData("LEIAME", "PC:1", "LEIAME (PC_1)")]
    public void Store_NamesAConflictCopyAfterTheMachine(string path, string machine, string expected)
    {
        Assert.Equal(expected, ClassStore.CopyPathFor(path, machine));
    }

    private ClassFolderSyncer Syncer(out ClassStore store)
    {
        store = new ClassStore(ClassesDirectory, "7º A");
        Directory.CreateDirectory(store.DirectoryPath);
        return new ClassFolderSyncer(
            ClassLesson.Start("7º A", DateTime.UtcNow), store, _masterCertificate, LessonPath,
            new MovingRecycleBin(BinDirectory), () => Array.Empty<KnownMachine>(), port: PortOf);
    }

    private int PortOf(KnownMachine machine) => _ports[machine.SessionThumbprint];

    private (KnownMachine Machine, ClassFolder Folder) Machine(string name)
    {
        var folder = new ClassFolder(
            Path.Combine(_work, name, "Desktop", "Pasta da turma"), Path.Combine(_work, name, "pasta-da-turma.txt"),
            new MovingRecycleBin(Path.Combine(_work, name, "Lixeira")));
        var session = new ThumbnailPollerTests.LoopbackSession(
            0, SelfSignedCertificate.Create($"session-{name}"), _masterCertificate, classFolder: folder);
        _sessions.Add(session);
        var machine = session.Machine() with { Name = name };
        _ports[machine.SessionThumbprint] = session.Port;
        return (machine, folder);
    }

    private static string PathIn(string root, string relativePath) =>
        Path.Combine(new[] { root }.Concat(relativePath.Split('/')).ToArray());

    private static void WriteIn(string root, string relativePath, byte[] content, long ticks)
    {
        var path = PathIn(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        File.SetLastWriteTimeUtc(path, new DateTime(ticks, DateTimeKind.Utc));
    }

    private sealed class MovingRecycleBin : IRecycleBin
    {
        private readonly string _bin;

        public MovingRecycleBin(string bin)
        {
            _bin = bin;
            Directory.CreateDirectory(bin);
        }

        public void Recycle(string path)
        {
            var target = Path.Combine(_bin, Path.GetFileName(path));
            if (Directory.Exists(path))
            {
                Directory.Move(path, target);
            }
            else
            {
                File.Move(path, target, overwrite: true);
            }
        }
    }

    // Answers one CollectClassFolder with whatever script sends.
    private sealed class HostileSession : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly X509Certificate2 _certificate = SelfSignedCertificate.Create("hostile-session");

        public HostileSession(X509Certificate2 masterCertificate, Action<SecureChannel> script)
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Task.Run(() =>
            {
                using var client = _listener.AcceptTcpClient();
                using var channel = SecureChannel.AuthenticateAsServer(
                    client, _certificate, new TrustStore(new[] { masterCertificate.Thumbprint }));
                if (channel.Receive().Type == MessageType.CollectClassFolder)
                {
                    script(channel);
                }
            });
        }

        public int Port { get; }

        public string Thumbprint => _certificate.Thumbprint;

        public void Dispose() => _listener.Stop();
    }
}
