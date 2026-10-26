using Midview.Master;
using Midview.Protocol;
using Xunit;

// A start whose take-overs some PCs refuse with "Encerre a aula…" (another
// home's lesson on there): nothing starts while the room's newest lesson is
// on and a PC that refused so follows it (the other teacher PC's lesson, or
// its next one), nor while such a PC's lesson can't be read; a lesson older
// than the room's newest, still on a PC that was off for its end, leaves only
// that PC out. Here PROF-1 sees only PC2 and PC3 (seen), PROF-2 holds PC1,
// and PROF-2's first start met PROF-1's lesson on PC2 and PC3.
public sealed partial class ClassFolderSyncTests
{
    // PROF-2 retries at once, PROF-1's lesson still on: refused again, saying
    // to try once that lesson is over; no lesson starts beside PROF-1's.
    [Fact]
    public async Task ARetryWhileTheOtherPcsLessonIsStillOn_IsRefusedAgain_AndStartsNothing()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var seen = rig.Machines.Skip(1).ToList();
        using var one = ProfOneSeeing(rig, seen);
        var (two, lesson) = await MeetTheOtherPcsLessonAsync(rig, one);
        rig.Now = rig.Now.AddMinutes(1);

        var retry = await two.StartAsync("3º B");

        Assert.Equal(3, retry.Count);
        Assert.All(retry, line => Assert.Equal((false, ClassFolderText.OtherPcsLessonOn(ThisPc)), (line.Ok, line.Detail)));
        Assert.False(two.Lesson.IsOn);
        Assert.False(rig.Pcs[0].Agent.Current.IsOn);
        Assert.All(rig.Pcs.Skip(1), pc => Assert.Equal((lesson, ThisPc), (pc.Agent.Current, pc.Agent.CurrentHome?.Name)));

        rig.Now = rig.Now.AddMinutes(1);
        await one.EndAsync(EndChoice.Keep);
        rig.Now = rig.Now.AddMinutes(1);
        var started = await two.StartAsync("3º B");

