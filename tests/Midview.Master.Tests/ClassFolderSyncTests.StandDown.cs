using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderSync.StandDownForAnotherHomeAsync: what ClassFolderRole does once
// the room confirmed another PC as the home (stand down only on a
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

    // The folders stay with this PC until the lesson the room follows ends.
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
    // its end goes to the Agents too, and those in another lesson don't take it.
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
            Assert.All(rig.Pcs, pc => Assert.Equal(newest, pc.Agent.Current)); // the end of another lesson: not taken
        }
        finally
        {
            reopened.Dispose();
        }
    }

    // A split room: PC1 and PC2 ended this PC's lesson and follow PROF-2's
    // now; PC3 is still in this PC's lesson, which the room left behind.
    // Confirmed remote, this PC ends it on the Agents too, then here: PC3 is
    // out of it at once (its student's downloads no longer go into the
    // lesson's folder until the day ends), and PC1 and PC2 stay in PROF-2's.
    [Fact]
    public async Task ALessonTheRoomLeftBehind_IsEndedOnThePcsStillInIt()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        var lesson = sync.Lesson;
        var moved = rig.Pcs.Take(2).ToList();
        var pc3 = rig.Pcs[2];
        foreach (var pc in moved)
        {
            Assert.True(ClassFolderClient.SetLesson(
                pc.Machine, rig.OtherMaster, lesson with { SavedAt = lesson.SavedAt + 5, Ended = true }, rig.AgentPort).Succeeded);
        }
        var newest = StartAnotherHomesLesson(rig, lesson.SavedAt + 10, moved);
        pc3.Follow();
        pc3.Write("ainda.txt", Bytes(10, 1), Utc(1));

        Assert.Null(await sync.StandDownForAnotherHomeAsync());

        Assert.Equal(ClassLesson.None(0), sync.Lesson);
        Assert.Equal(lesson.LessonId, pc3.Agent.Current.LessonId);
        Assert.False(pc3.Agent.Current.IsOn);
        Assert.All(moved, pc => Assert.Equal(newest, pc.Agent.Current));
        Assert.False(rig.Store.IsCollected(lesson.LessonId));
        // What PC3's student saved is set aside, kept on that PC.
        pc3.Follow();
        Assert.Contains(pc3.Folder.List().Entries, entry => entry.LessonId == lesson.LessonId && entry.Path == "ainda.txt");
        Assert.Empty(pc3.Bin.FileNames());
    }

    // The same split, with PROF-2's lesson saved in the very second of this
    // PC's (two starts at once): the room still left this PC's behind, which
    // is ended on PC3 (one second after it) and here, rather than held until
    // the day ends. PC3 then takes PROF-2's lesson once PROF-2 claims it.
    [Fact]
    public async Task ALessonTheRoomLeftBehindForOneOfTheSameSecond_IsEndedToo()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var pc3 = rig.Pcs[2];
        var visible = new List<KnownMachine> { pc3.Machine };
        var sync = new ClassFolderSync(
            rig.Store, rig.Master, () => visible, machine => !rig.Offline.ContainsKey(machine.Name), () => rig.Now,
            ClassSyncRig.MachineName, agentPort: rig.AgentPort, sessionPort: rig.SessionPort, interval: TimeSpan.FromHours(1),
            lessonPath: rig.LessonPath, homePath: rig.HomePath);
        try
        {
            await sync.StartAsync(Turma);
            var lesson = sync.Lesson;
            var theirs = StartAnotherHomesLesson(rig, lesson.SavedAt, rig.Pcs.Take(2));
            visible = rig.Machines.ToList();

            Assert.Null(await sync.StandDownForAnotherHomeAsync());

            Assert.Equal(ClassLesson.None(0), sync.Lesson);
            Assert.Equal(lesson with { SavedAt = lesson.SavedAt + 1, Ended = true }, pc3.Agent.Current);
            Assert.All(rig.Pcs.Take(2), pc => Assert.Equal(theirs, pc.Agent.Current));
            StartAnotherHomesLesson(rig, theirs.SavedAt, new[] { pc3 }, theirs);
        }
        finally
        {
            sync.Dispose();
        }
    }

    // Another lesson saved in that second but ended (PROF-2's, over by then)
    // doesn't leave this PC's behind: PC3 still follows it, so it stays on.
    [Fact]
    public async Task AnotherLessonsEndOfTheSameSecond_DoesNotLeaveThisPcsLessonBehind()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var pc3 = rig.Pcs[2];
        var visible = new List<KnownMachine> { pc3.Machine };
        var sync = new ClassFolderSync(
            rig.Store, rig.Master, () => visible, machine => !rig.Offline.ContainsKey(machine.Name), () => rig.Now,
            ClassSyncRig.MachineName, agentPort: rig.AgentPort, sessionPort: rig.SessionPort, interval: TimeSpan.FromHours(1),
            lessonPath: rig.LessonPath, homePath: rig.HomePath);
        try
        {
            await sync.StartAsync(Turma);
            var lesson = sync.Lesson;
            var theirs = StartAnotherHomesLesson(rig, lesson.SavedAt - 5, rig.Pcs.Take(2));
            foreach (var pc in rig.Pcs.Take(2))
            {
                Assert.True(ClassFolderClient.SetLesson(
                    pc.Machine, rig.OtherMaster, theirs with { SavedAt = lesson.SavedAt, Ended = true }, rig.AgentPort).Succeeded);
            }
            visible = rig.Machines.ToList();

            Assert.Equal(lesson, await sync.StandDownForAnotherHomeAsync());

            Assert.Equal(lesson, sync.Lesson);
            Assert.Equal(lesson, pc3.Agent.Current);
        }
        finally
        {
            sync.Dispose();
        }
    }

    // PROF-2 takes the folders over on those PCs (every one by default) and starts 3º B there, saved at savedAt.
    // lesson: that one, already started on other PCs.
    private static ClassLesson StartAnotherHomesLesson(
        ClassSyncRig rig, long savedAt, IEnumerable<StudentPc>? pcs = null, ClassLesson? lesson = null)
    {
        lesson ??= new ClassLesson(savedAt, Guid.NewGuid(), "3º B", savedAt, Ended: false);
        foreach (var pc in pcs ?? rig.Pcs)
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
