using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderHandOver: a claim whose answer never came back (Rulings 18 to 20).
// A claim that went out may still be taken: this PC waits until no Agent can
// take it any more (its lock wait, from when the answer was lost, and a
// margin), then asks the Agents which home they keep. One that took it makes
// the take-over done; when one whose answer was lost doesn't answer that
// either, nothing is put back and the room decides when that PC is back.
// A claim that never went out needs no wait. Never the classes split.
public sealed partial class ClassFolderHandOverTests
{
    // An Agent that takes the claim (or not) and whose answer never comes back.
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
        lab.Taker.SendClaim = (machine, claim, sending) =>
        {
            sending();
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

    // No answer says yes: PC1 refuses (it still follows a lesson), PC2 took
    // the claim and its answer was lost, PC3 is off. The Agents asked again
    // say PC2 keeps this PC: the take-over is done, its generation kept.
    [Fact]
    public async Task AClaimOnlyALostAnswerTook_IsDone()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = rig.Pcs[0].Agent.CurrentHome!.Generation;
        lab.FillTheOldHome();
        lab.PutALessonOnTheAgentOf(rig.Pcs[0]);
        rig.Offline["PC3"] = true;
        lab.Taker.SendClaim = (machine, claim, sending) =>
        {
            var answer = ClassFolderClient.ClaimHome(machine, rig.OtherMaster, claim, rig.AgentPort, sending);
            return machine.Name == "PC2" ? throw new IOException("the answer was lost") : answer;
        };

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.True(result.Ok, result.Detail);
        Assert.Equal(OldHome, rig.Pcs[0].Agent.CurrentHome!.Name);
        Assert.Equal((NewHome, generation + 1), (rig.Pcs[1].Agent.CurrentHome!.Name, rig.Pcs[1].Agent.CurrentHome!.Generation));
        Assert.Equal(OldHome, rig.Pcs[2].Agent.CurrentHome!.Name);
        Assert.Equal(generation + 1, ClassHomeFileMaster.Read(lab.NewHomePath));
        Assert.True(Directory.Exists(Path.Combine(rig.Root, "Turmas (passadas para PROF-2)")));
    }

    // Ruling 19 (a): the claim's connection breaks (a reset) while the Agent
    // still waits for its lesson lock; the lock is freed a moment after the
    // answer was lost, and the claim is taken then. Done, never split.
    [Fact]
    public async Task AClaimWhoseConnectionBreaksWhileTheAgentWaits_IsDoneWhenItIsTakenLater()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = rig.Pcs[0].Agent.CurrentHome!.Generation;
        lab.FillTheOldHome();
        var claims = new List<Task>();
        var missed = 0;
        HandOverResult result;
        using (var busy = new BusyAgents(rig.Pcs))
        {
            lab.FreeWhenLogged(busy, LostLine);
            lab.Taker.SendClaim = (machine, claim, sending) =>
            {
                var gone = new ManualResetEventSlim();
                lock (claims)
                {
                    claims.Add(OwnThread(() => ClassFolderClient.ClaimHome(machine, rig.OtherMaster, claim, rig.AgentPort, gone.Set)));
                }
                if (!gone.Wait(TimeSpan.FromSeconds(10)))
                {
                    Volatile.Write(ref missed, 1);
                }
                sending();
                throw new IOException("the connection was reset");
            };
            result = await lab.Taker.TakeOverAsync(lab.Home);
            await lab.WhenAll(claims);
        }

