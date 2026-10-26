using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderSync ending a lesson it holds for the room while another teacher
// PC keeps the class folders (a split room): the end goes only to the PCs
// still in that lesson, saved one second after it, so it never outranks the
// other home's lesson, started since. Four PCs: PC1 and PC2 follow PROF-2's
// lesson, PC3 is still in this PC's, and PC4 was off when it started (no lesson).
public sealed partial class ClassFolderSyncTests
{
    // The room left the lesson behind; this PC, confirmed remote a minute
    // after PROF-2's start, ends it. PC3, once PROF-2 takes it back, joins
    // PROF-2's lesson; PC4 is never sent the end.
    [Fact]
    public async Task ALeftBehindLessonsEnd_ReachesOnlyItsPcs_AndNeverOutranksTheOtherHomesLesson()
    {
        using var rig = new ClassSyncRig(pcs: 4, homes: 4);
        var split = await SplitRoomAsync(rig);
        try
        {
            Assert.Null(await split.Sync.StandDownForAnotherHomeAsync());

            Assert.Equal(split.Lesson with { SavedAt = split.Lesson.SavedAt + 1, Ended = true }, split.Pc3.Agent.Current);
            Assert.Equal(ClassLesson.None(0), split.Pc4.Agent.Current);
            Assert.All(rig.Pcs.Take(2), pc => Assert.Equal(split.Theirs, pc.Agent.Current));
            TakeBackForTheOtherHome(rig, split.Pc3, split.Theirs);
        }
        finally
        {
            split.Sync.Dispose();
        }
    }

    // The teacher ends the lesson this PC holds ("Encerrar a aula"), a minute
    // after PROF-2's start: the same, and each PC's line says where it was.
    [Fact]
    public async Task AHeldLessonsEnd_ReachesOnlyItsPcs_AndNeverOutranksTheOtherHomesLesson()
    {
        using var rig = new ClassSyncRig(pcs: 4, homes: 4);
        var split = await SplitRoomAsync(rig);
        try
        {
            var lines = await split.Sync.EndAsync(EndChoice.Keep);

            var ended = split.Lesson with { SavedAt = split.Lesson.SavedAt + 1, Ended = true };
            Assert.Equal(ended, split.Sync.Lesson);
            Assert.True(split.Sync.EndGoesOnlyToItsLesson);
            Assert.Equal(ended, split.Pc3.Agent.Current);
            Assert.Equal(ClassLesson.None(0), split.Pc4.Agent.Current);
            Assert.All(rig.Pcs.Take(2), pc => Assert.Equal(split.Theirs, pc.Agent.Current));
            Assert.Equal(
                new[] { (true, ClassFolderText.NotInThisLesson), (true, ClassFolderText.NotInThisLesson), (true, "aula encerrada"),
                    (true, ClassFolderText.NotInThisLesson) },
                rig.Pcs.Select(pc => lines.Single(line => line.Machine == pc.Machine)).Select(line => (line.Ok, line.Detail)));
            TakeBackForTheOtherHome(rig, split.Pc3, split.Theirs);
        }
        finally
        {
            split.Sync.Dispose();
        }
    }

