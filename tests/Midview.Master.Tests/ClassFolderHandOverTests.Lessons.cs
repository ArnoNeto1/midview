using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderHandOver and this PC's own lessons: no lesson starts on the PC
// taking the folders over until the take-over
// is over, and none may be on when it begins, so what a take-over puts back
// or throws away never holds files a lesson there brought in.
public sealed partial class ClassFolderHandOverTests
{
    // A start on this PC during the copy is refused, saying why, and nothing
    // of it reaches the Agents.
    [Fact]
    public async Task AStartOnThisPc_WhileItTakesTheFoldersOver_IsRefused()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        lab.GiveTheNewHomeAClassOfItsOwn();
        IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)>? during = null;
        var progress = new Inline(step =>
        {
            if (during is null && step.Files > 0)
            {
                during = lab.NewSync.StartAsync("Antiga").GetAwaiter().GetResult();
            }
        });

        var result = await lab.Taker.TakeOverAsync(lab.Home, progress);

        Assert.True(result.Ok, result.Detail);
        Assert.NotNull(during);
        Assert.Equal(rig.Pcs.Count, during!.Count);
        Assert.All(during, line => Assert.Equal((false, ClassFolderText.TakeOverRunningHere), (line.Ok, line.Detail)));
        Assert.False(lab.NewSync.Lesson.IsOn);
        // Once it is over, this PC (the home now) starts lessons (a minute on: newer than the old home's last).
        rig.Now = rig.Now.AddMinutes(1);
        var started = await lab.NewSync.StartAsync(Turma);
        Assert.All(started, line => Assert.True(line.Ok, $"{line.Machine.Name}: {line.Detail}"));
    }

    // The old home: no lesson starts there while its copy goes to a
    // colleague's PC, nor after, until that PC says how its take-over went
    // (or the settle wait passes): a start among that PC's claims would
    // split the room. Each PC's line says why, and nothing reaches the Agents.
    [Fact]
    public async Task AStartOnTheOldHome_WhileItsCopyIsSentOrUnsettled_IsRefused()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        lab.Giver.SettleWait = TimeSpan.FromSeconds(2);
        IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)>? sending = null, unsettled = null;
        var progress = new Inline(step =>
        {
            if (sending is null && step.Files > 0 && !step.InPlace)
            {
                sending = lab.OldSync.StartAsync(Turma).GetAwaiter().GetResult();
            }
        });
        var claimed = new object();
        lab.Taker.SendClaim = (_, _, _) =>
        {
            lock (claimed)
            {
                unsettled ??= lab.OldSync.StartAsync(Turma).GetAwaiter().GetResult();
            }
            return (false, "recusado", false);
        };

        var result = await lab.Taker.TakeOverAsync(lab.Home, progress);

        Assert.False(result.Ok);
        foreach (var lines in new[] { sending, unsettled })
        {
            Assert.NotNull(lines);
            Assert.Equal(rig.Pcs.Count, lines!.Count);
            Assert.All(lines, line => Assert.Equal((false, ClassFolderText.HandOverRunningHere), (line.Ok, line.Detail)));
        }
        Assert.False(lab.OldSync.Lesson.IsOn);
        Assert.All(rig.Pcs, pc => Assert.False(pc.Agent.Current.IsOn));

        // Settled (never told here: the wait passed), it starts lessons again.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (lab.Giver.IsBusy)
        {
            Assert.True(DateTime.UtcNow < deadline, "still busy");
            await Task.Delay(50);
        }
        rig.Now = rig.Now.AddMinutes(1);
        var started = await lab.OldSync.StartAsync(Turma);
        Assert.All(started, line => Assert.True(line.Ok, $"{line.Machine.Name}: {line.Detail}"));
    }

    [Fact]
    public void TheHandOverRunningHereText_IsTheOneAgreed() => Assert.Equal(
        "as pastas das turmas estão passando para outro computador; espere a troca terminar", ClassFolderText.HandOverRunningHere);

    // With a lesson on here (one this PC ran as the home before, say), no
    // take-over begins, from a home that answers or one that doesn't.
    [Fact]
    public async Task ATakeOver_WithALessonOnHere_IsRefused_AndChangesNothing()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        lab.GiveTheNewHomeAClassOfItsOwn();
        var now = new DateTimeOffset(rig.Now).ToUnixTimeSeconds();
        var lessonPath = Path.Combine(lab.NewRoot, "dados", "aula-aqui.json");
        ClassLessonFileMaster.Write(new ClassLesson(now, Guid.NewGuid(), "Antiga", now, Ended: false), lessonPath);
        using var sync = new ClassFolderSync(
            lab.NewStore, rig.OtherMaster, () => rig.Machines, _ => true, () => rig.Now, NewHome, agentPort: rig.AgentPort,
            sessionPort: rig.SessionPort, interval: TimeSpan.FromHours(1), lessonPath: lessonPath, homePath: lab.NewHomePath);
        Assert.True(sync.Lesson.IsOn);
        var taker = new ClassFolderHandOver(
            lab.NewStore, sync, rig.OtherMaster, NewHome, () => rig.Machines, _ => true, () => rig.Now, rig.AgentPort,
            homePath: lab.NewHomePath, log: _ => { });
        var before = lab.Snapshot();

        var result = await taker.TakeOverAsync(lab.Home);
        var unreachable = await taker.TakeOverUnreachableAsync(OldHome);

        Assert.Equal((false, ClassFolderText.TakeOverFailed(OldHome, ClassFolderText.EndLessonFirst)), (result.Ok, result.Detail));
        Assert.Equal((false, ClassFolderText.TakeOverUnreachableFailed(ClassFolderText.EndLessonFirst)), (unreachable.Ok, unreachable.Detail));
        lab.AssertUnchanged(before);
        Assert.True(sync.Lesson.IsOn);
    }

    [Fact]
    public void TheTakeOverRunningHereText_IsTheOneAgreed()
    {
        Assert.Equal(
            "este computador está recebendo as pastas das turmas; espere a troca terminar", ClassFolderText.TakeOverRunningHere);
    }
}