        Assert.Equal(0, missed); // every claim went out
        Assert.True(result.Ok, result.Detail);
        Assert.All(rig.Pcs, pc => Assert.Equal((NewHome, generation + 1), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));
        Assert.Equal(generation + 1, ClassHomeFileMaster.Read(lab.NewHomePath));
        Assert.True(Directory.Exists(Path.Combine(rig.Root, "Turmas (passadas para PROF-2)")));
    }

    // Ruling 20 (1): a claim slow to reach the Agent (a dispatch behind the
    // others, a slow handshake: longer than the whole wait from the start)
    // whose answer is then lost, and the Agent takes it a moment later. The
    // wait runs from when the answer was lost, so this PC finds it: done.
    [Fact]
    public async Task AClaimThatReachesTheAgentLate_AndIsLost_IsDoneWhenItIsTaken()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = rig.Pcs[0].Agent.CurrentHome!.Generation;
        lab.FillTheOldHome();
        var claims = new List<Task>();
        var missed = 0;
        HandOverResult result;
        using (var busy = new BusyAgents(rig.Pcs))
        {
            lab.FreeWhenLogged(busy, LostLine);
            lab.Taker.SendClaim = (machine, claim, sending) =>
            {
                Thread.Sleep(lab.Taker.LostAnswerWait + TimeSpan.FromMilliseconds(500));
                var gone = new ManualResetEventSlim();
                lock (claims)
                {
                    claims.Add(OwnThread(() => ClassFolderClient.ClaimHome(machine, rig.OtherMaster, claim, rig.AgentPort, gone.Set)));
                }
                if (!gone.Wait(TimeSpan.FromSeconds(10)))
                {
                    Volatile.Write(ref missed, 1);
                }
                sending();
                throw new IOException("the connection was reset");
            };
            result = await lab.Taker.TakeOverAsync(lab.Home);
            await lab.WhenAll(claims);
        }

        Assert.Equal(0, missed); // every claim went out
        Assert.True(result.Ok, result.Detail);
        Assert.Equal((NewHome, generation + 1), (rig.Pcs[0].Agent.CurrentHome!.Name, rig.Pcs[0].Agent.CurrentHome!.Generation));
        Assert.Equal(generation + 1, ClassHomeFileMaster.Read(lab.NewHomePath));
        Assert.Equal(new[] { Turma, "3º B", "Vídeos" }, lab.NewStore.List().Select(summary => summary.Turma));
    }

    // Ruling 20 (1): the wait runs from when the answer was lost, not from when
    // the claim went out (3 s before, here: the clock and the wait are the
    // test's), and a claim that never went out (no connection, no handshake)
    // needs none.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ALostAnswer_IsWaitedOutFromWhenItWasLost_AndAClaimThatNeverWentOutIsNot(bool wentOut)
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var now = 1_000L;
        var slept = new List<TimeSpan>();
        lab.Taker.LostAnswerWait = ClassFolderHandOver.DefaultLostAnswerWait;
        lab.Taker.Milliseconds = () => Interlocked.Read(ref now);
        lab.Taker.Sleep = wait => slept.Add(wait);
        lab.Taker.SendClaim = (_, _, sending) =>
        {
            Interlocked.Exchange(ref now, 2_000);
            if (wentOut)
            {
                sending();
            }
            Interlocked.Exchange(ref now, 5_000);
            throw new IOException("the answer was lost");
        };

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.False(result.Ok);
        Assert.Equal(ClassHomeClaim.MaxLockWait + TimeSpan.FromSeconds(2), ClassFolderHandOver.DefaultLostAnswerWait);
        Assert.Equal(wentOut ? new[] { ClassFolderHandOver.DefaultLostAnswerWait } : Array.Empty<TimeSpan>(), slept);
    }

    // Ruling 20 (2), Ruling 21 (b): PC1 may have taken the claim (its answer
    // was lost) and doesn't answer when asked again (it went off), and the old
    // home's Master closed before it could be asked to take the room back.
    // Nothing is put back (that could split the classes): the copy stays in
    // Turmas, this PC's own classes aside, and this PC keeps no generation of
    // its own. When PC1 is back, the room's PCs decide: naming this PC (it
    // took the claim), this PC works as the home with the copy; naming the old
    // home, this PC stays a remote, and its starts are refused.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnUndecidedTakeOverTheOldHomeCantTakeBack_LeavesTheCopy_AndTheRoomDecidesWhenPc1IsBack(bool itTookTheClaim)
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = rig.Pcs[0].Agent.CurrentHome!.Generation;
        var oldLesson = lab.OldSync.Lesson;
        lab.FillTheOldHome();
        lab.GiveTheNewHomeAClassOfItsOwn();
        lab.PutALessonOnTheAgentOf(rig.Pcs[1]);
        lab.PutALessonOnTheAgentOf(rig.Pcs[2]);
        lab.LetPc1TakeTheClaimAndGoOff(itTookTheClaim, whenGone: () => lab.Host!.Dispose());

        var result = await lab.Taker.TakeOverAsync(lab.Home);
        rig.Offline.Clear(); // PC1 is back

        Assert.False(result.Ok);
        Assert.Equal(ClassFolderText.TakeOverUndecided(OldHome, new[] { "PC1" }, OwnAside), result.Detail);
        Assert.Equal(new[] { Turma, "3º B", "Vídeos" }, lab.NewStore.List().Select(summary => summary.Turma));
        Assert.True(File.Exists(Path.Combine(lab.NewRoot, OwnAside, "Antiga", "velho.txt")));
        Assert.Equal(new[] { Turma, "3º B", "Vídeos" }, rig.Store.List().Select(summary => summary.Turma));
        Assert.Equal(oldLesson, lab.OldSync.Lesson);
        Assert.Null(ClassHomeFileMaster.Read(lab.NewHomePath));
        lab.AssertNoStaging();

        using var tracker = lab.TrackerOf(rig.OtherMaster, NewHome, lab.NewHomePath);
        await tracker.RefreshAsync();
        if (itTookTheClaim)
        {
            Assert.Equal(ClassFolderMode.Local, tracker.Mode);
            Assert.Equal((NewHome, generation + 1), (tracker.Home!.Name, tracker.Home.Generation));
        }
        else
        {
            Assert.Equal(ClassFolderMode.Remote, tracker.Mode);
            Assert.Equal(OldHome, tracker.Home!.Name);
            var started = await lab.NewSync.StartAsync(Turma);
            Assert.All(started, line => Assert.False(line.Ok));
            Assert.False(lab.NewSync.Lesson.IsOn);
            Assert.All(rig.Pcs, pc => Assert.Equal(OldHome, pc.Agent.CurrentHome!.Name));
        }
    }

    // What the taking PC logs when a claim's answer is lost after it went out.
    private const string LostLine = "answer to the claim was lost";

    private sealed partial class HandOverLab
    {
        // The busy Agents are freed a moment after the taking PC logs text
        // (once): their claims wait for the lock StudentPc.ClaimWait, five
        // times this moment, so the claim is taken well after its answer was lost.
        public void FreeWhenLogged(BusyAgents busy, string text)
        {
            var freed = 0;
            TakerLog = message =>
            {
                if (message.Contains(text, StringComparison.Ordinal) && Interlocked.Exchange(ref freed, 1) == 0)
                {
                    busy.FreeAfter(StudentPc.ClaimWait / 5);
                }
            };
        }

        // The claims started on their own threads, all finished.
        public async Task WhenAll(List<Task> claims)
        {
            Task[] started;
            lock (claims)
            {
                started = claims.ToArray();
            }
            await Task.WhenAll(started).WaitAsync(TimeSpan.FromSeconds(30));
        }
    }
}
