using System.Net;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderHandOver: the claim on the room's Agents after the copy is in
// place (lost answers are in ClassFolderHandOverTests.Lost.cs): busy Agents
// take nothing; the old home lets go only with no lesson on, and names its
// copy as the Agents name the new home; and a take-over that breaks off after
// the swap says how far it got (never "nada mudou").
public sealed partial class ClassFolderHandOverTests
{
    // An Agent's lock wait, twice over, still fits in a Master's read timeout.
    [Fact]
    public void TheAgentsLockWait_FitsTwiceInTheMastersReadTimeout()
    {
        Assert.True(ClassHomeClaim.MaxLockWait * 2 <= TimeSpan.FromMilliseconds(CommandClient.TimeoutMs));
    }

    // Agents busy with their students' folders refuse the claim as
    // busy, and never apply it later: not taken, so everything is put back,
    // and the teacher is told to try again.
    [Fact]
    public async Task AClaimTheBusyAgentsRefuse_IsNotTaken_AndEverythingIsPutBack()
    {
        using var rig = new ClassSyncRig(homes: 3, claimWait: BusyAgents.ClaimWait);
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

    // Every Agent from before two teacher PCs: they answered, as old ones, so
    // the teacher is told they need the new Midview (not that none answered),
    // and everything is put back.
    [Fact]
    public async Task AClaimOnlyOldAgentsAnswer_SaysTheyNeedTheNewMidview_AndEverythingIsPutBack()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        var before = lab.Snapshot();
        lab.Taker.SendClaim = (_, _, _) => (false, string.Empty, true);

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.Equal((false, ClassFolderText.TakeOverFailed(OldHome, ClassFolderText.PcsNeedTheNewMidview)), (result.Ok, result.Detail));
        lab.AssertUnchanged(before);
        Assert.Equal(
            "Os PCs da sala estão com um Midview antigo, que não passa as pastas para outro computador: atualize o Midview neles.",
            ClassFolderText.PcsNeedTheNewMidview);
    }

    // What couldn't be put back is said, with where this PC's
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
        lab.Taker.SendClaim = (_, _, _) =>
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
            ClassFolderText.TakeOverNotPutBack(
                OldHome, ClassFolderText.EndLessonFirst, copyStayed: true, ownAside: "Turmas (antes de 20-10-2026 1330)"),
            result.Detail);
        Assert.True(File.Exists(Path.Combine(lab.NewRoot, "Turmas (antes de 20-10-2026 1330)", "Antiga", "velho.txt")));
        // It stopped at the first thing that couldn't move back: the copy stays whole where it went.
        Assert.True(File.Exists(lab.NewPath(Turma, "a.txt")));
        Assert.Equal(OldHome, rig.Pcs[0].Agent.CurrentHome!.Name);
        Assert.Null(ClassHomeFileMaster.Read(lab.NewHomePath));
    }

    // The old home with a lesson on (some PCs took the claim,
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

    // The old home's copy is named after the new home as the
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

    // A home an Agent recorded with no name (a Master from
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

    // A take-over that breaks off after the copy went in place
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
