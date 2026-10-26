using Midview.Master;
using Midview.Protocol;
using Xunit;

// Two teacher PCs claiming the room at the same moment (their first lessons
// started at once, say): whichever way their claims cross on the Agents, the
// room ends with one home and that home's lesson on every PC, and the other
// PC has no lesson on (no lesson that no Agent follows), its lines naming the
// home. The newer generation decides, and between equal ones (the rig's one
// clock) the computer name (PROF-2 over PROF-1). PROF-2 has its own store, as
// a second teacher PC does.
public sealed partial class ClassFolderSyncTests
{
    [Fact]
    public async Task TwoFirstStartsAtOnce_LeaveOneHome_AndItsLessonOnEveryPc()
    {
        for (var round = 0; round < 8; round++)
        {
            using var rig = new ClassSyncRig(homes: 3);
            var one = rig.NewSync();
            var two = ProfTwo(rig);

            var lines = await RaceAsync(one, two);

            AssertOneHomeAndItsLesson(rig, one, two, lines, round);
        }
    }

    // PROF-1's clock is 5 seconds ahead of PROF-2's: its generation is the
    // newer, and decides on both PCs, though its name sorts first.
    [Fact]
    public async Task TwoFirstStartsAtOnce_WithClocksApart_LeaveOneHome_TheNewerGenerations()
    {
        for (var round = 0; round < 12; round++)
        {
            using var rig = new ClassSyncRig(homes: 3);
            var one = rig.NewSync(clock: () => rig.Now.AddSeconds(5));
            var two = ProfTwo(rig);

            var lines = await RaceAsync(one, two);

            var home = AssertOneHomeAndItsLesson(rig, one, two, lines, round);
            // PROF-1's generation, unless PROF-2 got every PC first (PROF-1 then held none).
            var generation = rig.Pcs[0].Agent.CurrentHome!.Generation;
            Assert.Same(generation == rig.NowTicks ? two : one, home);
            Assert.Contains(generation, new[] { rig.NowTicks, rig.NowTicks + TimeSpan.FromSeconds(5).Ticks });
        }
    }

    // PROF-2's clock is ahead (its generation the newer), but PROF-1's claims
    // reach every PC between PROF-2's reading of the homes and its own claims:
    // PROF-2 holds no PC, and starts nothing, newer or not (taking them all
    // back would race PROF-1's lesson, already on its way). Its lines name
    // PROF-1, it keeps no generation, and it has seen PROF-1 as the home.
    [Fact]
    public async Task ANewerPcThatHoldsNoPc_StartsNothing()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var two = ProfTwo(rig, clock: () => rig.Now.AddSeconds(5));
        two.ClaimsGoingOut = () =>
        {
            foreach (var pc in rig.Pcs)
            {
                Assert.True(ClassFolderClient.ClaimHome(
                    pc.Machine, rig.Master, new ClassHomeClaim(ThisPc, ClassFolderRemote.Port, false, rig.NowTicks), rig.AgentPort).Ok);
            }
        };

        var refused = await two.StartAsync("3º B");

