using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderSync.StandDownForAnotherHomeAsync: what ClassFolderRole does once
// the room confirmed another PC as the home (Task 7: stand down only on a
// confirmed flip, never while a lesson of this PC's is on that the room still
// follows; a lesson the room left behind is ended here first, so it doesn't
// keep this PC from taking the folders over until the day ends).
public sealed partial class ClassFolderSyncTests
{
    [Fact]
    public async Task WithNoLessonOn_ItStandsDown()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        await sync.EndAsync(EndChoice.Keep);
        var ended = sync.Lesson;

        Assert.Null(await sync.StandDownForAnotherHomeAsync());

        Assert.Equal(ClassLesson.None(0), sync.Lesson);
        Assert.Equal(ClassLesson.None(0), ClassLessonFile.Read(rig.LessonPath));
        Assert.All(rig.Pcs, pc => Assert.Equal(ended, pc.Agent.Current)); // nothing sent
    }

    // Ruling 17: the folders stay with this PC until the lesson the room follows ends.
    [Fact]
    public async Task ALessonTheRoomStillFollows_StaysOn_AndIsTheAnswer()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        var lesson = sync.Lesson;

        Assert.Equal(lesson, await sync.StandDownForAnotherHomeAsync());

        Assert.Equal(lesson, sync.Lesson);
        Assert.Equal(lesson, ClassLessonFile.Read(rig.LessonPath));
        Assert.All(rig.Pcs, pc => Assert.Equal(lesson, pc.Agent.Current));
    }

    // With no Agent answering, whether the room left it behind can't be told: it stays on.
    [Fact]
    public async Task ALessonNoAgentSaysAnythingAbout_StaysOn()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        var lesson = sync.Lesson;
        foreach (var pc in rig.Pcs)
        {
            rig.Offline[pc.Name] = true;
        }

        Assert.Equal(lesson, await sync.StandDownForAnotherHomeAsync());
        Assert.Equal(lesson, sync.Lesson);
    }

    // This PC's Master was closed with its lesson on; a colleague's PC ended it on the
    // Agents, took the folders over and started a lesson of its own. Opened again, this
    // PC still has its lesson on (the Agents' newest is another one: nothing to adopt).
    // Confirmed remote, it ends it here, as one nobody collected, and stands down;
    // nothing goes to the Agents.
    [Fact]
    public async Task ALessonTheRoomLeftBehind_IsEndedHereUncollected_ThenItStandsDown()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var closed = rig.NewSync();
        await closed.StartAsync(Turma);
        var lesson = closed.Lesson;
        closed.Dispose();
        EndOnTheAgents(rig, lesson with { SavedAt = lesson.SavedAt + 5, Ended = true });
        var newest = StartAnotherHomesLesson(rig, lesson.SavedAt + 10);
        var log = new FreeingLog();
        var reopened = new ClassFolderSync(
            rig.Store, rig.Master, () => rig.Machines, machine => !rig.Offline.ContainsKey(machine.Name), () => rig.Now,
            ClassSyncRig.MachineName, agentPort: rig.AgentPort, sessionPort: rig.SessionPort, interval: TimeSpan.FromHours(1),
            lessonPath: rig.LessonPath, homePath: rig.HomePath, log: log.Add);
        try
        {
            await reopened.AdoptAgentsEndAsync();
            Assert.Equal(lesson, reopened.Lesson);
            var changes = new List<ClassLesson>();
            reopened.LessonChanged += changes.Add;

            var on = await reopened.StandDownForAnotherHomeAsync();

            Assert.Null(on);
            Assert.Equal(ClassLesson.None(0), reopened.Lesson);
            Assert.Equal(ClassLesson.None(0), ClassLessonFile.Read(rig.LessonPath));
            Assert.Contains(ClassLesson.None(0), changes);
            Assert.False(rig.Store.IsCollected(lesson.LessonId));
            Assert.True(log.Has($"The lesson of {Turma} ended here"));
            Assert.All(rig.Pcs, pc => Assert.Equal(newest, pc.Agent.Current)); // nothing sent
        }
        finally
        {
            reopened.Dispose();
        }
    }

    // PROF-2 takes the folders over on every Agent and starts 3º B there, saved at savedAt.
    private static ClassLesson StartAnotherHomesLesson(ClassSyncRig rig, long savedAt)
    {
        var lesson = new ClassLesson(savedAt, Guid.NewGuid(), "3º B", savedAt, Ended: false);
        foreach (var pc in rig.Pcs)
        {
            var claim = new ClassHomeClaim(OtherPc, ClassFolderRemote.Port, TakeOver: true, rig.NowTicks + TimeSpan.TicksPerHour);
            var (claimed, why, _) = ClassFolderClient.ClaimHome(pc.Machine, rig.OtherMaster, claim, rig.AgentPort);
            Assert.True(claimed, why);
            var (ok, detail) = ClassFolderClient.SetLesson(pc.Machine, rig.OtherMaster, lesson, rig.AgentPort);
            Assert.True(ok, detail);
        }
        return lesson;
    }
}
