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

    // One PC still follows a lesson (it refuses), one's answer is lost after
    // it took the claim, one takes it: the take-over is done, and the PC that
    // refused is taken back at this PC's next start.
    [Fact]
    public async Task AClaimSomePcsTakeAndOthersDont_IsDone()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = rig.Pcs[0].Agent.CurrentHome!.Generation;
        lab.FillTheOldHome();
        lab.PutALessonOnTheAgentOf(rig.Pcs[0]);
        lab.Taker.SendClaim = (machine, claim) =>
        {
            var answer = ClassFolderClient.ClaimHome(machine, rig.OtherMaster, claim, rig.AgentPort);
            return machine.Name == "PC2" ? throw new IOException("the answer was lost") : answer;
        };

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.True(result.Ok, result.Detail);
        Assert.Equal(OldHome, rig.Pcs[0].Agent.CurrentHome!.Name);
        Assert.All(rig.Pcs.Skip(1), pc => Assert.Equal((NewHome, generation + 1), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));
        Assert.Equal(generation + 1, ClassHomeFileMaster.Read(lab.NewHomePath));
        Assert.Equal(new[] { Turma, "3º B", "Vídeos" }, lab.NewStore.List().Select(summary => summary.Turma));
        Assert.True(Directory.Exists(Path.Combine(rig.Root, "Turmas (passadas para PROF-2)")));
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
