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
        using var cancel = new CancellationTokenSource();
        var progress = new Inline(step =>
        {
            if (step.Bytes > 0)
            {
                cancel.Cancel();
            }
        });

        var result = await lab.Taker.TakeOverAsync(lab.Home, progress, cancel.Token);

        Assert.True(cancel.IsCancellationRequested);
        Assert.False(result.Ok);
        Assert.Equal(ClassFolderText.TakeOverFailed(OldHome, ClassFolderText.CopyCancelled), result.Detail);
        lab.AssertUnchanged(before);
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

    // (i) A lesson started on the home while the copy runs: the home refuses
    // before its last answer, so the Agents are never claimed and nothing
    // changes on this PC. A home with a lessons map sees it change (the new
    // lesson goes in it) when it comes to send it; one with none yet finds the
    // lesson on at its second look, before the last answer.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ALessonStartedOnTheHomeDuringTheCopy_StopsIt_BeforeTheAgentsAreClaimed(bool lessonsMap)
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
        var homes = lab.Snapshot().Homes;
        var newFiles = lab.NewFiles();
        var logged = new List<string>();
        lab.TakerLog = message =>
        {
            lock (logged)
            {
                logged.Add(message);
            }
        };
        var started = 0;
        var progress = new Inline(step =>
        {
            if (step.Files > 0 && Interlocked.Exchange(ref started, 1) == 0)
            {
                lab.OldSync.StartAsync("3º B").Wait();
            }
        });

        var result = await lab.Taker.TakeOverAsync(lab.Home, progress);

        Assert.Equal(1, started);
        Assert.False(result.Ok);
        Assert.Equal(ClassFolderText.TakeOverFailed(OldHome, ClassFolderText.EndLessonFirst), result.Detail);
        Assert.True(lab.OldSync.Lesson.IsOn);
        Assert.Equal(homes, lab.Snapshot().Homes);
        Assert.Equal(newFiles, lab.NewFiles());
        Assert.DoesNotContain(logged, message => message.Contains("didn't take this PC", StringComparison.Ordinal));
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
