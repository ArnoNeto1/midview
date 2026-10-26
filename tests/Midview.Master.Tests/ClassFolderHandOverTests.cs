using System.Net;
using System.Net.Sockets;
using System.Text;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderHandOver: "Guardar as pastas das turmas neste computador" (plan
// decision 7, Review Focus 5). Two teacher PCs over loopback,
// each with a store of its own in temp folders: the home is the rig's Master
// (PROF-1: the rig's store, a sync, its window's controller and a
// ClassFolderHost serving the hand-over) and PROF-2 (the rig's OtherMaster,
// with its own store, sync and files) takes the folders over. The rig's Agents
// keep the home. Never the teacher's real folders; ports the system picks.
// A hostile home, a home that can't be reached, and the window's texts are in
// ClassFolderHandOverTests.Hostile.cs.
public sealed partial class ClassFolderHandOverTests
{
    private const string Turma = ClassSyncRig.Turma;
    private const string OldHome = ClassSyncRig.MachineName;
    private const string NewHome = "PROF-2";

    private static readonly DateTime Earlier = new(2026, 10, 14, 9, 15, 0, DateTimeKind.Utc);

    // Review Focus 5's happy path: every class (an empty one too), its files
    // with their times, its index and the lessons map arrive; this PC's own
    // earlier classes go aside; the old home moves its copy aside and stands
    // down; the Agents keep this PC as the home, with a newer generation.
    [Fact]
    public async Task TakingOver_CopiesEveryClass_MovesBothOldCopiesAside_AndTheAgentsFollow()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        var lessonId = await lab.MakeTheOldHomeTheHomeAsync();
        var generation = rig.Pcs[0].Agent.CurrentHome!.Generation;
        lab.FillTheOldHome();
        // A record not saved yet: the copy saves it first.
        var known = new FileVersion(3, Earlier.Ticks);
        rig.Store.RecordSynced(Turma, "chave-do-pc1", "a.txt", known);
        lab.GiveTheNewHomeAClassOfItsOwn();

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.True(result.Ok, result.Detail);
        Assert.Equal(
            ClassFolderText.TookOver(3, 3) + " " + ClassFolderText.OwnClassesAside("Turmas (antes de 20-10-2026 1330)") + " "
            + ClassFolderText.OldCopyAside(OldHome, "Turmas (passadas para PROF-2)"),
            result.Detail);
        // Everything arrived, as it was.
        Assert.Equal(new[] { Turma, "3º B", "Vídeos" }, lab.NewStore.List().Select(summary => summary.Turma));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(lab.NewPath(Turma, "a.txt")));
        Assert.Equal(Earlier, File.GetLastWriteTimeUtc(lab.NewPath(Turma, "a.txt")));
        Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(lab.NewPath(Turma, "sub/b.txt")));
        Assert.Equal(Earlier.AddDays(1), File.GetLastWriteTimeUtc(lab.NewPath(Turma, "sub/b.txt")));
        Assert.Equal(HandOverLab.Video, File.ReadAllBytes(lab.NewPath("Vídeos", "aula.mp4")));
        Assert.Empty(lab.NewStore.Files("3º B"));
        Assert.Equal(known, lab.NewStore.LastSynced(Turma, "chave-do-pc1", "a.txt"));
        Assert.Equal(Turma, lab.NewStore.TurmaOfLesson(lessonId));
        Assert.True(lab.NewStore.IsCollected(lessonId));
        // This PC's own earlier classes, aside.
        Assert.False(lab.NewStore.Exists("Antiga"));
        Assert.True(File.Exists(Path.Combine(lab.NewRoot, "Turmas (antes de 20-10-2026 1330)", "Antiga", "velho.txt")));
        // The old home's copy, aside: files, index and lessons map; and it stood down.
        Assert.False(Directory.Exists(Path.Combine(rig.Root, "Turmas")));
        Assert.True(File.Exists(Path.Combine(rig.Root, "Turmas (passadas para PROF-2)", Turma, "a.txt")));
        Assert.True(File.Exists(Path.Combine(rig.Root, "dados", "turmas (passadas para PROF-2)", Turma + ".json")));
        Assert.True(File.Exists(Path.Combine(rig.Root, "dados", "class-lessons (passadas para PROF-2).json")));
        Assert.False(File.Exists(Path.Combine(rig.Root, "dados", "class-lessons.json")));
        Assert.Empty(rig.Store.List());
        Assert.Equal(ClassLesson.None(0), lab.OldSync.Lesson);
        // The Agents keep this PC, newer than the old home.
        foreach (var pc in rig.Pcs)
        {
            var home = pc.Agent.CurrentHome!;
            Assert.Equal((NewHome, generation + 1), (home.Name, home.Generation));
            Assert.Equal(rig.OtherMaster.Thumbprint, home.Thumbprint, ignoreCase: true);
        }
        Assert.Equal(generation + 1, ClassHomeFileMaster.Read(lab.NewHomePath));
        lab.AssertNoStaging();
    }

    // Plan decision 7: never while a lesson is on. The home says so, and nothing changes.
    [Fact]
    public async Task ATakeOverDuringALesson_IsRefused_AndChangesNothing()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        lab.GiveTheNewHomeAClassOfItsOwn();
        await lab.OldSync.StartAsync(Turma);
        var before = lab.Snapshot();

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.False(result.Ok);
        Assert.Equal(ClassFolderText.TakeOverFailed(OldHome, ClassFolderText.EndLessonFirst), result.Detail);
        lab.AssertUnchanged(before);
        Assert.True(lab.OldSync.Lesson.IsOn);
    }

    // The copy arrived whole, but the room's PCs follow a lesson the home's
    // Master doesn't know of (its lesson file lost, say): they refuse the
    // take-over, so this PC puts everything back as it was and the old home
    // stays the home.
    [Fact]
    public async Task WhenThePcsRefuseTheClaim_EverythingIsPutBack()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        foreach (var pc in rig.Pcs)
        {
            var on = new ClassLesson(
                pc.Agent.Current.SavedAt + 10, Guid.NewGuid(), "3º B", new DateTimeOffset(rig.Now).ToUnixTimeSeconds(), Ended: false);
            Assert.True(ClassFolderClient.SetLesson(pc.Machine, rig.Master, on, rig.AgentPort).Succeeded);
        }
        Assert.False(lab.OldSync.Lesson.IsOn);
        lab.FillTheOldHome();
        lab.GiveTheNewHomeAClassOfItsOwn();
        var before = lab.Snapshot();

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.False(result.Ok);
        Assert.Equal(ClassFolderText.TakeOverFailed(OldHome, ClassFolderText.EndLessonFirst), result.Detail);
        lab.AssertUnchanged(before);
        Assert.Null(ClassHomeFileMaster.Read(lab.NewHomePath));
    }

    // Review Focus 5: the old home's Master closed in the middle of the copy.
    // This PC throws its partial copy away; both stores and the Agents stay as they were.
    [Fact]
    public async Task ADropInTheMiddle_LeavesBothStoresAndTheAgentsAsTheyWere()
    {
        using var rig = new ClassSyncRig(pcs: 2, homes: 2);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        var big = new byte[FileTransfer.ChunkSize * 2 + 10];
        for (var number = 0; number < 40; number++)
        {
            rig.WriteInStore($"grande-{number:00}.bin", big, Earlier, "Vídeos");
        }
        lab.GiveTheNewHomeAClassOfItsOwn();
        var before = lab.Snapshot();
        var dropped = 0;
        var progress = new Inline(step =>
        {
            if (step.Bytes > 0 && Interlocked.Exchange(ref dropped, 1) == 0)
            {
                lab.Host!.Dispose();
            }
        });

        var result = await lab.Taker.TakeOverAsync(lab.Home, progress);

        Assert.Equal(1, dropped);
        Assert.False(result.Ok);
        Assert.Equal(ClassFolderText.TakeOverFailed(OldHome, ClassFolderText.CopyDropped(OldHome)), result.Detail);
        lab.AssertUnchanged(before);
        Assert.Equal(41, rig.Store.Files("Vídeos").Count);
    }

    // The copy is one connection, and the state the remote's window polls for
    // another: the home's cap on one PC's connections leaves room for both, so
    // the window keeps showing the home while the copy runs.
    [Fact]
    public async Task WhileTheRemoteTakesOver_ItStillReadsTheHomesState()
    {
        using var rig = new ClassSyncRig(pcs: 2, homes: 2);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        using var remote = new RemoteClassFolderController(
            rig.OtherMaster, () => lab.Home, () => rig.Machines, machine => !rig.Offline.ContainsKey(machine.Name),
            TimeSpan.FromHours(1), rig.AgentPort, () => rig.Now);
        await remote.PollAsync();
        IReadOnlyList<string>? seenDuring = null;
        var progress = new Inline(step =>
        {
            if (seenDuring is null && step.Files > 0)
            {
                Assert.True(rig.Store.Create("9º Z").Ok);
                remote.PollAsync().Wait();
                seenDuring = remote.ReadAsync(lessonFiles: false).Result.Classes.Select(summary => summary.Turma).ToList();
            }
        });

        var result = await remote.TakeOverAsync(lab.Taker, remote.Home, homeClosed: false, progress);

        Assert.True(result.Ok, result.Detail);
        Assert.NotNull(seenDuring);
        Assert.Contains("9º Z", seenDuring!);
        Assert.Equal(new[] { Turma, "3º B", "Vídeos" }, lab.NewStore.List().Select(summary => summary.Turma));
        Assert.All(rig.Pcs, pc => Assert.Equal(NewHome, pc.Agent.CurrentHome!.Name));
    }

    // Only the PC the room's Agents now keep as the home may tell the old home
    // it took over: anyone else is refused, and the old home keeps its copy.
    [Fact]
    public async Task HandedOver_FromAPcTheAgentsDontKeepAsTheHome_IsRefused()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var lesson = lab.OldSync.Lesson;

        var (ok, _) = lab.SendHandedOver(NewHome);

        Assert.False(ok);
        Assert.Equal(new[] { Turma, "3º B" }, rig.Store.List().Select(summary => summary.Turma));
        Assert.False(Directory.Exists(Path.Combine(rig.Root, "Turmas (passadas para PROF-2)")));
        Assert.Equal(lesson, lab.OldSync.Lesson);
        Assert.All(rig.Pcs, pc => Assert.Equal(OldHome, pc.Agent.CurrentHome!.Name));
    }

    // A folder "Turmas (passadas para PROF-2)" already there (an earlier
    // hand-over to the same PC): the next one is numbered.
    [Fact]
    public async Task AnOldCopyAlreadyThere_TheNextOneIsNumbered()
    {
        using var rig = new ClassSyncRig(homes: 1, pcs: 1);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        rig.Store.RecordSynced(Turma, "chave-do-pc1", "a.txt", new FileVersion(3, Earlier.Ticks));
        Directory.CreateDirectory(Path.Combine(rig.Root, "Turmas (passadas para PROF-2)"));

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.True(result.Ok, result.Detail);
        Assert.True(File.Exists(Path.Combine(rig.Root, "Turmas (passadas para PROF-2) (2)", Turma, "a.txt")));
        Assert.EndsWith(" " + ClassFolderText.OldCopyAside(OldHome, "Turmas (passadas para PROF-2) (2)"), result.Detail);
        Assert.True(Directory.Exists(Path.Combine(rig.Root, "dados", "turmas (passadas para PROF-2) (2)")));
    }

    // Work that blocks for a long time, on a thread of its own: never the
    // pool's, which the tests running alongside need (their bounds are short).
    private static Task OwnThread(Action work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    // A progress report that runs where it is reported (a Progress<T> would post it).
    private sealed class Inline : IProgress<HandOverProgress>
    {
        private readonly Action<HandOverProgress> _report;

        public Inline(Action<HandOverProgress> report) => _report = report;

        public void Report(HandOverProgress value) => _report(value);
    }

    // The two teacher PCs of the lab (see the class comment). Host: the old
    // home's, serving the hand-over (none for a home that can't be reached).
    private sealed partial class HandOverLab : IDisposable
    {
        public static readonly byte[] Video = Enumerable.Range(0, FileTransfer.ChunkSize + 1000).Select(i => (byte)i).ToArray();

        private readonly ClassSyncRig _rig;

        public HandOverLab(ClassSyncRig rig, bool openHost = true)
        {
            _rig = rig;
            OldSync = rig.NewSync();
            OldController = LocalClassFolderControllerTests.NewController(rig, OldSync);
            Giver = new ClassFolderHandOver(
                rig.Store, OldSync, rig.Master, OldHome, () => rig.Machines, IsOnline, () => rig.Now, rig.AgentPort,
                homePath: rig.HomePath, log: message => GiverLog(message));

            NewRoot = Path.Combine(rig.Root, "prof-2");
            NewStore = new ClassStore(
                Path.Combine(NewRoot, "Turmas"), Path.Combine(NewRoot, "dados", "turmas"), new FolderBin(Path.Combine(NewRoot, "lixeira")),
                () => rig.Now, lessonsPath: Path.Combine(NewRoot, "dados", "class-lessons.json"), freeBytes: () => NewFreeSpace);
            NewSync = new ClassFolderSync(
                NewStore, rig.OtherMaster, () => rig.Machines, IsOnline, () => rig.Now, NewHome, agentPort: rig.AgentPort,
                sessionPort: rig.SessionPort, interval: TimeSpan.FromHours(1), lessonPath: Path.Combine(NewRoot, "dados", "class-lesson.json"),
                homePath: NewHomePath);
            Taker = new ClassFolderHandOver(
                NewStore, NewSync, rig.OtherMaster, NewHome, () => rig.Machines, IsOnline, () => rig.Now, rig.AgentPort,
                homePath: NewHomePath, log: message => TakerLog(message));
            // A claim here waits for an Agent's lock only behind BusyAgents, whose
            // rigs' Agents wait BusyAgents.ClaimWait, not ClassHomeClaim.MaxLockWait;
            // any other answer these tests lose was given before it was lost.
            Taker.LostAnswerWait = BusyAgents.ClaimWait + TimeSpan.FromMilliseconds(500);

            if (openHost)
            {
                Host = ClassFolderHost.Open(
                    rig.Master, OldController,
                    (thumbprint, _) => Task.FromResult(string.Equals(thumbprint, rig.OtherMaster.Thumbprint, StringComparison.OrdinalIgnoreCase)),
                    0, _ => { }, IPAddress.Loopback, Giver) ?? throw new InvalidOperationException("The host could not listen.");
            }
        }

        public ClassFolderSync OldSync { get; }

        public LocalClassFolderController OldController { get; }

        public ClassFolderHandOver Giver { get; }

        public ClassFolderHost? Host { get; }

        public string NewRoot { get; }

        public string NewHomePath => Path.Combine(NewRoot, "dados", "class-home.json");

        public ClassStore NewStore { get; }

        public ClassFolderSync NewSync { get; }

        public ClassFolderHandOver Taker { get; }

        public long NewFreeSpace { get; set; } = long.MaxValue;

        // What the taking PC logs, and the old home (nothing by default).
        public Action<string> TakerLog { get; set; } = _ => { };

        public Action<string> GiverLog { get; set; } = _ => { };

        // What each of the rig's Agents keeps as the home.
        public List<ClassHome?> Homes() => _rig.Pcs.Select(pc => pc.Agent.CurrentHome).ToList();

        // The home as the room's Agents keep it, where its host listens (the
        // Agents keep the port its claims carried, ClassFolderRemote.Port).
        public ClassHome Home => _rig.Pcs[0].Agent.CurrentHome! with
        {
            Address = IPAddress.Loopback.ToString(),
            Port = Host?.Port ?? ClosedPort,
        };

        // Nothing listens there.
        public int ClosedPort { get; } = FreePort();

        public string NewPath(string turma, string relativePath) =>
            Path.Combine(NewRoot, "Turmas", turma, relativePath.Replace('/', Path.DirectorySeparatorChar));

        // A lesson started and ended by the old home: the Agents keep it as the
        // home, and the lessons map has the lesson.
        public async Task<Guid> MakeTheOldHomeTheHomeAsync()
        {
            var started = await OldSync.StartAsync(Turma);
            var lessonId = OldSync.Lesson.LessonId;
            await OldSync.EndAsync(EndChoice.Keep);
            // What the start said of each PC, should one not keep the old home.
            var said = string.Join("; ", started.Select(line => $"{line.Machine.Name} {line.Ok} {line.Detail}"));
            Assert.All(_rig.Pcs.Take(_rig.Homes), pc => Assert.True(
                pc.Agent.CurrentHome?.Name == OldHome,
                $"{pc.Name} keeps {pc.Agent.CurrentHome?.Name}@{pc.Agent.CurrentHome?.Generation}; the start said {said}"));
            return lessonId;
        }

        // 3º A with two files (one in a subfolder), 3º B empty, Vídeos with a file of more than one chunk.
        public void FillTheOldHome()
        {
            _rig.WriteInStore("a.txt", new byte[] { 1, 2, 3 }, Earlier);
            _rig.WriteInStore("sub/b.txt", new byte[] { 4, 5 }, Earlier.AddDays(1));
            Assert.True(_rig.Store.Create("Vídeos").Ok);
            _rig.WriteInStore("aula.mp4", Video, Earlier, "Vídeos");
        }

        public void GiveTheNewHomeAClassOfItsOwn()
        {
            Assert.True(NewStore.Create("Antiga").Ok);
            ClassSyncRig.WriteFile(NewPath("Antiga", "velho.txt"), new byte[] { 9 }, Earlier);
        }

        // RemoteHandOverDone, from the new home's PC, to the old home's host.
        public (bool Ok, string Detail) SendHandedOver(string name)
        {
            using var tcp = new TcpClient();
            tcp.Connect(IPAddress.Loopback, Host!.Port);
            using var channel = SecureChannel.AuthenticateAsClient(
                tcp, "localhost", _rig.OtherMaster, new TrustStore(new[] { _rig.Master.Thumbprint }));
            channel.Send(new Message(MessageType.RemoteHandOverDone, Encoding.UTF8.GetBytes(name)));
            var reply = channel.Receive();
            Assert.Equal(MessageType.CommandAck, reply.Type);
            return CommandResult.Decode(reply.Payload);
        }

        // Every file under the lab's folder (both PCs' stores, data and asides) and what the Agents keep.
        public Snapshot Snapshot() => new(
            Directory.GetFiles(_rig.Root, "*", SearchOption.AllDirectories)
                .Where(path => !path.StartsWith(Path.Combine(_rig.Root, "PC"), StringComparison.OrdinalIgnoreCase))
                .Select(path => (Path.GetRelativePath(_rig.Root, path), new FileInfo(path).Length))
                .Order()
                .ToList(),
            _rig.Pcs.Select(pc => pc.Agent.CurrentHome).ToList());

        public void AssertUnchanged(Snapshot before)
        {
            var after = Snapshot();
            Assert.Equal(before.Files, after.Files);
            Assert.Equal(before.Homes, after.Homes);
            AssertNoStaging();
        }

        public void AssertNoStaging()
        {
            Assert.False(Directory.Exists(Path.Combine(NewRoot, "Turmas (recebendo)")));
            Assert.False(Directory.Exists(Path.Combine(NewRoot, "dados", "turmas (recebendo)")));
            Assert.False(File.Exists(Path.Combine(NewRoot, "dados", "class-lessons.json.recebendo")));
        }

        public void Dispose()
        {
            Host?.Dispose();
            NewSync.Dispose();
        }

        private bool IsOnline(KnownMachine machine) => !_rig.Offline.ContainsKey(machine.Name);

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    private sealed record Snapshot(List<(string Path, long Length)> Files, List<ClassHome?> Homes);
}