        Assert.All(started, line => Assert.True(line.Ok, $"{line.Machine.Name}: {line.Detail}"));
        Assert.All(rig.Pcs, pc => Assert.Equal((two.Lesson, OtherPc), (pc.Agent.Current, pc.Agent.CurrentHome?.Name)));
    }

    // ...nor beside PROF-1's next lesson, started once the first ended.
    [Fact]
    public async Task ARetryWhileTheOtherPcsNextLessonIsOn_IsRefused_AndStartsNothing()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var seen = rig.Machines.Skip(1).ToList();
        using var one = ProfOneSeeing(rig, seen);
        var (two, _) = await MeetTheOtherPcsLessonAsync(rig, one);
        rig.Now = rig.Now.AddMinutes(30);
        await one.EndAsync(EndChoice.Keep);
        rig.Now = rig.Now.AddMinutes(30);
        await one.StartAsync(Turma);
        var next = one.Lesson;
        Assert.All(rig.Pcs.Skip(1), pc => Assert.Equal(next, pc.Agent.Current));
        rig.Now = rig.Now.AddMinutes(1);

        var retry = await two.StartAsync("3º B");

        Assert.All(retry, line => Assert.Equal((false, ClassFolderText.OtherPcsLessonOn(ThisPc)), (line.Ok, line.Detail)));
        Assert.False(two.Lesson.IsOn);
        Assert.False(rig.Pcs[0].Agent.Current.IsOn);
        Assert.All(rig.Pcs.Skip(1), pc => Assert.Equal(next, pc.Agent.Current));
    }

    // Once PROF-1 ended that lesson, a PC that was off for its end (PC3,
    // still in it) blocks nothing: PC2's end is newer. PROF-2 starts, and PC3
    // is left out until its lesson is over there.
    [Fact]
    public async Task ARetryAfterTheOtherPcsLessonEnded_Starts_ThoughAPcOffForTheEndIsStillInIt()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var (pc1, pc2, pc3) = (rig.Pcs[0], rig.Pcs[1], rig.Pcs[2]);
        var seen = rig.Machines.Skip(1).ToList();
        using var one = ProfOneSeeing(rig, seen);
        var (two, lesson) = await MeetTheOtherPcsLessonAsync(rig, one);
        rig.Now = rig.Now.AddMinutes(30);
        seen.Remove(pc3.Machine); // off for PROF-1's end
        await one.EndAsync(EndChoice.Keep);
        Assert.False(pc2.Agent.Current.IsOn);
        Assert.Equal(lesson, pc3.Agent.Current);
        rig.Now = rig.Now.AddMinutes(30);

        var started = await two.StartAsync("3º B");

        Assert.True(two.Lesson.IsOn, Said(started));
        Assert.All(new[] { pc1, pc2 }, pc => Assert.Equal((two.Lesson, OtherPc), (pc.Agent.Current, pc.Agent.CurrentHome?.Name)));
        Assert.Equal((lesson, ThisPc), (pc3.Agent.Current, pc3.Agent.CurrentHome?.Name));
    }

    // ...also once PROF-1 ran another lesson since (PC2 now holds that one's
    // end), and closed: nothing of PROF-1's is on, but PC3 is still in the
    // first lesson. PROF-2 starts; PC3 is left out.
    [Fact]
    public async Task ARetryAfterTheOtherPcRanAnotherLesson_Starts_ThoughAPcIsStillInTheFirst()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var (pc1, pc2, pc3) = (rig.Pcs[0], rig.Pcs[1], rig.Pcs[2]);
        var seen = rig.Machines.Skip(1).ToList();
        ClassFolderSync two;
        ClassLesson lesson;
        using (var one = ProfOneSeeing(rig, seen))
        {
            (two, lesson) = await MeetTheOtherPcsLessonAsync(rig, one);
            rig.Now = rig.Now.AddMinutes(30);
            seen.Remove(pc3.Machine);
            await one.EndAsync(EndChoice.Keep);
            rig.Now = rig.Now.AddMinutes(30);
            await one.StartAsync(Turma);
            Assert.Equal(one.Lesson, pc2.Agent.Current);
            rig.Now = rig.Now.AddMinutes(30);
            await one.EndAsync(EndChoice.Keep);
        }
        Assert.NotEqual(lesson.LessonId, pc2.Agent.Current.LessonId);
        Assert.Equal(lesson, pc3.Agent.Current);
        rig.Now = rig.Now.AddMinutes(30);

        var started = await two.StartAsync("3º B");

        Assert.True(two.Lesson.IsOn, Said(started));
        Assert.All(new[] { pc1, pc2 }, pc => Assert.Equal(two.Lesson, pc.Agent.Current));
        Assert.Equal(lesson, pc3.Agent.Current);
    }

    // With only PC3 (still in PROF-1's ended lesson) on, nothing says that
    // lesson ended: the start is refused. Once PC2 is back, it goes on.
    [Fact]
    public async Task ARetryWithOnlyThePcStillInTheLessonOn_IsRefused_UntilAPcWithItsEndIsBack()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var (pc2, pc3) = (rig.Pcs[1], rig.Pcs[2]);
        var seen = rig.Machines.Skip(1).ToList();
        using var one = ProfOneSeeing(rig, seen);
        var (two, _) = await MeetTheOtherPcsLessonAsync(rig, one);
        rig.Now = rig.Now.AddMinutes(30);
        seen.Remove(pc3.Machine);
        await one.EndAsync(EndChoice.Keep);
        rig.Now = rig.Now.AddMinutes(30);
        rig.Offline[pc2.Name] = true;

        var refused = await two.StartAsync("3º B");

        Assert.False(two.Lesson.IsOn);
        Assert.All(refused, line => Assert.Equal((false, ClassFolderText.OtherPcsLessonOn(ThisPc)), (line.Ok, line.Detail)));

        rig.Offline.Clear();
        var started = await two.StartAsync("3º B");

        Assert.True(two.Lesson.IsOn, Said(started));
    }

    // A PC that refused for a lesson on there and whose lesson can't then be
    // read (gone off meanwhile): this start is refused, never one beside
    // what can't be seen.
    [Fact]
    public async Task ARefusingPcWhoseLessonCantBeRead_RefusesTheStart()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var (pc2, pc3) = (rig.Pcs[1], rig.Pcs[2]);
        var seen = rig.Machines.Skip(1).ToList();
        using var one = ProfOneSeeing(rig, seen);
        var dropPc3 = false;
        var (two, _) = await MeetTheOtherPcsLessonAsync(rig, one, message =>
        {
            if (Volatile.Read(ref dropPc3) && message.StartsWith($"{pc3.Name} didn't take", StringComparison.Ordinal))
            {
                rig.Offline[pc3.Name] = true; // off right after it refused
            }
        });
        rig.Now = rig.Now.AddMinutes(30);
        seen.Remove(pc3.Machine);
        await one.EndAsync(EndChoice.Keep); // PC2 has its end; PC3, still in it, alone could say nothing
        rig.Now = rig.Now.AddMinutes(30);
        Volatile.Write(ref dropPc3, true);
        using var disposeTwo = two;

        var refused = await two.StartAsync("3º B");

        Assert.False(two.Lesson.IsOn, Said(refused));
        Assert.All(refused, line => Assert.Equal((false, ClassFolderText.OtherPcsLessonOn(ThisPc)), (line.Ok, line.Detail)));
        Assert.False(pc2.Agent.Current.IsOn);
    }

    // PROF-1's PCs busy with their students' folders when PROF-2 retries: their
    // take-overs come back busy, not "Encerre a aula…", but their lessons can
    // be read, and PROF-1's is the room's newest: nothing starts.
    [Fact]
    public async Task ARetryWhoseTakeOversFindTheOtherPcsPcsBusy_IsRefused()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var seen = rig.Machines.Skip(1).ToList();
        using var one = ProfOneSeeing(rig, seen);
        var (two, lesson) = await MeetTheOtherPcsLessonAsync(rig, one);
        rig.Now = rig.Now.AddMinutes(1);

        IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)> retry;
        using (new BusyAgents(rig.Pcs.Skip(1)))
        {
            retry = await two.StartAsync("3º B");
        }

        Assert.False(two.Lesson.IsOn, Said(retry));
        Assert.All(retry, line => Assert.Equal((false, ClassFolderText.OtherPcsLessonOn(ThisPc)), (line.Ok, line.Detail)));
        Assert.False(rig.Pcs[0].Agent.Current.IsOn);
        Assert.All(rig.Pcs.Skip(1), pc => Assert.Equal(lesson, pc.Agent.Current));
    }

    // PROF-1 was the home; its last lesson ran on PC3 alone, then its PC
    // broke. PROF-2 took the folders over from the home that didn't answer,
    // with PC3 off. PC3 comes back still in that lesson, which PROF-2 never
    // knew: as a PC of the home taken over, in a lesson from before the
    // take-over, it is left out, and PROF-2's starts go on.
    [Fact]
    public async Task AfterATakeOverFromAHomeThatDidntAnswer_APcStillInItsLesson_IsLeftOut()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var (pc1, pc2, pc3) = (rig.Pcs[0], rig.Pcs[1], rig.Pcs[2]);
        var (two, lesson) = await TakeOverWithPc3OffAsync(rig, lessonOnPc3: true);

        var started = await two.StartAsync("3º B");

        Assert.True(two.Lesson.IsOn, Said(started));
        Assert.All(new[] { pc1, pc2 }, pc => Assert.Equal((two.Lesson, OtherPc), (pc.Agent.Current, pc.Agent.CurrentHome?.Name)));
        Assert.Equal((lesson, ThisPc), (pc3.Agent.Current, pc3.Agent.CurrentHome?.Name));
    }

    // ...the same when the old home is recorded under a new certificate (paired
    // again) and PC3 still keeps its old one: the same PC by its name.
    [Fact]
    public async Task AfterATakeOverFromAHomePairedAgain_APcStillInItsLesson_IsLeftOut()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var (two, _) = await TakeOverWithPc3OffAsync(rig, lessonOnPc3: true);
        var (recorded, at) = ClassHomeFileMaster.ReadTakenOverFrom(OtherHomePath(rig))!.Value;
        ClassHomeFileMaster.WriteTakenOverFrom(recorded with { Thumbprint = new string('C', 40) }, at, OtherHomePath(rig));

        var started = await two.StartAsync("3º B");

        Assert.True(two.Lesson.IsOn, Said(started));
    }

    // But a lesson the old home started after the take-over (it came back,
    // seeing only PC3), or a PC that keeps the old home above the generation
    // taken over (it claimed PC3 again since): not a straggler's, and while
    // that lesson is the room's newest, nothing starts.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AfterATakeOverFromAHomeThatDidntAnswer_ItsLessonOfLaterOrANewerClaim_StillBlocks(bool laterLesson)
    {
        using var rig = new ClassSyncRig(homes: 3);
        var pc3 = rig.Pcs[2];
        var (two, _) = await TakeOverWithPc3OffAsync(rig, lessonOnPc3: !laterLesson);
        var recorded = ClassHomeFileMaster.ReadTakenOverFrom(OtherHomePath(rig))!.Value.Home;
        if (laterLesson)
        {
            using var back = ProfOneSeeing(rig, new List<KnownMachine> { pc3.Machine });
            await back.StartAsync(Turma);
            Assert.True(pc3.Agent.Current.IsOn);
        }
        else
        {
            Assert.True(ClassFolderClient.ClaimHome(
                pc3.Machine, rig.Master, new ClassHomeClaim(ThisPc, ClassFolderRemote.Port, false, recorded.Generation + 1),
                rig.AgentPort).Ok);
        }
        rig.Now = rig.Now.AddMinutes(1);

        var lines = await two.StartAsync("3º B");

        Assert.False(two.Lesson.IsOn, Said(lines));
        Assert.All(lines, line => Assert.Equal((false, ClassFolderText.OtherPcsLessonOn(ThisPc)), (line.Ok, line.Detail)));
    }

    // Any start that meets another home's lesson.
    [Fact]
    public void TheOtherPcsLessonOnText_IsTheOneAgreed() => Assert.Equal(
        "a aula do PC PROF-1 continua em andamento; tente de novo quando ela acabar", ClassFolderText.OtherPcsLessonOn("PROF-1"));

    // PROF-1 (this rig's PC), seeing only the machines in seen.
    private static ClassFolderSync ProfOneSeeing(ClassSyncRig rig, List<KnownMachine> seen) => new(
        rig.Store, rig.Master, () => seen.ToList(), _ => true, () => rig.Now, ThisPc, agentPort: rig.AgentPort,
        sessionPort: rig.SessionPort, interval: TimeSpan.FromHours(1), lessonPath: rig.LessonPath, homePath: rig.HomePath);

    // PC1 keeps PROF-2; PROF-1 starts on PC2 and PC3; PROF-2's start meets that
    // lesson there and starts nothing. PROF-2 (twoLog: what it logs), and
    // PROF-1's lesson.
    private static async Task<(ClassFolderSync Two, ClassLesson Lesson)> MeetTheOtherPcsLessonAsync(
        ClassSyncRig rig, ClassFolderSync one, Action<string>? twoLog = null)
    {
        ClaimFor(rig, rig.Pcs[0], OtherPc, takeOver: false, rig.NowTicks);
        await one.StartAsync(Turma);
        var two = twoLog is null
            ? ProfTwo(rig)
            : new ClassFolderSync(
                rig.OwnStore("outro-professor"), rig.OtherMaster, () => rig.Machines, machine => !rig.Offline.ContainsKey(machine.Name),
                () => rig.Now, OtherPc, agentPort: rig.AgentPort, sessionPort: rig.SessionPort, interval: TimeSpan.FromHours(1),
                lessonPath: OtherLessonPath(rig), log: twoLog, homePath: OtherHomePath(rig));
        Assert.All(await two.StartAsync("3º B"), line => Assert.Equal(ClassFolderText.OtherPcsLessonFirst(ThisPc), line.Detail));
        return (two, one.Lesson);
    }

    // PROF-1 the home of every PC (a lesson ended there); with lessonOnPc3, its
    // next lesson then on PC3 alone. PROF-1 breaks; an hour later PROF-2 takes
    // the folders over from it, unanswered, with PC3 off, which is back ten
    // minutes later. PROF-2, and PC3's lesson.
    private static async Task<(ClassFolderSync Two, ClassLesson OnPc3)> TakeOverWithPc3OffAsync(ClassSyncRig rig, bool lessonOnPc3)
    {
        var pc3 = rig.Pcs[2];
        var seen = rig.Machines.ToList();
        using (var one = ProfOneSeeing(rig, seen))
        {
            await one.StartAsync(Turma);
            await one.EndAsync(EndChoice.Keep);
            if (lessonOnPc3)
            {
                rig.Now = rig.Now.AddMinutes(30);
                seen.RemoveAll(machine => machine != pc3.Machine);
                await one.StartAsync("3º B");
                Assert.True(pc3.Agent.Current.IsOn);
            }
        }
        rig.Now = rig.Now.AddHours(1);
        rig.Offline[pc3.Name] = true;
        var store = rig.OwnStore("outro-professor");
        var two = rig.NewSync(
            machineName: OtherPc, master: rig.OtherMaster, lessonPath: OtherLessonPath(rig), homePath: OtherHomePath(rig), store: store);
        var taker = new ClassFolderHandOver(
            store, two, rig.OtherMaster, OtherPc, () => rig.Machines, machine => !rig.Offline.ContainsKey(machine.Name), () => rig.Now,
            rig.AgentPort, homePath: OtherHomePath(rig), log: _ => { });
        Assert.True((await taker.TakeOverUnreachableAsync(ThisPc)).Ok);
        rig.Offline.Clear();
        rig.Now = rig.Now.AddMinutes(10);
        return (two, pc3.Agent.Current);
    }

    private static string Said(IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)> lines) =>
        string.Join("; ", lines.Select(line => $"{line.Machine.Name} {line.Ok} {line.Detail}"));
}