    // A PC not in the held lesson but still in an older lesson this PC ran
    // (it was off for that lesson's end and the next start) isn't sent the
    // held end, and has that older lesson ended instead.
    [Fact]
    public async Task AHeldLessonsEnd_EndsAnOlderLessonOfThisPcWhereAPcIsStillInIt()
    {
        using var rig = new ClassSyncRig(pcs: 2, homes: 2);
        var (pc1, pc2) = (rig.Pcs[0], rig.Pcs[1]);
        var visible = new List<KnownMachine> { pc2.Machine };
        var sync = new ClassFolderSync(
            rig.Store, rig.Master, () => visible, machine => !rig.Offline.ContainsKey(machine.Name), () => rig.Now,
            ClassSyncRig.MachineName, agentPort: rig.AgentPort, sessionPort: rig.SessionPort, interval: TimeSpan.FromHours(1),
            lessonPath: rig.LessonPath, homePath: rig.HomePath);
        try
        {
            await sync.StartAsync("3º B");
            var older = sync.Lesson;
            visible = new List<KnownMachine> { pc1.Machine }; // PC2 off from here
            await sync.StartAsync(Turma);
            var lesson = sync.Lesson;
            Assert.True(ClassFolderClient.SetLesson(
                pc1.Machine, rig.OtherMaster, lesson with { SavedAt = lesson.SavedAt + 5, Ended = true }, rig.AgentPort).Succeeded);
            var theirs = StartAnotherHomesLesson(rig, lesson.SavedAt + 10, new[] { pc1 });
            rig.Now = rig.Now.AddMinutes(1);
            visible = rig.Machines.ToList(); // PC2 back, still in 3º B
            Assert.Equal(older, pc2.Agent.Current);

            Assert.Null(await sync.StandDownForAnotherHomeAsync());

            Assert.Equal(older with { SavedAt = older.SavedAt + 1, Ended = true }, pc2.Agent.Current);
            Assert.Equal(theirs, pc1.Agent.Current);
        }
        finally
        {
            sync.Dispose();
        }
    }

    // A PC whose home can't be read (here an Agent from before two teacher
    // PCs) but that follows a newer lesson of another PC: another PC may be
    // the home, so this PC's end is a held one, as when an Agent names it.
    // PC2, off meanwhile and with no lesson, isn't handed it by this PC's
    // poller once back, and takes PROF-2's lesson: an end saved now would
    // have made it refuse that lesson, saved earlier.
    [Fact]
    public async Task APcFollowingANewerLessonOfAnotherPc_MakesTheEndAHeldOne_EvenWithItsHomeUnread()
    {
        using var rig = new ClassSyncRig(pcs: 3, homes: 2);
        var (pc1, pc2, pc3) = (rig.Pcs[0], rig.Pcs[1], rig.Pcs[2]);
        var visible = new List<KnownMachine> { pc1.Machine, pc3.Machine };
        var sync = new ClassFolderSync(
            rig.Store, rig.Master, () => visible, machine => !rig.Offline.ContainsKey(machine.Name), () => rig.Now,
            ClassSyncRig.MachineName, agentPort: rig.AgentPort, sessionPort: rig.SessionPort, interval: TimeSpan.FromHours(1),
            lessonPath: rig.LessonPath, homePath: rig.HomePath);
        try
        {
            await sync.StartAsync(Turma);
            var lesson = sync.Lesson;
            var theirs = new ClassLesson(lesson.SavedAt + 10, Guid.NewGuid(), "3º B", lesson.StartedAt, Ended: false);
            Assert.True(ClassFolderClient.SetLesson(pc3.Machine, rig.OtherMaster, theirs, rig.AgentPort).Succeeded);
            rig.Now = rig.Now.AddMinutes(1);

            await sync.EndAsync(EndChoice.Keep);

            var ended = lesson with { SavedAt = lesson.SavedAt + 1, Ended = true };
            Assert.Equal(ended, sync.Lesson);
            Assert.True(sync.EndGoesOnlyToItsLesson);
            Assert.Equal(ended, pc1.Agent.Current);
            Assert.Equal(theirs, pc3.Agent.Current);

            visible = rig.Machines.ToList();
            var thumbnails = 0;
            using (var poller = new ThumbnailPoller(
                pc2.Machine, rig.Master, rig.SessionPort, rig.AgentPort, agentRetryInterval: TimeSpan.Zero,
                classLesson: () => sync.Lesson, classLessonHeldEnd: () => sync.EndGoesOnlyToItsLesson))
            {
                poller.ThumbnailReceived += _ => Interlocked.Increment(ref thumbnails);
                poller.Start();
                await WaitUntil(() => Volatile.Read(ref thumbnails) >= 2);
            }
            Assert.Equal(ClassLesson.None(0), pc2.Agent.Current);
            var (ok, detail) = ClassLessonOnAgents.Send(pc2.Machine, rig.OtherMaster, theirs, rig.AgentPort);
            Assert.True(ok, detail);
            Assert.Equal(theirs, pc2.Agent.Current);
        }
        finally
        {
            sync.Dispose();
        }
    }

