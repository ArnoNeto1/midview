using System.Globalization;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderHandOver: the old home's checks before it takes the room back.
// It is still the home only while no
// Agent names a PC newer than it (but the PC giving the take-over up) and it
// handed nothing over meanwhile; a generation given up from too far ahead of
// its clock is refused; a take-back that can't start claiming in time is
// refused rather than finishing after the PC asking stopped waiting; and once
// it took the room back, the PC that gave up goes by it even when only a
// straggler naming that PC is on.
public sealed partial class ClassFolderHandOverTests
{
    // A third teacher PC (never one the rig's Agents are told about through TLS).
    private const string ThirdPcThumbprint = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";

    // PC1 and PC2 keep a third PC (PROF-3), newer than this PC but
    // older than the generation given up; PC3, a straggler, still names this
    // PC. This PC isn't the home: refused, and nothing changes.
    [Fact]
    public async Task ATakeBack_IsRefused_WhenAnAgentKeepsAThirdPcNewerThanThisOne()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        foreach (var pc in rig.Pcs.Take(2))
        {
            Assert.True(pc.Agent.ClaimHome(
                ThirdPcThumbprint, "127.0.0.9", new ClassHomeClaim("PROF-3", ClassFolderRemote.Port, true, generation + 1)).Ok);
        }
        var homes = lab.Homes();

        var answer = lab.SendTakeBack(rig.OtherMaster, ClassFolderRemote.EncodeTakeBack(generation + 5));

