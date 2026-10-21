using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderSync against three loopback student PCs (ClassSyncRig): real
// Agents and Sessions, the real store, rounds run by the test one PC after the
// other. The lesson's start, end and leftovers are in ClassFolderSyncTests.Lesson.cs.
public sealed partial class ClassFolderSyncTests
{
    private const string Turma = ClassSyncRig.Turma;

    private static DateTime Utc(int minutes) => new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc).AddMinutes(minutes);

    private static byte[] Bytes(int length, byte seed) =>
        Enumerable.Range(0, length).Select(i => (byte)(i * 31 + seed)).ToArray();

    [Fact]
    public async Task AFileSavedOnOnePc_ReachesTheOthers()
    {
        using var rig = new ClassSyncRig();
        var sync = rig.NewSync();
        var statuses = ClassSyncRig.Watch(sync);
        var started = await sync.StartAsync(Turma);
        Assert.All(started, result => Assert.True(result.Ok, $"{result.Machine.Name}: {result.Detail}"));
        Assert.All(rig.Pcs, pc => Assert.Equal(sync.Lesson, pc.Agent.Current));
        var content = Bytes(FileTransfer.ChunkSize + 100, 1);

        rig.Pcs[0].Write("mapa.png", content, Utc(1));
        await rig.Rounds(sync, 2);

        Assert.Equal(content, File.ReadAllBytes(rig.StorePath("mapa.png")));
        Assert.Equal(Utc(1), File.GetLastWriteTimeUtc(rig.StorePath("mapa.png")));
        foreach (var pc in rig.Pcs)
        {
            Assert.Equal(content, pc.Read("mapa.png"));
            Assert.Equal(Utc(1), pc.TimeOf("mapa.png"));
        }

        await rig.Rounds(sync, 1);
        Assert.All(rig.Pcs, pc => Assert.Equal(PcSyncState.InSync, statuses[pc.Name].State));
    }

    [Fact]
    public async Task ADownloadInProgress_IsNotCopied()
    {
        using var rig = new ClassSyncRig();
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        var pc1 = rig.Pcs[0];
        pc1.Write("video.mp4.crdownload", Bytes(10, 1), Utc(1));
        pc1.Write("trabalho.docx", Bytes(10, 2), Utc(1));
        await rig.Rounds(sync, 1);

        // Still being saved: it changed since the last listing.
        pc1.Write("trabalho.docx", Bytes(20, 3), Utc(2));
        await rig.Rounds(sync, 1);

        Assert.Empty(rig.StoreFiles());
        Assert.All(rig.Pcs.Skip(1), pc => Assert.Empty(pc.Files()));

        // Held still between two listings: copied. The download never is.
        await rig.Rounds(sync, 1);

        Assert.Equal(new[] { "trabalho.docx" }, rig.StoreFiles());
        Assert.All(rig.Pcs.Skip(1), pc => Assert.Equal(new[] { "trabalho.docx" }, pc.Files()));
        Assert.Equal(Bytes(20, 3), rig.Pcs[2].Read("trabalho.docx"));
    }

    [Fact]
    public async Task TwoPcsSameNameDifferentFiles_BothKept()
    {
        using var rig = new ClassSyncRig();
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        var first = Bytes(100, 1);
        var second = Bytes(200, 2);
        rig.Pcs[0].Write("images.jpg", first, Utc(1));
        rig.Pcs[1].Write("images.jpg", second, Utc(2));

        await rig.Rounds(sync, 3);

        Assert.Equal(new[] { "images (2).jpg", "images.jpg" }, rig.StoreFiles());
        Assert.Equal(first, File.ReadAllBytes(rig.StorePath("images.jpg")));
        Assert.Equal(second, File.ReadAllBytes(rig.StorePath("images (2).jpg")));
        foreach (var pc in rig.Pcs)
        {
            Assert.Equal(new[] { "images (2).jpg", "images.jpg" }, pc.Files());
            Assert.Equal(first, pc.Read("images.jpg"));
            Assert.Equal(second, pc.Read("images (2).jpg"));
        }
    }

    [Fact]
    public async Task AnEdit_ReplacesEverywhere()
    {
        using var rig = new ClassSyncRig();
        rig.WriteInStore("texto.txt", Bytes(10, 1), Utc(1));
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        await rig.Rounds(sync, 1);
        Assert.All(rig.Pcs, pc => Assert.Equal(Bytes(10, 1), pc.Read("texto.txt")));

        rig.Pcs[1].Write("texto.txt", Bytes(30, 2), Utc(5));
        await rig.Rounds(sync, 3);

        Assert.Equal(Bytes(30, 2), File.ReadAllBytes(rig.StorePath("texto.txt")));
        Assert.All(rig.Pcs, pc => Assert.Equal(Bytes(30, 2), pc.Read("texto.txt")));
        Assert.All(rig.Pcs, pc => Assert.Equal(Utc(5), pc.TimeOf("texto.txt")));
        // The class's earlier copy went to the teacher's Recycle Bin.
        Assert.Equal(Bytes(10, 1), rig.TeacherBin.Read(rig.TeacherBin.FileNames().Single()));
        Assert.Equal(new[] { "texto.txt" }, rig.StoreFiles());
    }

    [Fact]
    public async Task StudentDeletes_FileComesBack()
    {
        using var rig = new ClassSyncRig();
        rig.WriteInStore("texto.txt", Bytes(10, 1), Utc(1));
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        await rig.Rounds(sync, 1);

        File.Delete(rig.Pcs[2].PathOf("texto.txt"));
        await rig.Rounds(sync, 1);

        Assert.Equal(Bytes(10, 1), rig.Pcs[2].Read("texto.txt"));
        Assert.Equal(Utc(1), rig.Pcs[2].TimeOf("texto.txt"));
    }

    [Fact]
    public async Task TeacherDeletesInTheStore_FileGoesFromThePcs()
    {
        using var rig = new ClassSyncRig();
        rig.WriteInStore("texto.txt", Bytes(10, 1), Utc(1));
        rig.WriteInStore("fica.txt", Bytes(10, 2), Utc(1));
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        await rig.Rounds(sync, 1);

        File.Delete(rig.StorePath("texto.txt"));
        await rig.Rounds(sync, 2);

        Assert.True(rig.Store.IsDeleted(Turma, "texto.txt"));
        Assert.Equal(new[] { "fica.txt" }, rig.StoreFiles());
        foreach (var pc in rig.Pcs)
        {
            Assert.Equal(new[] { "fica.txt" }, pc.Files());
            Assert.Equal(new[] { "texto.txt" }, pc.Bin.FileNames()); // the student's Recycle Bin
        }
    }

    [Fact]
    public async Task AFileHeldOpenOnAPc_IsRetried_OthersGoOn()
    {
        using var rig = new ClassSyncRig();
        rig.WriteInStore("a.docx", Bytes(10, 1), Utc(1));
        rig.WriteInStore("b.txt", Bytes(10, 2), Utc(1));
        var sync = rig.NewSync();
        var statuses = ClassSyncRig.Watch(sync);
        await sync.StartAsync(Turma);
        await rig.Rounds(sync, 1);

        // The teacher changes both; Word holds a.docx open on PC2.
        rig.WriteInStore("a.docx", Bytes(40, 3), Utc(9));
        rig.WriteInStore("b.txt", Bytes(40, 4), Utc(9));
        var pc2 = rig.Pcs[1];
        using (new FileStream(pc2.PathOf("a.docx"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await rig.Rounds(sync, 1);

            Assert.Equal(Bytes(40, 4), pc2.Read("b.txt"));
            Assert.Equal(PcSyncState.Failed, statuses[pc2.Name].State);
            Assert.StartsWith("a.docx (", statuses[pc2.Name].Detail);
            Assert.All(new[] { rig.Pcs[0], rig.Pcs[2] }, pc => Assert.Equal(Bytes(40, 3), pc.Read("a.docx")));
        }
        Assert.Equal(Bytes(10, 1), pc2.Read("a.docx"));

        await rig.Rounds(sync, 1);

        Assert.Equal(Bytes(40, 3), pc2.Read("a.docx"));
        Assert.Equal(PcSyncState.InSync, statuses[pc2.Name].State);
        Assert.Equal(Bytes(40, 3), File.ReadAllBytes(rig.StorePath("a.docx"))); // the teacher's copy was never taken for an edit
    }

    [Fact]
    public async Task APushPlannedBeforeTheWait_DoesNotOverwriteWhatTheStudentSavedMeanwhile()
    {
        using var rig = new ClassSyncRig();
        rig.WriteInStore("nova.txt", Bytes(10, 1), Utc(1));
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        var pc1 = rig.Pcs[0];
        var saved = false;
        // The round plans to push nova.txt; while it waits for its turn, the student saves a file of that name.
        sync.WaitingForCopySlot = machine =>
        {
            if (machine == pc1.Machine && !saved)
            {
                saved = true;
                pc1.Write("nova.txt", Bytes(30, 2), Utc(5));
            }
        };

        await sync.RunRoundAsync(pc1.Machine, CancellationToken.None);

        Assert.True(saved);
        Assert.Equal(Bytes(30, 2), pc1.Read("nova.txt"));
        Assert.Equal(Bytes(10, 1), File.ReadAllBytes(rig.StorePath("nova.txt")));

        // Once it held still it is the student's own file of that name, kept next to the class's.
        for (var round = 0; round < 3; round++)
        {
            await sync.RunRoundAsync(pc1.Machine, CancellationToken.None);
        }

        Assert.Equal(new[] { "nova (2).txt", "nova.txt" }, rig.StoreFiles());
        Assert.Equal(Bytes(30, 2), File.ReadAllBytes(rig.StorePath("nova (2).txt")));
        Assert.Equal(Bytes(10, 1), pc1.Read("nova.txt"));
        Assert.Equal(Bytes(30, 2), pc1.Read("nova (2).txt"));
    }

    [Fact]
    public async Task APlanOverTheLimit_FinishesOverSeveralRounds()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var count = ClassFolderSync.MaxActionsPerRound + 5;
        for (var file = 0; file < count; file++)
        {
            rig.WriteInStore($"arquivo {file:00}.txt", Bytes(5, (byte)file), Utc(1));
        }
        var sync = rig.NewSync();
        var statuses = ClassSyncRig.Watch(sync);
        await sync.StartAsync(Turma);
        var pc1 = rig.Pcs[0];

        await rig.Rounds(sync, 1);

        Assert.Equal(ClassFolderSync.MaxActionsPerRound, pc1.Files().Length);
        Assert.Equal(new PcStatus(pc1.Machine, PcSyncState.Copying, "copiando 5 arquivos"), statuses[pc1.Name]);

        await rig.Rounds(sync, 1);

        Assert.Equal(count, pc1.Files().Length);
        Assert.Equal(PcSyncState.InSync, statuses[pc1.Name].State);
    }

    [Fact]
    public async Task AStalePc_IsNotSynced_UntilItFollowsTheLesson()
    {
        using var rig = new ClassSyncRig();
        var sync = rig.NewSync();
        var statuses = ClassSyncRig.Watch(sync);
        await sync.StartAsync(Turma);
        var pc1 = rig.Pcs[0];
        pc1.Write("mapa.png", Bytes(10, 1), Utc(1));
        pc1.SessionLessonOverride = ClassLesson.None(1); // its Session hasn't seen the lesson yet

        await rig.Rounds(sync, 2);

        Assert.Empty(rig.StoreFiles());
        Assert.Equal(new PcStatus(pc1.Machine, PcSyncState.Copying, "atualizando a aula"), statuses[pc1.Name]);

        pc1.SessionLessonOverride = null;
        await rig.Rounds(sync, 2);

        Assert.Equal(new[] { "mapa.png" }, rig.StoreFiles());
    }

    [Fact]
    public async Task AnOldSession_IsFailedWithMidviewAntigo()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        using var old = new OldSession(rig.Master);
        var machine = new KnownMachine("PC-antigo", "127.0.0.1", old.Thumbprint, null);
        using var sync = new ClassFolderSync(
            rig.Store, rig.Master, () => new[] { machine }, _ => true, () => rig.Now, rig.AgentPort, old.Port,
            TimeSpan.FromHours(1), rig.LessonPath);
        var statuses = ClassSyncRig.Watch(sync);

        var started = await sync.StartAsync(Turma);
        await sync.RunRoundAsync(machine, CancellationToken.None);

        Assert.False(started.Single().Ok); // no Agent identity to send the lesson to
        Assert.True(sync.Lesson.IsOn);
        Assert.Equal(new PcStatus(machine, PcSyncState.Failed, "Midview antigo"), statuses[machine.Name]);
    }

    [Fact]
    public async Task TheLoop_KeepsThePcsTheSame_OnItsOwn_AndLeavesTheOfflineOnesAlone()
    {
        using var rig = new ClassSyncRig();
        rig.Offline[rig.Pcs[2].Name] = true;
        var sync = rig.NewSync(TimeSpan.FromMilliseconds(100));
        var statuses = ClassSyncRig.Watch(sync);
        await sync.StartAsync(Turma);

        rig.Pcs[0].Write("mapa.png", Bytes(10, 1), Utc(1));

        await WaitUntil(() => rig.Pcs[1].Has("mapa.png"));
        Assert.Equal(new[] { "mapa.png" }, rig.StoreFiles());
        Assert.False(rig.Pcs[2].Has("mapa.png"));
        Assert.Equal(PcSyncState.Offline, statuses[rig.Pcs[2].Name].State);

        sync.Dispose();
        rig.Pcs[0].Write("depois.txt", Bytes(10, 2), Utc(2));
        await Task.Delay(500);
        Assert.Equal(new[] { "mapa.png" }, rig.StoreFiles());
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(50);
        }
    }
}