        Assert.Equal(3, refused.Count);
        Assert.All(refused, line => Assert.Equal((false, ClassHomeClaim.OtherHomeText + ThisPc), (line.Ok, line.Detail)));
        Assert.Equal(ClassLesson.None(0), two.Lesson);
        Assert.Null(ClassHomeFileMaster.Read(OtherHomePath(rig)));
        Assert.Equal(ThisPc, ClassHomeFileMaster.ReadLastOtherHome(OtherHomePath(rig))?.Name);
        Assert.All(rig.Pcs, pc => Assert.Equal((ThisPc, rig.NowTicks), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));
        Assert.All(rig.Pcs, pc => Assert.False(pc.Agent.Current.IsOn));
    }

    // PROF-2 took PC1 at the same generation while PROF-1 couldn't see PC1
    // (off for it): PROF-1's claims all go through, but its lesson is refused
    // on PC1, naming a home that outranks it. PROF-1 backs out: its lesson is
    // ended on the PCs that took it, one second after it, and isn't on here;
    // every line names PROF-2. PROF-2's start then takes the room back, with
    // a newer generation, and its lesson, saved in the second of PROF-1's,
    // goes on every PC.
    [Fact]
    public async Task AStartRefusedByAPcOfAHomeThatOutranksThisPc_BacksOut_AndTheRoomGoesToThatHome()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var pc1 = rig.Pcs[0];
        ClaimFor(rig, pc1, OtherPc, takeOver: false, rig.NowTicks);
        rig.Offline[pc1.Name] = true;
        var sync = rig.NewSync();

        var refused = await sync.StartAsync(Turma);

        Assert.Equal(3, refused.Count);
        Assert.All(refused, line => Assert.Equal((false, ClassHomeClaim.OtherHomeText + OtherPc), (line.Ok, line.Detail)));
        var backedOut = rig.Pcs[1].Agent.Current;
        Assert.False(backedOut.IsOn);
        Assert.Equal(Turma, backedOut.Turma);
        Assert.Equal(backedOut, rig.Pcs[2].Agent.Current);
        Assert.Equal(backedOut, sync.Lesson);
        Assert.Equal(OtherPc, ClassHomeFileMaster.ReadLastOtherHome(rig.HomePath)?.Name);

        rig.Offline.Clear();
        var other = ProfTwo(rig);
        var started = await other.StartAsync("3º B");

        Assert.All(started, line => Assert.True(line.Ok, $"{line.Machine.Name}: {line.Detail}"));
        Assert.Equal(backedOut.SavedAt - 1, other.Lesson.SavedAt);
        Assert.All(rig.Pcs, pc => Assert.Equal(other.Lesson, pc.Agent.Current));
        Assert.All(rig.Pcs, pc => Assert.Equal((OtherPc, rig.NowTicks + 1), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));
        Assert.Equal(rig.NowTicks + 1, ClassHomeFileMaster.Read(OtherHomePath(rig)));
    }

    // The same back-out with PC2's answer to the lesson lost (it took the
    // lesson, but this PC never heard so): the end still reaches it, as it
    // reaches every PC known to be in the lesson or that can't be read. And
    // this PC keeps that end as a held one, which its pollers hand only to a
    // PC still in the lesson.
    [Fact]
    public async Task ABackOut_ReachesAPcWhoseAnswerWasLost()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var (pc1, pc2) = (rig.Pcs[0], rig.Pcs[1]);
        ClaimFor(rig, pc1, OtherPc, takeOver: false, rig.NowTicks);
        rig.Offline[pc1.Name] = true;
        var sync = rig.NewSync();
        sync.SendToAgent = (machine, lesson) =>
        {
            var answer = ClassLessonOnAgents.Send(machine, rig.Master, lesson, rig.AgentPort);
            return machine == pc2.Machine && lesson.IsOn ? (false, "a conexão caiu") : answer;
        };

        var refused = await sync.StartAsync(Turma);

        Assert.All(refused, line => Assert.Equal((false, ClassHomeClaim.OtherHomeText + OtherPc), (line.Ok, line.Detail)));
        var backedOut = rig.Pcs[2].Agent.Current;
        Assert.False(backedOut.IsOn);
        Assert.Equal(backedOut, pc2.Agent.Current);
        Assert.Equal(backedOut, sync.Lesson);
        Assert.True(sync.EndGoesOnlyToItsLesson);
    }

    // PROF-1, which couldn't see PC1 (another list of machines), started its
    // lesson on PC2 and PC3 before PROF-2's start: PROF-2 outranks it, but
    // can't take those PCs back while PROF-1's lesson is on them. Its start
    // doesn't happen (no second lesson in the room), each line says PROF-1
    // started at the same time, and it keeps its newer generation, so the
    // room comes to it once that lesson ends, and its next start takes every PC.
    [Fact]
    public async Task AWinnerThatFindsTheOtherPcsLessonOn_DoesNotStart_AndGetsTheRoomOnceItEnds()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var pc1 = rig.Pcs[0];
        ClaimFor(rig, pc1, OtherPc, takeOver: false, rig.NowTicks);
        var others = rig.Machines.Skip(1).ToList();
        var sync = new ClassFolderSync(
            rig.Store, rig.Master, () => others, _ => true, () => rig.Now, ThisPc, agentPort: rig.AgentPort,
            sessionPort: rig.SessionPort, interval: TimeSpan.FromHours(1), lessonPath: rig.LessonPath, homePath: rig.HomePath);
        try
        {
            await sync.StartAsync(Turma);
            var lesson = sync.Lesson;
            Assert.All(rig.Pcs.Skip(1), pc => Assert.Equal(lesson, pc.Agent.Current));
            var other = ProfTwo(rig);

            var refused = await other.StartAsync("3º B");

            Assert.Equal(3, refused.Count);
            Assert.All(refused, line => Assert.Equal((false, ClassFolderText.OtherPcsLessonFirst(ThisPc)), (line.Ok, line.Detail)));
            Assert.False(other.Lesson.IsOn);
            Assert.All(rig.Pcs.Skip(1), pc => Assert.Equal((lesson, ThisPc), (pc.Agent.Current, pc.Agent.CurrentHome?.Name)));
            Assert.Equal((OtherPc, rig.NowTicks + 1), (pc1.Agent.CurrentHome!.Name, pc1.Agent.CurrentHome.Generation));
            Assert.Equal(rig.NowTicks + 1, ClassHomeFileMaster.Read(OtherHomePath(rig)));

            rig.Now = rig.Now.AddMinutes(1);
            await sync.EndAsync(EndChoice.Keep);
            rig.Now = rig.Now.AddMinutes(1);
            var started = await other.StartAsync("3º B");

            Assert.All(started, line => Assert.True(line.Ok, $"{line.Machine.Name}: {line.Detail}"));
            Assert.All(rig.Pcs, pc => Assert.Equal((other.Lesson, OtherPc), (pc.Agent.Current, pc.Agent.CurrentHome?.Name)));
        }
        finally
        {
            sync.Dispose();
        }
    }

    // That lesson may be taken back by then: the line doesn't say it is on.
    [Fact]
    public void TheOtherPcsLessonFirstText_IsTheOneAgreed() => Assert.Equal(
        "o PC PROF-1 começou uma aula ao mesmo tempo; tente de novo", ClassFolderText.OtherPcsLessonFirst("PROF-1"));

    // PROF-2, the other teacher PC of the lab, with its own store, files and (maybe) clock.
    private static ClassFolderSync ProfTwo(ClassSyncRig rig, Func<DateTime>? clock = null) =>
        rig.NewSync(
            machineName: OtherPc, master: rig.OtherMaster, lessonPath: OtherLessonPath(rig), homePath: OtherHomePath(rig),
            store: rig.OwnStore("outro-professor"), clock: clock);

    // PROF-1 starts 3º A and PROF-2 3º B at the same moment.
    private static async Task<IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)>[]> RaceAsync(
        ClassFolderSync one, ClassFolderSync two)
    {
        using var go = new ManualResetEventSlim();
        var first = Task.Run(() =>
        {
            go.Wait();
            return one.StartAsync(Turma);
        });
        var second = Task.Run(() =>
        {
            go.Wait();
            return two.StartAsync("3º B");
        });
        go.Set();
        return await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(60));
    }

    // One home, at one generation, its lesson on every PC and all its lines
    // Ok; the other PC with no lesson, its lines naming the home. The home.
    private static ClassFolderSync AssertOneHomeAndItsLesson(
        ClassSyncRig rig, ClassFolderSync one, ClassFolderSync two,
        IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)>[] lines, int round)
    {
        string Said(IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)> pcs) =>
            string.Join("; ", pcs.Select(line => $"{line.Machine.Name} {line.Ok} {line.Detail}"));
        Assert.True(one.Lesson.IsOn ^ two.Lesson.IsOn,
            $"round {round}: PROF-1 on {one.Lesson.IsOn} ({Said(lines[0])}), PROF-2 on {two.Lesson.IsOn} ({Said(lines[1])}); homes "
            + string.Join(", ", rig.Pcs.Select(pc => $"{pc.Name} {pc.Agent.CurrentHome?.Name}@{pc.Agent.CurrentHome?.Generation}")));
        var (home, homeLines, other, otherLines) = one.Lesson.IsOn
            ? (one, lines[0], two, lines[1])
            : (two, lines[1], one, lines[0]);
        var name = home == one ? ThisPc : OtherPc;
        Assert.All(homeLines, line => Assert.True(line.Ok, $"round {round}, {line.Machine.Name}: {line.Detail}"));
        // A line for every PC, or, refused before its claims (it read the homes
        // after the other's claims reached some PCs), one for each of those PCs.
        Assert.InRange(otherLines.Count, 1, 3);
        Assert.All(otherLines, line => Assert.Equal((false, ClassHomeClaim.OtherHomeText + name), (line.Ok, line.Detail)));
        Assert.Equal(ClassLesson.None(0), other.Lesson);
        Assert.All(rig.Pcs, pc => Assert.Equal(name, pc.Agent.CurrentHome?.Name));
        Assert.Single(rig.Pcs.Select(pc => pc.Agent.CurrentHome!.Generation).Distinct());
        Assert.All(rig.Pcs, pc => Assert.Equal(home.Lesson, pc.Agent.Current));
        return home;
    }
}