        Assert.Equal((false, "O PC PROF-1 não guarda mais as pastas das turmas."), answer);
        Assert.Equal(homes, lab.Homes());
        Assert.Equal(generation, ClassHomeFileMaster.Read(rig.HomePath));
    }

    // This PC saw a third PC as the home, newer than it (it stood down
    // for it before its Master was opened again, say), though no Agent on now
    // names that PC: not the home either.
    [Fact]
    public async Task ATakeBack_IsRefused_WhenThisPcSawAThirdPcNewerThanItself()
    {
        using var rig = new ClassSyncRig(homes: 2, pcs: 2);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        ClassHomeFileMaster.WriteLastOtherHome(
            new ClassHome(ThirdPcThumbprint, "PROF-3", "127.0.0.9", ClassFolderRemote.Port, generation + 1), rig.HomePath);
        var homes = lab.Homes();

        var answer = lab.SendTakeBack(rig.OtherMaster, ClassFolderRemote.EncodeTakeBack(generation + 5));

        Assert.Equal((false, ClassFolderText.NotTheHomeAnyMore(OldHome)), answer);
        Assert.Equal(homes, lab.Homes());
        Assert.Equal(generation, ClassHomeFileMaster.Read(rig.HomePath));
    }

    // The guard on the generation given up, on its own: PC1 keeps the
    // very PC asking, newer than what it says it gave up (the others still
    // name this PC, which has its generation on file).
    [Fact]
    public async Task ATakeBack_IsRefused_WhenAnAgentKeepsTheAskingPcNewerThanWhatItGaveUp()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        Assert.True(ClassFolderClient.ClaimHome(
            rig.Pcs[0].Machine, rig.OtherMaster, new ClassHomeClaim(NewHome, ClassFolderRemote.Port, true, generation + 10), rig.AgentPort).Ok);
        var homes = lab.Homes();

        var answer = lab.SendTakeBack(rig.OtherMaster, ClassFolderRemote.EncodeTakeBack(generation + 5));

        Assert.Equal((false, ClassFolderText.NotTheHomeAnyMore(OldHome)), answer);
        Assert.Equal(homes, lab.Homes());
        Assert.Equal(generation, ClassHomeFileMaster.Read(rig.HomePath));
    }

    // Once this PC handed the folders over (it stood down), a
    // take-back is refused, even from the PC it handed them to.
    [Fact]
    public async Task AnOldHomeThatHandedTheFoldersOver_DoesntTakeTheRoomBack()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        Assert.True(ClassFolderClient.ClaimHome(
            rig.Pcs[0].Machine, rig.OtherMaster, new ClassHomeClaim(NewHome, ClassFolderRemote.Port, true, generation + 1), rig.AgentPort).Ok);
        Assert.True(lab.SendHandedOver(NewHome).Ok);
        var homes = lab.Homes();

        var answer = lab.SendTakeBack(rig.OtherMaster, ClassFolderRemote.EncodeTakeBack(generation + 1));

        Assert.Equal((false, ClassFolderText.NotTheHomeAnyMore(OldHome)), answer);
        Assert.Equal(homes, lab.Homes());
        Assert.Equal(generation, ClassHomeFileMaster.Read(rig.HomePath));
    }

    // A take-back that arrives while this PC hands the folders over
    // waits for it, and then finds this PC isn't the home: never both.
    [Fact]
    public async Task ATakeBack_WaitsForAHandOverInProgress_ThenIsRefused()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        Assert.True(ClassFolderClient.ClaimHome(
            rig.Pcs[0].Machine, rig.OtherMaster, new ClassHomeClaim(NewHome, ClassFolderRemote.Port, true, generation + 1), rig.AgentPort).Ok);
        using var reading = new ManualResetEventSlim();
        using var goOn = new ManualResetEventSlim();
        var held = 1;
        lab.Giver.ReadHome = machine =>
        {
            if (Volatile.Read(ref held) == 1)
            {
                reading.Set();
                goOn.Wait(TimeSpan.FromSeconds(30));
            }
            return ClassFolderClient.GetHome(machine, rig.Master, rig.AgentPort);
        };

        var handingOver = OwnThread(() => Assert.True(lab.SendHandedOver(NewHome).Ok));
        Assert.True(reading.Wait(TimeSpan.FromSeconds(10)));
        Volatile.Write(ref held, 0);
        var takeBack = Task.Factory.StartNew(
            () => lab.SendTakeBack(rig.OtherMaster, ClassFolderRemote.EncodeTakeBack(generation + 1)),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        // It doesn't run alongside the hand-over: it is still waiting when that goes on.
        await Task.WhenAny(takeBack, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.False(takeBack.IsCompleted);
        goOn.Set();
        await handingOver.WaitAsync(TimeSpan.FromSeconds(30));
        var answer = await takeBack.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal((false, ClassFolderText.NotTheHomeAnyMore(OldHome)), answer);
        Assert.Equal(ClassLesson.None(0), lab.OldSync.Lesson);
        Assert.All(rig.Pcs.Skip(1), pc => Assert.Equal((OldHome, generation), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));
    }

    // A generation given up from further ahead of this PC's clock than
    // a day (long.MaxValue would freeze every later take-over) is refused.
    [Fact]
    public async Task ATakeBack_OfAGenerationFromTooFarAhead_IsRefused()
    {
        using var rig = new ClassSyncRig(homes: 2, pcs: 2);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        var homes = lab.Homes();

        foreach (var abandoned in new[] { long.MaxValue, rig.NowTicks + TimeSpan.FromDays(1).Ticks + 1 })
        {
            var answer = lab.SendTakeBack(rig.OtherMaster, ClassFolderRemote.EncodeTakeBack(abandoned));

            Assert.Equal((false, ClassFolderText.TakeBackFromTooFarAhead(OldHome)), answer);
        }
        Assert.Equal(homes, lab.Homes());
        Assert.Equal(generation, ClassHomeFileMaster.Read(rig.HomePath));
    }

    // A room whose generations are ahead of this PC's clock
    // (the clock was set back since it became the home) is still taken back:
    // "too far ahead" counts from the later of the clock and its own generation.
    [Fact]
    public async Task ATakeBack_InARoomAheadOfThisPcsClock_IsTakenBack()
    {
        using var rig = new ClassSyncRig(homes: 2, pcs: 2);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        rig.Now = rig.Now.AddDays(-3);

        var (ok, detail) = lab.SendTakeBack(rig.OtherMaster, ClassFolderRemote.EncodeTakeBack(generation + 1));

        Assert.True(ok, detail);
        var takenBack = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        Assert.Equal(takenBack.ToString(CultureInfo.InvariantCulture), detail);
        Assert.True(takenBack > generation + 1);
        Assert.All(rig.Pcs, pc => Assert.Equal((OldHome, takenBack), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));
    }

    // An old home's yes naming a generation from more than a
    // day ahead isn't believed: this PC keeps that home at what it gave up
    // + 1, so a bogus answer can't put every later take-over out of reach.
    [Fact]
    public async Task ATakeBackAnswer_FromTooFarAhead_IsKeptAsWhatWasGivenUpPlusOne()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig, openHost: false);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = rig.Pcs[0].Agent.CurrentHome!.Generation;
        lab.PutALessonOnTheAgentOf(rig.Pcs[1]);
        lab.PutALessonOnTheAgentOf(rig.Pcs[2]);
        lab.LetPc1TakeTheClaimAndGoOff(takes: true);
        using var home = new ScriptedHandOverHome(rig, channel =>
        {
            SendManifest(channel, new[] { Turma });
            Done(channel);
        })
        {
            TakeBackAnswer = (true, long.MaxValue.ToString(CultureInfo.InvariantCulture)),
        };

        var result = await lab.Taker.TakeOverAsync(lab.Home with { Port = home.Port });

        Assert.Equal(ClassFolderText.TakeOverTakenBack(OldHome, new[] { "PC1" }), result.Detail);
        var seen = ClassHomeFileMaster.ReadLastOtherHome(lab.NewHomePath)!;
        Assert.Equal((OldHome, generation + 2), (seen.Name, seen.Generation));
    }

    // A take-over given up at the latest generation (the room one below it):
    // the old home's yes is kept at the latest, never past it, whatever it names.
    [Fact]
    public async Task ATakeBackAnswer_ForATakeOverAtTheLatest_IsKeptAtTheLatest()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig, openHost: false);
        await lab.MakeTheOldHomeTheHomeAsync();
        foreach (var pc in rig.Pcs)
        {
            Assert.True(ClassFolderClient.ClaimHome(
                pc.Machine, rig.Master, new ClassHomeClaim(OldHome, ClassFolderRemote.Port, false, ClassHome.MaxGeneration - 1),
                rig.AgentPort).Ok);
        }
        lab.PutALessonOnTheAgentOf(rig.Pcs[1]);
        lab.PutALessonOnTheAgentOf(rig.Pcs[2]);
        lab.LetPc1TakeTheClaimAndGoOff(takes: true);
        using var home = new ScriptedHandOverHome(rig, channel =>
        {
            SendManifest(channel, new[] { Turma });
            Done(channel);
        })
        {
            TakeBackAnswer = (true, ClassHome.MaxGeneration.ToString(CultureInfo.InvariantCulture)),
        };

        var result = await lab.Taker.TakeOverAsync(lab.Home with { Port = home.Port });

        Assert.Equal(ClassFolderText.TakeOverTakenBack(OldHome, new[] { "PC1" }), result.Detail);
        var pc1Home = rig.Pcs[0].Agent.CurrentHome!;
        Assert.Equal((NewHome, ClassHome.MaxGeneration), (pc1Home.Name, pc1Home.Generation));
        var seen = ClassHomeFileMaster.ReadLastOtherHome(lab.NewHomePath)!;
        Assert.Equal((OldHome, ClassHome.MaxGeneration), (seen.Name, seen.Generation));
    }

    // The old home takes longer than one network wait to take the room
    // back (its Agents slow to answer): this PC waits for its answer, and says
    // the folders stayed there, not that it can't tell.
    [Fact]
    public async Task ASlowTakeBackThatWorks_IsToldTakenBack_NotUndecided()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        lab.GiveTheNewHomeAClassOfItsOwn();
        lab.PutALessonOnTheAgentOf(rig.Pcs[1]);
        lab.PutALessonOnTheAgentOf(rig.Pcs[2]);
        lab.LetPc1TakeTheClaimAndGoOff(takes: true);
        lab.Taker.NetworkWait = TimeSpan.FromSeconds(1);
        lab.Giver.ReadHome = machine =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(1.5));
            return ClassFolderClient.GetHome(machine, rig.Master, rig.AgentPort);
        };

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.Equal(ClassFolderText.TakeOverTakenBack(OldHome, new[] { "PC1" }), result.Detail);
        Assert.Equal(new[] { "Antiga" }, lab.NewStore.List().Select(summary => summary.Turma));
    }

    // A take-back that can't start claiming within TakeBackClaimBy of
    // being asked (the Agents slow to answer, a hand-over in the way) is
    // refused, so it never finishes after the PC asking stopped waiting.
    [Fact]
    public async Task ATakeBackThatCantStartClaimingInTime_IsRefused_AndChangesNothing()
    {
        using var rig = new ClassSyncRig(homes: 2, pcs: 2);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        var homes = lab.Homes();
        var now = 1_000L;
        lab.Giver.Milliseconds = () => Interlocked.Read(ref now);
        lab.Giver.ReadHome = machine =>
        {
            Interlocked.Add(ref now, (long)ClassFolderHandOver.TakeBackClaimBy.TotalMilliseconds);
            return ClassFolderClient.GetHome(machine, rig.Master, rig.AgentPort);
        };

        var answer = lab.SendTakeBack(rig.OtherMaster, ClassFolderRemote.EncodeTakeBack(generation + 1));

        Assert.Equal((false, ClassFolderText.TakeBackTooSlow(OldHome)), answer);
        Assert.Equal(homes, lab.Homes());
        Assert.Equal(generation, ClassHomeFileMaster.Read(rig.HomePath));
        Assert.True(ClassFolderHandOver.TakeBackClaimBy * 3 < ClassFolderHandOver.TakeBackWait);
    }

    // After the old home took the room back, its answer carries
    // the generation it took it with, which this PC keeps as the home it saw.
    // With only PC1 on (it took this PC's claim, and names it), this PC still
    // goes by the old home: a remote, and its start is refused. The old home's
    // next start takes PC1 back.
    [Fact]
    public async Task AfterATakeBack_OnlyTheStragglerOn_LeavesThisPcARemote_AndTheOldHomeTakesItBack()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = rig.Pcs[0].Agent.CurrentHome!.Generation;
        lab.FillTheOldHome();
        lab.GiveTheNewHomeAClassOfItsOwn();
        lab.PutALessonOnTheAgentOf(rig.Pcs[1]);
        lab.PutALessonOnTheAgentOf(rig.Pcs[2]);
        lab.LetPc1TakeTheClaimAndGoOff(takes: true);

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.Equal(ClassFolderText.TakeOverTakenBack(OldHome, new[] { "PC1" }), result.Detail);
        var takenBack = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        var seen = ClassHomeFileMaster.ReadLastOtherHome(lab.NewHomePath)!;
        Assert.Equal((OldHome, takenBack), (seen.Name, seen.Generation));
        Assert.Equal(rig.Master.Thumbprint, seen.Thumbprint, ignoreCase: true);

        // Only PC1 is on, naming this PC at the generation given up.
        rig.Offline.Clear();
        rig.Offline["PC2"] = true;
        rig.Offline["PC3"] = true;
        using var tracker = lab.TrackerOf(rig.OtherMaster, NewHome, lab.NewHomePath);
        await tracker.RefreshAsync();
        Assert.Equal(ClassFolderMode.Remote, tracker.Mode);
        Assert.Equal((OldHome, takenBack), (tracker.Home!.Name, tracker.Home.Generation));
        var started = await lab.NewSync.StartAsync("Antiga");
        Assert.All(started, line => Assert.Equal((false, "As pastas das turmas ficam no PC PROF-1"), (line.Ok, line.Detail)));
        Assert.False(lab.NewSync.Lesson.IsOn);
        Assert.Equal((NewHome, generation + 1), (rig.Pcs[0].Agent.CurrentHome!.Name, rig.Pcs[0].Agent.CurrentHome!.Generation));

        // The old home's next start: PC1 is a straggler, taken back.
        rig.Offline.Clear();
        var oldStarted = await lab.OldSync.StartAsync(Turma);
        var pc1 = oldStarted.Single(line => line.Machine.Name == "PC1");
        Assert.True(pc1.Ok, pc1.Detail);
        Assert.Equal((OldHome, takenBack), (rig.Pcs[0].Agent.CurrentHome!.Name, rig.Pcs[0].Agent.CurrentHome!.Generation));
        Assert.Equal(lab.OldSync.Lesson, rig.Pcs[0].Agent.Current);
    }

    // The old home's own generation isn't on file (it couldn't be
    // saved, say): it goes by the one the room's Agents recorded for it.
    [Fact]
    public async Task ATakeBackWithNoGenerationOnFile_GoesByTheOneTheAgentsKeep()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = rig.Pcs[0].Agent.CurrentHome!.Generation;
        lab.FillTheOldHome();
        lab.PutALessonOnTheAgentOf(rig.Pcs[1]);
        lab.PutALessonOnTheAgentOf(rig.Pcs[2]);
        lab.LetPc1TakeTheClaimAndGoOff(takes: true, whenGone: () => File.Delete(rig.HomePath));

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.Equal(ClassFolderText.TakeOverTakenBack(OldHome, new[] { "PC1" }), result.Detail);
        var takenBack = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        Assert.True(takenBack > generation + 1);
        Assert.All(rig.Pcs.Skip(1), pc => Assert.Equal((OldHome, takenBack), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));
    }

    // Agents busy with their students' folders are asked once more
    // (as a start does), and take the room back then; the answer carries the
    // generation the old home took it with, newer than its own.
    [Fact]
    public async Task ATakeBack_AsksABusyAgentOnceMore()
    {
        using var rig = new ClassSyncRig(homes: 3, claimWait: BusyAgents.ClaimWait);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        (bool Ok, string Detail) answer;
        using (var busy = new BusyAgents(rig.Pcs))
        {
            var freed = 0;
            lab.GiverLog = message =>
            {
                if (message.Contains("was busy", StringComparison.Ordinal) && Interlocked.Exchange(ref freed, 1) == 0)
                {
                    busy.Free();
                }
            };
            answer = lab.SendTakeBack(rig.OtherMaster, ClassFolderRemote.EncodeTakeBack(1));
        }

        Assert.True(answer.Ok, answer.Detail);
        var takenBack = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        Assert.Equal(takenBack.ToString(CultureInfo.InvariantCulture), answer.Detail);
        Assert.True(takenBack > generation);
        Assert.All(rig.Pcs, pc => Assert.Equal((OldHome, takenBack), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));
    }
}
