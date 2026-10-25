using System.Net;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderHandOver: the claim on the room's Agents after the copy is in
// place. An answer lost on the way back (the Agent may have taken the claim)
// is found out by asking the Agents again before anything is put back; a
// claim some PCs take and others don't is done; the old home lets go only
// with no lesson on; and a take-over that breaks off after the swap says how
// far it got (never "nada mudou").
public sealed partial class ClassFolderHandOverTests
{
    // An Agent that takes the claim (or not) and whose answer never comes back
    // (its lesson lock held it past the read timeout, a network blip): this PC
    // asks the Agents which home they keep before putting anything back.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AClaimWhoseAnswersAreLost_IsDone_WhenTheAgentsTookIt(bool taken)
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = rig.Pcs[0].Agent.CurrentHome!.Generation;
        lab.FillTheOldHome();
        lab.GiveTheNewHomeAClassOfItsOwn();
        var before = lab.Snapshot();
        lab.Taker.SendClaim = (machine, claim) =>
        {
            if (taken)
            {
                ClassFolderClient.ClaimHome(machine, rig.OtherMaster, claim, rig.AgentPort);
            }
            throw new IOException("the answer was lost");
        };

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        if (taken)
        {
            Assert.True(result.Ok, result.Detail);
            Assert.All(rig.Pcs, pc => Assert.Equal((NewHome, generation + 1), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));
            Assert.Equal(generation + 1, ClassHomeFileMaster.Read(lab.NewHomePath));
            Assert.Equal(new[] { Turma, "3º B", "Vídeos" }, lab.NewStore.List().Select(summary => summary.Turma));
            Assert.True(Directory.Exists(Path.Combine(rig.Root, "Turmas (passadas para PROF-2)")));
            Assert.Equal(ClassLesson.None(0), lab.OldSync.Lesson);
        }
        else
        {
            Assert.False(result.Ok);
            Assert.Equal(ClassFolderText.TakeOverFailed(OldHome, ClassFolderText.NoPcTookIt), result.Detail);
            lab.AssertUnchanged(before);
        }
    }

    // No answer says yes: PC1 refuses (it still follows a lesson), PC2 took
    // the claim and its answer was lost, PC3 is off. The Agents asked again
    // say PC2 keeps this PC: the take-over is done, its generation kept, and
    // the others are taken back at this PC's next start.
    [Fact]
    public async Task AClaimOnlyALostAnswerTook_IsDone()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = rig.Pcs[0].Agent.CurrentHome!.Generation;
        lab.FillTheOldHome();
        lab.PutALessonOnTheAgentOf(rig.Pcs[0]);
        rig.Offline["PC3"] = true;
        lab.Taker.SendClaim = (machine, claim) =>
        {
            var answer = ClassFolderClient.ClaimHome(machine, rig.OtherMaster, claim, rig.AgentPort);
            return machine.Name == "PC2" ? throw new IOException("the answer was lost") : answer;
        };

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.True(result.Ok, result.Detail);
        Assert.Equal(OldHome, rig.Pcs[0].Agent.CurrentHome!.Name);
        Assert.Equal((NewHome, generation + 1), (rig.Pcs[1].Agent.CurrentHome!.Name, rig.Pcs[1].Agent.CurrentHome!.Generation));
        Assert.Equal(OldHome, rig.Pcs[2].Agent.CurrentHome!.Name);
        Assert.Equal(generation + 1, ClassHomeFileMaster.Read(lab.NewHomePath));
        Assert.Equal(new[] { Turma, "3º B", "Vídeos" }, lab.NewStore.List().Select(summary => summary.Turma));
        Assert.True(Directory.Exists(Path.Combine(rig.Root, "Turmas (passadas para PROF-2)")));
    }

    // Ruling 18: Agents busy with their students' folders refuse the claim as
    // busy, and never apply it later: not taken, so everything is put back,
    // and the teacher is told to try again.
    [Fact]
    public async Task AClaimTheBusyAgentsRefuse_IsNotTaken_AndEverythingIsPutBack()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        lab.GiveTheNewHomeAClassOfItsOwn();
        var before = lab.Snapshot();
        HandOverResult result;
        using (var busy = new BusyAgents(rig.Pcs))
        {
            result = await lab.Taker.TakeOverAsync(lab.Home);
        }

        Assert.False(result.Ok);
        Assert.Equal(ClassFolderText.TakeOverFailed(OldHome, ClassFolderText.PcsBusy), result.Detail);
        lab.AssertUnchanged(before);
        Assert.Null(ClassHomeFileMaster.Read(lab.NewHomePath));
    }

    // Ruling 18, the start: a busy Agent is one that didn't answer the claim,
    // never another home; the start goes on, and that PC gets the lesson once
    // it is free.
    [Fact]
    public async Task AStartWithABusyAgent_GoesOn_AsWithOneThatDidntAnswer()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var logged = new List<string>();
        using var sync = new ClassFolderSync(
            rig.Store, rig.Master, () => rig.Machines, machine => !rig.Offline.ContainsKey(machine.Name), () => rig.Now,
            ClassSyncRig.MachineName, agentPort: rig.AgentPort, sessionPort: rig.SessionPort, interval: TimeSpan.FromHours(1),
            lessonPath: rig.LessonPath, homePath: rig.HomePath, log: message =>
            {
                lock (logged)
                {
                    logged.Add(message);
                }
            });
        await sync.StartAsync(Turma);
        await sync.EndAsync(EndChoice.Keep);
        var generation = ClassHomeFileMaster.Read(rig.HomePath);
        var homes = rig.Pcs.Select(pc => pc.Agent.CurrentHome).ToList();
        IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)> started;
        using (var busy = new BusyAgents(rig.Pcs.Take(1)))
        {
            // Free again well after its claim was refused, while its lesson waits for the lock.
            busy.FreeAfter(StudentPc.ClaimWait * 4);
            started = await sync.StartAsync("3º B");
        }

        Assert.All(started, line => Assert.True(line.Ok, $"{line.Machine.Name}: {line.Detail}"));
        Assert.True(sync.Lesson.IsOn);
        Assert.Equal("3º B", sync.Lesson.Turma);
        Assert.All(rig.Pcs, pc => Assert.Equal(sync.Lesson, pc.Agent.Current));
        Assert.Equal(generation, ClassHomeFileMaster.Read(rig.HomePath));
        Assert.Equal(homes, rig.Pcs.Select(pc => pc.Agent.CurrentHome));
        lock (logged)
        {
            Assert.Contains(logged, message => message.Contains($"PC1 didn't take this PC as the class folders' home: {ClassHomeClaim.BusyText}"));
        }
    }

    // Ruling 18 (5): what couldn't be put back is said, with where this PC's
    // own classes are, never "nada mudou".
    [Fact]
    public async Task WhatCouldntBePutBack_IsSaid_WithWhereThisPcsClassesAre()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        lab.GiveTheNewHomeAClassOfItsOwn();
        FileStream? held = null;
        lab.Taker.SendClaim = (_, _) =>
        {
            // A file of the copy, now in place, open in another program: its folder can't move back.
            held ??= new FileStream(lab.NewPath(Turma, "a.txt"), FileMode.Open, FileAccess.Read, FileShare.Read);
            return (false, ClassFolderText.EndLessonFirst, false);
        };
        HandOverResult result;
        try
        {
            result = await lab.Taker.TakeOverAsync(lab.Home);
        }
        finally
        {
            held?.Dispose();
        }

        Assert.False(result.Ok);
        Assert.DoesNotContain("nada mudou", result.Detail);
        Assert.Equal(
            ClassFolderText.TakeOverNotPutBack(OldHome, ClassFolderText.EndLessonFirst, "Turmas (antes de 20-10-2026 1330)"),
            result.Detail);
        Assert.True(File.Exists(Path.Combine(lab.NewRoot, "Turmas (antes de 20-10-2026 1330)", "Antiga", "velho.txt")));
        // It stopped at the first thing that couldn't move back: the copy stays whole where it went.
        Assert.True(File.Exists(lab.NewPath(Turma, "a.txt")));
        Assert.Equal(OldHome, rig.Pcs[0].Agent.CurrentHome!.Name);
        Assert.Null(ClassHomeFileMaster.Read(lab.NewHomePath));
    }

    // Ruling 17 (b): the old home with a lesson on (some PCs took the claim,
    // others still follow its lesson) doesn't let go of the folders yet, and says so.
    [Fact]
    public async Task TheOldHomeWithALessonOn_DoesntLetGoYet_AndSaysSo()
    {
        using var rig = new ClassSyncRig(pcs: 2, homes: 2);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = rig.Pcs[0].Agent.CurrentHome!.Generation;
        await lab.OldSync.StartAsync(Turma);
        var lesson = lab.OldSync.Lesson;
        // PC1 heard the lesson end (the old home's poller, say), then took the take-over.
        Assert.True(ClassFolderClient.SetLesson(
            rig.Pcs[0].Machine, rig.Master, lesson with { SavedAt = lesson.SavedAt + 1, Ended = true }, rig.AgentPort).Succeeded);
        Assert.True(ClassFolderClient.ClaimHome(
            rig.Pcs[0].Machine, rig.OtherMaster, new ClassHomeClaim(NewHome, ClassFolderRemote.Port, true, generation + 1), rig.AgentPort).Ok);

        var (ok, detail) = lab.SendHandedOver(NewHome);

        Assert.False(ok);
        Assert.Equal(ClassFolderText.LetsGoAfterTheLesson(OldHome, Turma), detail);
        Assert.Equal(lesson, lab.OldSync.Lesson);
        Assert.Equal(new[] { Turma, "3º B" }, rig.Store.List().Select(summary => summary.Turma));
        Assert.False(Directory.Exists(Path.Combine(rig.Root, "Turmas (passadas para PROF-2)")));
    }

    // Ruling 17 (g, h): the old home's copy is named after the new home as the
    // Agents recorded it, and the answer names the folder it went to.
    [Fact]
    public async Task TheOldHomesCopy_IsNamedAfterTheAgentsRecord()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = rig.Pcs[0].Agent.CurrentHome!.Generation;
        Assert.True(ClassFolderClient.ClaimHome(
            rig.Pcs[0].Machine, rig.OtherMaster, new ClassHomeClaim("Prof-2", ClassFolderRemote.Port, true, generation + 1), rig.AgentPort).Ok);

        var (ok, detail) = lab.SendHandedOver("PROF-2");

        Assert.True(ok, detail);
        Assert.Equal("Turmas (passadas para Prof-2)", detail);
        Assert.True(Directory.Exists(Path.Combine(rig.Root, "Turmas (passadas para Prof-2)")));
        Assert.Equal(ClassLesson.None(0), lab.OldSync.Lesson);
    }

    // Ruling 17 (g): a home an Agent recorded with no name (a Master from
    // before two teacher PCs started a lesson there) is never taken for the
    // sender, whatever name it gives, even on a PC with no generation of its own.
    [Fact]
    public void AHomeRecordedWithNoName_IsNeverTheSender()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig);
        var started = new ClassLesson(
            new DateTimeOffset(rig.Now).ToUnixTimeSeconds(), Guid.NewGuid(), Turma, new DateTimeOffset(rig.Now).ToUnixTimeSeconds(), Ended: false);
        Assert.True(ClassFolderClient.SetLesson(rig.Pcs[0].Machine, rig.OtherMaster, started, rig.AgentPort).Succeeded);
        Assert.Equal(string.Empty, rig.Pcs[0].Agent.CurrentHome!.Name);
        Assert.True(ClassFolderClient.SetLesson(
            rig.Pcs[0].Machine, rig.OtherMaster, started with { SavedAt = started.SavedAt + 1, Ended = true }, rig.AgentPort).Succeeded);

        var (ok, _) = lab.SendHandedOver(NewHome);

        Assert.False(ok);
        Assert.Equal(new[] { Turma, "3º B" }, rig.Store.List().Select(summary => summary.Turma));
        Assert.False(Directory.EnumerateDirectories(rig.Root, "Turmas (*").Any());
    }

    // Ruling 17 (e): a take-over that breaks off after the copy went in place
    // says how far it got, never that nothing changed.
    [Theory]
    [InlineData("didn't take this PC")] // while the Agents are claimed
    [InlineData("This PC took the class folders over")] // once they took it
    public async Task ATakeOverThatBreaksOffAfterTheSwap_SaysHowFarItGot(string breaksAt)
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        lab.PutALessonOnTheAgentOf(rig.Pcs[0]);
        lab.TakerLog = message =>
        {
            if (message.Contains(breaksAt, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("a mistake in the code");
            }
        };

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.DoesNotContain("nada mudou", result.Detail);
        var reason = ErrorText.Describe(new InvalidOperationException("a mistake in the code"));
        Assert.Equal(
            breaksAt.StartsWith("This PC", StringComparison.Ordinal)
                ? ClassFolderText.TakeOverBrokeOffTellingTheOldHome(OldHome, reason)
                : ClassFolderText.TakeOverBrokeOffClaiming(OldHome, reason),
            result.Detail);
        Assert.Equal(new[] { Turma, "3º B", "Vídeos" }, lab.NewStore.List().Select(summary => summary.Turma));
        lab.AssertNoStaging();
    }

    // The PCs' Agents busy with their students' folders (holding their
    // lesson locks) until disposed, or until FreeAfter's time.
    private sealed class BusyAgents : IDisposable
    {
        private readonly List<StudentPc> _pcs;
        private readonly ManualResetEventSlim _free = new();
        private readonly List<Task> _looking = new();

        public BusyAgents(IEnumerable<StudentPc> pcs)
        {
            _pcs = pcs.ToList();
            foreach (var pc in _pcs)
            {
                using var holding = new ManualResetEventSlim();
                pc.WhileLooking = () =>
                {
                    holding.Set();
                    _free.Wait(TimeSpan.FromSeconds(60));
                };
                _looking.Add(OwnThread(pc.Agent.Reconcile));
                Assert.True(holding.Wait(TimeSpan.FromSeconds(10)));
                pc.WhileLooking = null;
            }
        }

        public void FreeAfter(TimeSpan wait) => _ = Task.Delay(wait).ContinueWith(_ => _free.Set(), TaskScheduler.Default);

        public void Dispose()
        {
            _free.Set();
            Assert.True(Task.WaitAll(_looking.ToArray(), TimeSpan.FromSeconds(10)));
            _free.Dispose();
        }
    }

    private sealed partial class HandOverLab
    {
        // The machine's Agent follows a lesson the home's Master doesn't know
        // of: it refuses a take-over.
        public void PutALessonOnTheAgentOf(StudentPc pc)
        {
            var on = new ClassLesson(
                pc.Agent.Current.SavedAt + 10, Guid.NewGuid(), "3º B", new DateTimeOffset(_rig.Now).ToUnixTimeSeconds(), Ended: false);
            Assert.True(ClassFolderClient.SetLesson(pc.Machine, _rig.Master, on, _rig.AgentPort).Succeeded);
        }
    }
}
