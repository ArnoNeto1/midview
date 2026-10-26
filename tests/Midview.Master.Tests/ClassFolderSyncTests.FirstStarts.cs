using Midview.Master;
using Midview.Protocol;
using Xunit;

// Two teacher PCs claiming the room at the same moment (their first lessons
// started at once, say): whichever way their claims cross on the Agents, the
// room ends with one home and that home's lesson on every PC, and the other
// PC has no lesson on (no lesson that no Agent follows), its lines naming the
// home. The rig's clock gives both the same generation: the computer name
// decides (PROF-2 over PROF-1).
public sealed partial class ClassFolderSyncTests
{
    [Fact]
    public async Task TwoFirstStartsAtOnce_LeaveOneHome_AndItsLessonOnEveryPc()
    {
        for (var round = 0; round < 8; round++)
        {
            using var rig = new ClassSyncRig(homes: 3);
            var one = rig.NewSync();
            var two = OtherSync(rig, sameCertificate: false);
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
            var lines = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(60));

            Assert.True(one.Lesson.IsOn ^ two.Lesson.IsOn, $"round {round}: {one.Lesson.IsOn} {two.Lesson.IsOn}");
            var (home, homeLines, other, otherLines) = one.Lesson.IsOn
                ? (one, lines[0], two, lines[1])
                : (two, lines[1], one, lines[0]);
            var name = home == one ? ThisPc : OtherPc;
            Assert.All(homeLines, line => Assert.True(line.Ok, $"round {round}, {line.Machine.Name}: {line.Detail}"));
            Assert.Equal(3, otherLines.Count);
            Assert.All(otherLines, line => Assert.Equal((false, ClassHomeClaim.OtherHomeText + name), (line.Ok, line.Detail)));
            Assert.Equal(ClassLesson.None(0), other.Lesson);
            Assert.All(rig.Pcs, pc => Assert.Equal(name, pc.Agent.CurrentHome?.Name));
            Assert.Single(rig.Pcs.Select(pc => pc.Agent.CurrentHome!.Generation).Distinct());
            Assert.All(rig.Pcs, pc => Assert.Equal(home.Lesson, pc.Agent.Current));
        }
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
        Assert.Equal(ClassLesson.None(0), sync.Lesson);
        var backedOut = rig.Pcs[1].Agent.Current;
        Assert.False(backedOut.IsOn);
        Assert.Equal(Turma, backedOut.Turma);
        Assert.Equal(backedOut, rig.Pcs[2].Agent.Current);
        Assert.Equal(OtherPc, ClassHomeFileMaster.ReadLastOtherHome(rig.HomePath)?.Name);

        rig.Offline.Clear();
        var other = OtherSync(rig, sameCertificate: false);
        var started = await other.StartAsync("3º B");

        Assert.All(started, line => Assert.True(line.Ok, $"{line.Machine.Name}: {line.Detail}"));
        Assert.Equal(backedOut.SavedAt - 1, other.Lesson.SavedAt);
        Assert.All(rig.Pcs, pc => Assert.Equal(other.Lesson, pc.Agent.Current));
        Assert.All(rig.Pcs, pc => Assert.Equal((OtherPc, rig.NowTicks + 1), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));
        Assert.Equal(rig.NowTicks + 1, ClassHomeFileMaster.Read(OtherHomePath(rig)));
    }

    // PROF-1, which couldn't see PC1 (another list of machines), started its
    // lesson on PC2 and PC3 before PROF-2's start: PROF-2 outranks it, but
    // can't take those PCs back while PROF-1's lesson is on them. Its start
    // doesn't happen (no second lesson in the room), each line says PROF-1
    // started first, and it keeps its newer generation, so the room comes to it
    // once that lesson ends, and its next start takes every PC.
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
            var other = OtherSync(rig, sameCertificate: false);

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

    [Fact]
    public void TheOtherPcsLessonFirstText_IsTheOneAgreed() => Assert.Equal(
        "o PC PROF-1 começou uma aula antes; tente de novo quando ela acabar", ClassFolderText.OtherPcsLessonFirst("PROF-1"));
}