    // One teacher PC: its end is saved now, goes to every PC, and is handed to all.
    [Fact]
    public async Task WithNoOtherHomeEverSeen_AnEndIsAsBefore()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        var lesson = sync.Lesson;
        rig.Now = rig.Now.AddMinutes(1);

        await sync.EndAsync(EndChoice.Keep);

        Assert.Equal(lesson.SavedAt + 60, sync.Lesson.SavedAt);
        Assert.False(sync.EndGoesOnlyToItsLesson);
        Assert.All(rig.Pcs, pc => Assert.Equal(sync.Lesson, pc.Agent.Current));
    }

    // PROF-1 (this PC) starts 3º A with PC4 off; PC1 and PC2 ended it with a
    // colleague's end and PROF-2 took them over and started 3º B there; PC3
    // missed all of that. A minute later PC4 is on, with no lesson, taken over
    // by PROF-2's poller (so this PC's last rounds can't start 3º A on it).
    private static async Task<SplitRoom> SplitRoomAsync(ClassSyncRig rig)
    {
        var visible = rig.Machines.Take(3).ToList();
        var sync = new ClassFolderSync(
            rig.Store, rig.Master, () => visible, machine => !rig.Offline.ContainsKey(machine.Name), () => rig.Now,
            ClassSyncRig.MachineName, agentPort: rig.AgentPort, sessionPort: rig.SessionPort, interval: TimeSpan.FromHours(1),
            lessonPath: rig.LessonPath, homePath: rig.HomePath);
        await sync.StartAsync(Turma);
        var lesson = sync.Lesson;
        var moved = rig.Pcs.Take(2).ToList();
        foreach (var pc in moved)
        {
            Assert.True(ClassFolderClient.SetLesson(
                pc.Machine, rig.OtherMaster, lesson with { SavedAt = lesson.SavedAt + 5, Ended = true }, rig.AgentPort).Succeeded);
        }
        var theirs = StartAnotherHomesLesson(rig, lesson.SavedAt + 10, moved);
        rig.Now = rig.Now.AddMinutes(1);
        var pc4 = rig.Pcs[3];
        var claim = new ClassHomeClaim(OtherPc, ClassFolderRemote.Port, TakeOver: true, rig.NowTicks + TimeSpan.TicksPerHour);
        var (claimed, why, _) = ClassFolderClient.ClaimHome(pc4.Machine, rig.OtherMaster, claim, rig.AgentPort);
        Assert.True(claimed, why);
        Assert.Equal(ClassLesson.None(0), pc4.Agent.Current);
        visible = rig.Machines.ToList();
        return new SplitRoom(sync, lesson, theirs, rig.Pcs[2], pc4);
    }

    // PROF-2's poller takes the straggler back and hands it PROF-2's lesson: it takes it.
    private static void TakeBackForTheOtherHome(ClassSyncRig rig, StudentPc pc, ClassLesson theirs)
    {
        var claim = new ClassHomeClaim(OtherPc, ClassFolderRemote.Port, TakeOver: true, rig.NowTicks + TimeSpan.TicksPerHour);
        var (claimed, why, _) = ClassFolderClient.ClaimHome(pc.Machine, rig.OtherMaster, claim, rig.AgentPort);
        Assert.True(claimed, why);
        var (ok, detail) = ClassLessonOnAgents.Send(pc.Machine, rig.OtherMaster, theirs, rig.AgentPort);
        Assert.True(ok, detail);
        Assert.Equal(theirs, pc.Agent.Current);
    }

    private sealed record SplitRoom(ClassFolderSync Sync, ClassLesson Lesson, ClassLesson Theirs, StudentPc Pc3, StudentPc Pc4);
}
