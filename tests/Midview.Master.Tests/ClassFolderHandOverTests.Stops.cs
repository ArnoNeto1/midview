using System.Net;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderHandOver: take-overs that stop before anything changes (Ruling
// 17): a half copy an earlier take-over couldn't remove, the teacher's
// "Cancelar", the room keeping another home than the one the teacher was
// asked about, a lesson started on the home during the copy, and a PC at the
// home's address with another certificate. Each leaves both stores and the
// Agents as they were.
public sealed partial class ClassFolderHandOverTests
{
    // (c) A half copy that can't be removed (a file of it open in another
    // program) is never taken in: the take-over is refused.
    [Fact]
    public async Task AHalfCopyThatCantBeRemoved_IsNeverTakenIn()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        var stray = Path.Combine(lab.NewRoot, "Turmas (recebendo)", Turma, "velha.txt");
        ClassSyncRig.WriteFile(stray, new byte[] { 7 }, Earlier);
        var before = lab.Snapshot();
        HandOverResult result;
        using (new FileStream(stray, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await lab.Taker.TakeOverAsync(lab.Home);
        }

        Assert.False(result.Ok);
        Assert.Equal(ClassFolderText.TakeOverFailed(OldHome, ClassFolderText.StagingLeft("Turmas (recebendo)")), result.Detail);
        Assert.Equal(before.Files, lab.Snapshot().Files);
        Assert.Equal(before.Homes, lab.Snapshot().Homes);
        Assert.False(Directory.Exists(Path.Combine(lab.NewRoot, "Turmas")));
    }

    // (d) "Cancelar" in the middle of the copy: thrown away, nothing changes.
    [Fact]
    public async Task ACopyCancelledInTheMiddle_ChangesNothing()
    {
        using var rig = new ClassSyncRig(pcs: 2, homes: 2);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        lab.FillTheOldHomeWithBigFiles();
        lab.GiveTheNewHomeAClassOfItsOwn();
        var before = lab.Snapshot();
        var cancel = new HandOverCancel();
        var stopped = false;
        var progress = new Inline(step =>
        {
            if (step.Bytes > 0 && !cancel.IsCancelled)
            {
                stopped = cancel.Cancel();
            }
        });

        var result = await lab.Taker.TakeOverAsync(lab.Home, progress, cancel);

        Assert.True(stopped);
        Assert.False(result.Ok);
        Assert.Equal(ClassFolderText.TakeOverFailed(OldHome, ClassFolderText.CopyCancelled), result.Detail);
        lab.AssertUnchanged(before);
    }

    // Ruling 18 (2): a home that sends its manifest and then nothing more:
    // cancelling closes the connection at once, long before the network's
    // own wait would give up, and the half copy is gone.
    [Fact]
    public async Task CancellingAStalledCopy_ClosesItAtOnce()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig, openHost: false);
        await lab.MakeTheOldHomeTheHomeAsync();
        var before = lab.Snapshot();
        using var stalled = new ManualResetEventSlim();
        using var stall = new ManualResetEventSlim();
        HandOverResult result;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using (var home = new ScriptedHandOverHome(rig, channel =>
        {
            SendManifest(channel, new[] { Turma }, ($"files/{Turma}/a.txt", 3, Ticks));
            stalled.Set();
            stall.Wait(TimeSpan.FromSeconds(60));
        }))
        {
            var cancel = new HandOverCancel();
            var taking = lab.Taker.TakeOverAsync(lab.Home with { Port = home.Port }, cancel: cancel);
            Assert.True(stalled.Wait(TimeSpan.FromSeconds(10)));
            await Task.Delay(200);
            clock.Restart();
            _ = Task.Run(() => cancel.Cancel());
            result = await taking;
            clock.Stop();
            stall.Set();
        }

        Assert.False(result.Ok);
        Assert.Equal(ClassFolderText.TakeOverFailed(OldHome, ClassFolderText.CopyCancelled), result.Detail);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
        Assert.True(clock.Elapsed < lab.Taker.NetworkWait);
        lab.AssertUnchanged(before);
    }

    // Ruling 19 (e): once the copy is going in place, "Cancelar" is too late,
    // and says so at once (the window shows nothing of a cancel then); the
    // take-over goes on to the end.
    [Fact]
    public async Task ACancelOnceTheCopyIsGoingInPlace_IsTooLate_AndTheTakeOverEnds()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        var cancel = new HandOverCancel();
        bool? cancelled = null;
        var progress = new Inline(step =>
        {
            if (step.InPlace)
            {
                cancelled = cancel.Cancel();
            }
        });

        var result = await lab.Taker.TakeOverAsync(lab.Home, progress, cancel);

        Assert.False(cancelled);
        Assert.False(cancel.IsCancelled);
        Assert.True(result.Ok, result.Detail);
        Assert.Equal(NewHome, rig.Pcs[0].Agent.CurrentHome!.Name);
    }

    // A cancel before anything is received stops it there; asked again, it is still cancelled.
    [Fact]
    public async Task ACancelBeforeTheCopy_StopsIt()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        var before = lab.Snapshot();
        var cancel = new HandOverCancel();
        Assert.True(cancel.Cancel());
        Assert.True(cancel.Cancel());

        var result = await lab.Taker.TakeOverAsync(lab.Home, cancel: cancel);

        Assert.Equal(ClassFolderText.TakeOverFailed(OldHome, ClassFolderText.CopyCancelled), result.Detail);
        lab.AssertUnchanged(before);
    }

    // Ruling 18 (4): the copy says when it is in place (from then on it can't
    // be cancelled, and the window's "Cancelar" goes), once, after every file.
    [Fact]
    public async Task TheCopySaysWhenItIsInPlace_AfterEveryFile()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        var steps = new List<HandOverProgress>();
        var progress = new Inline(step =>
        {
            lock (steps)
            {
                steps.Add(step);
            }
        });

        var result = await lab.Taker.TakeOverAsync(lab.Home, progress);

        Assert.True(result.Ok, result.Detail);
        Assert.Single(steps, step => step.InPlace);
        var inPlace = steps.FindIndex(step => step.InPlace);
        Assert.Contains(steps.Take(inPlace), step => step.Files == 3 && step.TotalFiles == 3);
        Assert.Equal(steps.Count - 1, inPlace);
    }

    // (f) The window asked about PROF-1; the room keeps another PC as the home
    // by the time the teacher says yes: nothing is copied or claimed.
    [Fact]
    public async Task AHomeOtherThanTheOneConfirmed_IsRefused()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        var before = lab.Snapshot();
        var home = lab.Home;
        using var remote = new RemoteClassFolderController(
            rig.OtherMaster, () => home, () => rig.Machines, machine => !rig.Offline.ContainsKey(machine.Name),
            TimeSpan.FromHours(1), rig.AgentPort, () => rig.Now);
        await remote.PollAsync();
        var confirmed = remote.Home;
        home = home with { Name = "PROF-3" };

        var result = await remote.TakeOverAsync(lab.Taker, confirmed, homeClosed: false);

        Assert.False(result.Ok);
        Assert.Equal(ClassFolderText.HomeChanged(OldHome, "PROF-3"), result.Detail);
        lab.AssertUnchanged(before);
    }

    // (i) A start on the home while the copy runs is refused there, saying
    // why (a lesson starting then would meet the Agents' claims, splitting the
    // room): the copy carries on and the take-over is made. A home with a
    // lessons map, and one with none yet (the home by its claims alone).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AStartOnTheHomeDuringTheCopy_IsRefusedThere_AndTheTakeOverIsMade(bool lessonsMap)
    {
        using var rig = new ClassSyncRig(pcs: 2, homes: 2);
        using var lab = new HandOverLab(rig);
        if (lessonsMap)
        {
            await lab.MakeTheOldHomeTheHomeAsync();
        }
        else
        {
            // The home by its claims alone: no lesson yet, so no lessons map.
            foreach (var pc in rig.Pcs)
            {
                Assert.True(ClassFolderClient.ClaimHome(
                    pc.Machine, rig.Master, new ClassHomeClaim(OldHome, ClassFolderRemote.Port, false, rig.NowTicks), rig.AgentPort).Ok);
            }
            ClassHomeFileMaster.Write(rig.NowTicks, rig.HomePath);
            Assert.False(File.Exists(Path.Combine(rig.Root, "dados", "class-lessons.json")));
        }
        lab.FillTheOldHome();
        lab.FillTheOldHomeWithBigFiles();
        lab.GiveTheNewHomeAClassOfItsOwn();
        IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)>? lines = null;
        var progress = new Inline(step =>
        {
            if (lines is null && step.Files > 0 && !step.InPlace)
            {
                lines = lab.OldSync.StartAsync("3º B").GetAwaiter().GetResult();
            }
        });

        var result = await lab.Taker.TakeOverAsync(lab.Home, progress);

        Assert.True(result.Ok, result.Detail);
        Assert.NotNull(lines);
        Assert.All(lines!, line => Assert.Equal((false, ClassFolderText.HandOverRunningHere), (line.Ok, line.Detail)));
        Assert.False(lab.OldSync.Lesson.IsOn);
        Assert.All(rig.Pcs, pc => Assert.Equal(NewHome, pc.Agent.CurrentHome?.Name));
        Assert.All(rig.Pcs, pc => Assert.False(pc.Agent.Current.IsOn));
        Assert.True(File.Exists(lab.NewPath("Vídeos", "grande-39.bin")));
        lab.AssertNoStaging();
    }

    // (i) A PC at the home's address with another certificate than the one the
    // room knows for the home is told nothing and gives nothing.
    [Fact]
    public async Task APcWithAnotherCertificate_IsNotTheHome()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        var before = lab.Snapshot();

        var result = await lab.Taker.TakeOverAsync(lab.Home with { Thumbprint = rig.OtherMaster.Thumbprint });

        Assert.False(result.Ok);
        Assert.Equal(ClassFolderText.TakeOverFailed(OldHome, ClassFolderText.HomeOtherIdentity(OldHome)), result.Detail);
        lab.AssertUnchanged(before);
    }

    private sealed partial class HandOverLab
    {
        // 40 files of two chunks and a bit in Vídeos: a copy that takes long enough to stop in the middle.
        public void FillTheOldHomeWithBigFiles()
        {
            var big = new byte[FileTransfer.ChunkSize * 2 + 10];
            for (var number = 0; number < 40; number++)
            {
                _rig.WriteInStore($"grande-{number:00}.bin", big, Earlier, "Vídeos");
            }
        }

        // Every file under the new home's folder, with its size.
        public List<(string Path, long Length)> NewFiles() =>
            Directory.Exists(NewRoot)
                ? Directory.GetFiles(NewRoot, "*", SearchOption.AllDirectories)
                    .Select(path => (Path.GetRelativePath(NewRoot, path), new FileInfo(path).Length)).Order().ToList()
                : new List<(string Path, long Length)>();
    }
}
