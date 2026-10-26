using Midview.Master;
using Midview.Protocol;
using Xunit;

// ThumbnailPoller and an Agent still in a lesson this PC knows is over
// (ClassFolderSync.EndOfLessonOver): an Agent takes only the end of the lesson
// it follows, so that lesson is ended there before this PC's own lesson, or
// its claim, can reach it. Against the rig's real Agent and Session, with this
// PC's real sync and store.
public partial class ThumbnailPollerTests
{
    // One teacher PC: a PC off since a lesson this PC ran comes back still in
    // it, having missed its end and the whole next lesson. The poller hands it
    // that next lesson's end, as before two teacher PCs, and the PC is out of
    // the old lesson: its downloads go to the student's own folder again.
    [Fact]
    public async Task APcBackInALessonThisPcRan_GetsTheLastLessonsEnd()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        var pc = rig.Pcs[0];
        ClaimFor(rig, pc, rig.Master, ClassSyncRig.MachineName, HomeGeneration);
        var now = new DateTimeOffset(rig.Now).ToUnixTimeSeconds();
        var missed = new ClassLesson(now - 600, Guid.NewGuid(), ClassSyncRig.Turma, now - 600, Ended: false);
        Assert.True(ClassFolderClient.SetLesson(pc.Machine, rig.Master, missed, rig.AgentPort).Succeeded);
        rig.Store.RememberLesson(missed.LessonId, ClassSyncRig.Turma);
        var last = new ClassLesson(now - 60, Guid.NewGuid(), "3º B", now - 300, Ended: true);
        rig.Store.RememberLesson(last.LessonId, "3º B");
        ClassLessonFileMaster.Write(last, rig.LessonPath);
        var sync = rig.NewSync();
        Assert.Equal(last, sync.Lesson);
        using var poller = new ThumbnailPoller(
            pc.Machine, rig.Master, rig.SessionPort, rig.AgentPort, classLesson: () => sync.Lesson,
            classLessonOver: sync.EndOfLessonOver);

        poller.Start();

        await WaitUntil(() => pc.Agent.Current == last);
    }

    // After a take-over: a PC that was off for the old home's last end comes
    // back still in that lesson, which this PC's lessons map has from the
    // copy of the old home's store, with this PC's lesson on. Its Agent refuses
    // a take-over while in a lesson of its home, and this PC's lesson as not
    // the home's: the poller ends the old lesson there first, then takes the
    // PC back and hands it this PC's lesson. What its student saved for the
    // old lesson is set aside, kept, and comes into its class at the next round.
    [Fact]
    public async Task AStragglerInALessonOfTheCopiedMap_IsEndedThere_ClaimedAndInThisPcsLesson()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        var pc = rig.Pcs[0];
        ClaimFor(rig, pc, rig.OtherMaster, "PROF-2", HomeGeneration - 1_000); // the old home
        var now = new DateTimeOffset(rig.Now).ToUnixTimeSeconds();
        var old = new ClassLesson(now - 600, Guid.NewGuid(), ClassSyncRig.Turma, now - 600, Ended: false);
        SetLessonOn(rig, pc, old);
        pc.Follow();
        pc.Write("tarde.txt", new byte[] { 4, 2 }, new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc));
        rig.Store.RememberLesson(old.LessonId, ClassSyncRig.Turma); // the lessons map came with the copy
        ClassHomeFileMaster.Write(HomeGeneration, rig.HomePath);
        var sync = rig.NewSync();
        await sync.StartAsync("3º B");
        Assert.True(sync.Lesson.IsOn);
        Assert.Equal(old, pc.Agent.Current);
        using var poller = new ThumbnailPoller(
            pc.Machine, rig.Master, rig.SessionPort, rig.AgentPort, classLesson: () => sync.Lesson, classHomeClaim: HomeClaim,
            classLessonOver: sync.EndOfLessonOver);

        poller.Start();

        await WaitUntil(() => pc.Agent.Current == sync.Lesson);
        Assert.Equal((ClassSyncRig.MachineName, HomeGeneration), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation));
        pc.Follow();
        Assert.Contains(pc.Folder.List().Entries, entry => entry.LessonId == old.LessonId && entry.Path == "tarde.txt");
        Assert.Empty(pc.Bin.FileNames());
        await sync.RunRoundAsync(pc.Machine, CancellationToken.None);
        Assert.Equal(new byte[] { 4, 2 }, File.ReadAllBytes(rig.StorePath("tarde.txt")));
    }

    // Only a lesson this PC (or a home before it) ran is ended: one it only
    // heard of from the Agents, or never knew, is another PC's, which may
    // still be on there; and this PC's own lesson is never "over".
    [Fact]
    public void OnlyALessonThisPcRan_AndNotTheOneOnHere_IsOver()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        var now = new DateTimeOffset(rig.Now).ToUnixTimeSeconds();
        ClassLesson LessonOn(string turma) => new(now - 600, Guid.NewGuid(), turma, now - 600, Ended: false);
        var ran = LessonOn(ClassSyncRig.Turma);
        var heard = LessonOn(ClassSyncRig.Turma);
        var unknown = LessonOn(ClassSyncRig.Turma);
        rig.Store.RememberLesson(ran.LessonId, ClassSyncRig.Turma);
        rig.Store.RememberLessonHeardOf(heard.LessonId, ClassSyncRig.Turma);
        var here = new ClassLesson(now, Guid.NewGuid(), "3º B", now, Ended: false);
        rig.Store.RememberLesson(here.LessonId, "3º B");
        ClassLessonFileMaster.Write(here, rig.LessonPath);
        var sync = rig.NewSync();

        Assert.Equal(ran with { SavedAt = ran.SavedAt + 1, Ended = true }, sync.EndOfLessonOver(ran));
        Assert.Null(sync.EndOfLessonOver(ran with { Ended = true }));
        Assert.Null(sync.EndOfLessonOver(heard));
        Assert.Null(sync.EndOfLessonOver(unknown));
        Assert.Null(sync.EndOfLessonOver(here));
    }
}
