using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderSync with two teacher PCs in one lab (plan decisions 2, 8 and 9,
// Rulings 9 and 10): the claim on the class folders' home before a start, by
// generation; standing down when another PC keeps them; and adopting an end
// another PC made on the Agents. The rig's Agents keep the home (homes).
public sealed partial class ClassFolderSyncTests
{
    private const string ThisPc = ClassSyncRig.MachineName;
    private const string OtherPc = "PROF-2";

    [Fact]
    public async Task StartAsync_ClaimsTheHome_WithThisPcsFirstGeneration_ThenStarts()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var sync = rig.NewSync();

        var started = await sync.StartAsync(Turma);

        Assert.Equal(3, started.Count);
        Assert.All(started, result => Assert.True(result.Ok, $"{result.Machine.Name}: {result.Detail}"));
        Assert.Equal(rig.NowTicks, ClassHomeFileMaster.Read(rig.HomePath));
        foreach (var pc in rig.Pcs)
        {
            Assert.Equal(sync.Lesson, pc.Agent.Current);
            var home = pc.Agent.CurrentHome;
            Assert.NotNull(home);
            Assert.Equal(ThisPc, home!.Name);
            Assert.Equal(rig.Master.Thumbprint, home.Thumbprint, ignoreCase: true);
            Assert.Equal(ClassFolderRemote.Port, home.Port);
            Assert.Equal(rig.NowTicks, home.Generation);
        }

        // Later lessons claim with the same generation.
        await sync.EndAsync(EndChoice.Keep);
        rig.Now = rig.Now.AddMinutes(50);
        await sync.StartAsync("3º B");
        Assert.All(rig.Pcs, pc => Assert.Equal(rig.NowTicks - TimeSpan.FromMinutes(50).Ticks, pc.Agent.CurrentHome!.Generation));
    }

    // Another teacher PC of the lab, never the home, with another teacher's
    // certificate or the same one (two PCs can share one teacher identity).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASecondTeacherPc_IsRefusedNamingTheHome_AndSavesAndSendsNothing(bool sameCertificate)
    {
        using var rig = new ClassSyncRig(homes: 3);
        var home = rig.NewSync();
        await home.StartAsync(Turma);
        var lesson = home.Lesson;
        var homes = rig.Pcs.Select(pc => pc.Agent.CurrentHome).ToList();
        var other = OtherSync(rig, sameCertificate);
        var changes = new List<ClassLesson>();
        other.LessonChanged += changes.Add;

        var refused = await other.StartAsync("3º B");

        Assert.Equal(3, refused.Count);
        Assert.All(refused, result =>
        {
            Assert.False(result.Ok);
            Assert.Equal(ClassHomeClaim.OtherHomeText + ThisPc, result.Detail);
        });
        Assert.False(other.Lesson.IsOn);
        Assert.Empty(changes);
        Assert.False(File.Exists(OtherLessonPath(rig)));
        Assert.Null(ClassHomeFileMaster.Read(OtherHomePath(rig)));
        Assert.All(rig.Pcs, pc => Assert.Equal(lesson, pc.Agent.Current));
        Assert.Equal(homes, rig.Pcs.Select(pc => pc.Agent.CurrentHome));
    }

    // Ruling 11: B saw A as the home; with only Agents that keep no home on, B's start is
    // still cancelled, naming A, and nothing is claimed: B becomes the home only by taking
    // the folders over.
    [Fact]
    public async Task APcThatSawAnotherHome_DoesNotBecomeTheHomeByStarting()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var pc1 = rig.Pcs[0];
        var (claimed, why, _) = ClassFolderClient.ClaimHome(
            pc1.Machine, rig.Master, new ClassHomeClaim(ThisPc, ClassFolderRemote.Port, false, rig.NowTicks), rig.AgentPort);
        Assert.True(claimed, why);
        rig.Offline[rig.Pcs[1].Name] = true;
        rig.Offline[rig.Pcs[2].Name] = true;
        using (var tracker = OtherTracker(rig))
        {
            await tracker.RefreshAsync();
            Assert.Equal(ClassFolderMode.Remote, tracker.Mode);
        }
        rig.Offline.Clear();
        rig.Offline[pc1.Name] = true;
        var other = OtherSync(rig, sameCertificate: false);

        var refused = await other.StartAsync("3º B");

        Assert.Equal(new[] { "PC2", "PC3" }, refused.Select(result => result.Machine.Name).Order());
        Assert.All(refused, result => Assert.Equal((false, ClassHomeClaim.OtherHomeText + ThisPc), (result.Ok, result.Detail)));
        Assert.False(other.Lesson.IsOn);
        Assert.Null(ClassHomeFileMaster.Read(OtherHomePath(rig)));
        Assert.All(rig.Pcs.Skip(1), pc => Assert.Null(pc.Agent.CurrentHome));
        Assert.All(rig.Pcs, pc => Assert.False(pc.Agent.Current.IsOn));
    }

    // Ruling 11: a PC that never saw a home starts in a lab where no Agent keeps one.
    [Fact]
    public async Task AFreshPcThatSawNoHome_StartsInAnEmptyLab()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using (var tracker = OtherTracker(rig))
        {
            await tracker.RefreshAsync();
            Assert.Null(tracker.Home);
        }
        var other = OtherSync(rig, sameCertificate: false);

        var started = await other.StartAsync("3º B");

        Assert.All(started, result => Assert.True(result.Ok, $"{result.Machine.Name}: {result.Detail}"));
        Assert.True(other.Lesson.IsOn);
        Assert.Equal(rig.NowTicks, ClassHomeFileMaster.Read(OtherHomePath(rig)));
        Assert.All(rig.Pcs, pc => Assert.Equal(OtherPc, pc.Agent.CurrentHome?.Name));
    }

    // A start cancelled for another home counts as having seen it.
    [Fact]
    public async Task ACancelledStart_RemembersTheHomeItWasRefusedFor()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var pc1 = rig.Pcs[0];
        var (claimed, why, _) = ClassFolderClient.ClaimHome(
            pc1.Machine, rig.Master, new ClassHomeClaim(ThisPc, ClassFolderRemote.Port, false, rig.NowTicks), rig.AgentPort);
        Assert.True(claimed, why);
        var other = OtherSync(rig, sameCertificate: false);

        var refused = await other.StartAsync("3º B");

        Assert.Equal(new[] { "PC1" }, refused.Select(result => result.Machine.Name));
        Assert.Equal(new SeenHome(rig.Master.Thumbprint, ThisPc, rig.NowTicks), ClassHomeFileMaster.ReadLastOtherHome(OtherHomePath(rig)));

        rig.Offline[pc1.Name] = true;
        var again = await other.StartAsync("3º B");

        Assert.Equal(2, again.Count);
        Assert.False(other.Lesson.IsOn);
        Assert.All(rig.Pcs.Skip(1), pc => Assert.Null(pc.Agent.CurrentHome));
    }

    // Ruling 9: a PC took the folders over while only one PC was on. Once the others are
    // back, the old home's start sees the newer home on that one and cancels; the new
    // home's start brings the others over.
    [Fact]
    public async Task ATakeOverMadeWithOnePcOnline_IsNotUndone_ByTheOldHomesNextStart()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        await sync.EndAsync(EndChoice.Keep);
        var ended = sync.Lesson;
        var generation = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        var newer = ClassHomeFileMaster.TakeOverGeneration(rig.NowTicks, new[] { rig.Pcs[0].Agent.CurrentHome! });
        ClaimFor(rig, rig.Pcs[0], OtherPc, takeOver: true, newer);

        var refused = await sync.StartAsync(Turma);

        Assert.Equal(new[] { "PC1" }, refused.Select(result => result.Machine.Name));
        Assert.All(refused, result => Assert.Equal((false, ClassHomeClaim.OtherHomeText + OtherPc), (result.Ok, result.Detail)));
        Assert.Equal(ended, sync.Lesson);
        Assert.All(rig.Pcs, pc => Assert.False(pc.Agent.Current.IsOn));
        Assert.Equal(OtherPc, rig.Pcs[0].Agent.CurrentHome?.Name);
        Assert.All(rig.Pcs.Skip(1), pc => Assert.Equal((ThisPc, generation), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));

        ClassHomeFileMaster.Write(newer, OtherHomePath(rig));
        rig.Now = rig.Now.AddMinutes(5); // its lesson is saved after the old home's end
        var started = await OtherSync(rig, sameCertificate: false).StartAsync("3º B");

        Assert.All(started, result => Assert.True(result.Ok, $"{result.Machine.Name}: {result.Detail}"));
        Assert.All(rig.Pcs, pc => Assert.Equal((OtherPc, newer), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));
    }

    // A PC that still keeps an older home (it was off when this PC took the folders over)
    // is taken back by this PC's next start.
    [Fact]
    public async Task AStraggler_KeepingAnOlderHome_IsTakenBackByTheHomesNextStart()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var pc3 = rig.Pcs[2];
        ClaimFor(rig, pc3, OtherPc, takeOver: false, rig.NowTicks - 1_000);
        rig.Offline[pc3.Name] = true;
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        await sync.EndAsync(EndChoice.Keep);
        rig.Offline.TryRemove(pc3.Name, out _);

        var started = await sync.StartAsync("3º B");

        Assert.Equal(3, started.Count);
        Assert.All(started, result => Assert.True(result.Ok, $"{result.Machine.Name}: {result.Detail}"));
        Assert.All(rig.Pcs, pc => Assert.Equal(sync.Lesson, pc.Agent.Current));
        Assert.All(rig.Pcs, pc => Assert.Equal((ThisPc, rig.NowTicks), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));
    }

    // Ruling 9: the home PC renamed (same certificate, its generation on file) keeps the folders.
    [Fact]
    public async Task ARenamedPc_KeepsItsHome()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var before = rig.NewSync();
        await before.StartAsync(Turma);
        await before.EndAsync(EndChoice.Keep);
        before.Dispose();

        var renamed = rig.NewSync(machineName: "PROF-1-NOVO");
        var started = await renamed.StartAsync("3º B");

        Assert.All(started, result => Assert.True(result.Ok, $"{result.Machine.Name}: {result.Detail}"));
        Assert.True(renamed.Lesson.IsOn);
        Assert.All(rig.Pcs, pc => Assert.Equal(("PROF-1-NOVO", rig.NowTicks), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));
    }

    // Agents from before two teacher PCs take any start, as in part 1.
    [Fact]
    public async Task OldAgents_CountAsAccepting()
    {
        using var rig = new ClassSyncRig(homes: 1);
        var sync = rig.NewSync();

        var started = await sync.StartAsync(Turma);

        Assert.True(sync.Lesson.IsOn);
        Assert.Equal(3, started.Count);
        Assert.All(started, result => Assert.True(result.Ok, $"{result.Machine.Name}: {result.Detail}"));
        Assert.Equal(ThisPc, rig.Pcs[0].Agent.CurrentHome?.Name);
    }

    // An Agent that doesn't answer says nothing about the home, even one that keeps a newer one.
    [Fact]
    public async Task AnAgentThatDoesNotAnswer_IsNotCounted()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        await sync.EndAsync(EndChoice.Keep);
        var pc3 = rig.Pcs[2];
        ClaimFor(rig, pc3, OtherPc, takeOver: true, rig.NowTicks + 1_000);
        pc3.StopAgent();

        var started = await sync.StartAsync("3º B");

        Assert.True(sync.Lesson.IsOn);
        Assert.All(started.Where(result => result.Machine.Name != pc3.Name), result => Assert.True(result.Ok, result.Detail));
        Assert.False(started.Single(result => result.Machine.Name == pc3.Name).Ok);
        Assert.All(rig.Pcs.Take(2), pc => Assert.Equal(sync.Lesson, pc.Agent.Current));
    }

    [Fact]
    public async Task StandDown_ClearsTheLesson_StopsTheRounds_AndKeepsTheStore()
    {
        using var rig = new ClassSyncRig(homes: 3);
        rig.WriteInStore("mapa.png", Bytes(10, 1), Utc(1));
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        var lesson = sync.Lesson;
        await rig.Rounds(sync, 1);
        var changes = new List<ClassLesson>();
        sync.LessonChanged += changes.Add;

        await sync.StandDownAsync();
        await sync.StandDownAsync();

        Assert.Equal(ClassLesson.None(0), sync.Lesson);
        Assert.Equal(sync.Lesson, ClassLessonFile.Read(rig.LessonPath));
        Assert.Equal(new[] { ClassLesson.None(0) }, changes);
        Assert.Equal(new[] { "mapa.png" }, rig.StoreFiles());
        Assert.False(rig.Store.IsCollected(lesson.LessonId));

        // No more rounds: what a student saves stays on their PC.
        rig.Pcs[0].Write("novo.txt", Bytes(10, 2), Utc(2));
        await rig.Rounds(sync, 2);
        Assert.Equal(new[] { "mapa.png" }, rig.StoreFiles());
        Assert.All(rig.Pcs.Skip(1), pc => Assert.DoesNotContain("novo.txt", pc.Files()));

        // The folders back on this PC: it starts lessons again, each newer than the last.
        var again = await sync.StartAsync("3º B");
        Assert.All(again, result => Assert.True(result.Ok, $"{result.Machine.Name}: {result.Detail}"));
        Assert.True(sync.Lesson.IsOn);
        Assert.True(sync.Lesson.SavedAt > lesson.SavedAt);
    }

    // Ruling 10: a PC that stood down hands its Agents nothing, so the new home's lesson
    // stays on every PC, even when this PC's clock is ahead of the new home's.
    [Fact]
    public async Task AfterStandingDown_ThePollersHandNothingOver()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        await sync.EndAsync(EndChoice.Keep);
        var ended = sync.Lesson;
        var newer = ClassHomeFileMaster.TakeOverGeneration(rig.NowTicks, rig.Pcs.Select(pc => pc.Agent.CurrentHome!));
        foreach (var pc in rig.Pcs)
        {
            ClaimFor(rig, pc, OtherPc, takeOver: true, newer);
        }
        rig.Now = rig.Now.AddHours(1);

        await sync.StandDownAsync();
        var theirs = new ClassLesson(ended.SavedAt + 2, Guid.NewGuid(), "3º B", ended.StartedAt, Ended: false);
        foreach (var pc in rig.Pcs)
        {
            var (ok, detail) = ClassFolderClient.SetLesson(pc.Machine, rig.OtherMaster, theirs, rig.AgentPort);
            Assert.True(ok, detail);
        }
        var pollers = rig.Pcs.Select(pc => new ThumbnailPoller(
            pc.Machine, rig.Master, rig.SessionPort, rig.AgentPort, agentRetryInterval: TimeSpan.Zero,
            classLesson: () => sync.Lesson)).ToList();
        var thumbnails = new int[pollers.Count];
        try
        {
            for (var index = 0; index < pollers.Count; index++)
            {
                var poller = index;
                pollers[poller].ThumbnailReceived += _ => Interlocked.Increment(ref thumbnails[poller]);
                pollers[poller].Start();
            }
            await WaitUntil(() => Enumerable.Range(0, thumbnails.Length).All(index => Volatile.Read(ref thumbnails[index]) >= 2));
        }
        finally
        {
            foreach (var poller in pollers)
            {
                poller.Dispose();
            }
        }

        Assert.All(rig.Pcs, pc => Assert.Equal(theirs, pc.Agent.Current));
    }

    // The home's Master was closed and a colleague's PC ended the lesson straight on the
    // Agents ("Encerrar a aula", keeping the files): the home takes the end as one nobody
    // collected, and what the PCs set aside comes in at the next lesson.
    [Fact]
    public async Task AnEndAnotherPcMade_IsAdoptedUncollected_AndItsFilesComeInAtTheNextLesson()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        var lesson = sync.Lesson;
        var pc1 = rig.Pcs[0];
        foreach (var pc in rig.Pcs)
        {
            pc.Follow();
        }
        pc1.Write("depois.txt", Bytes(10, 1), Utc(1)); // never copied
        var ended = EndOnTheAgents(rig, lesson with { SavedAt = lesson.SavedAt + 5, Ended = true });
        var changes = new List<ClassLesson>();
        sync.LessonChanged += changes.Add;

        await sync.AdoptAgentsEndAsync();

        Assert.Equal(ended, sync.Lesson);
        Assert.Equal(new[] { ended }, changes);
        Assert.Equal(ended, ClassLessonFile.Read(rig.LessonPath));
        Assert.False(rig.Store.IsCollected(lesson.LessonId));

        await sync.StartAsync("3º B");
        foreach (var pc in rig.Pcs)
        {
            pc.Follow();
        }
        await rig.Rounds(sync, 1);

        Assert.Equal(new[] { "depois.txt" }, rig.StoreFiles(Turma));
        Assert.Equal(Bytes(10, 1), File.ReadAllBytes(rig.StorePath("depois.txt")));
        Assert.Empty(rig.StoreFiles("3º B"));
    }

    // Ruling 10: what is adopted is this lesson, ended when the Agents say, whatever else
    // their copy says.
    [Fact]
    public async Task AnAgentsEnd_IsAdoptedAsThisLessonEnded()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        var lesson = sync.Lesson;
        EndOnTheAgents(rig, lesson with { SavedAt = lesson.SavedAt + 5, Ended = true, Turma = "3º B", StartedAt = 0 });

        await sync.AdoptAgentsEndAsync();

        Assert.Equal(lesson with { SavedAt = lesson.SavedAt + 5, Ended = true }, sync.Lesson);
    }

    // Ruling 10: an end saved more than a day ahead of this PC's clock is not believed.
    [Fact]
    public async Task AnEndFromMoreThanADayAhead_IsNotAdopted()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        var lesson = sync.Lesson;
        var now = new DateTimeOffset(rig.Now).ToUnixTimeSeconds();
        EndOnTheAgents(rig, lesson with { SavedAt = now + (long)TimeSpan.FromDays(2).TotalSeconds, Ended = true });

        await sync.AdoptAgentsEndAsync();

        Assert.Equal(lesson, sync.Lesson);
    }

    [Fact]
    public async Task AHomeOpenedAfterAnotherPcEndedItsLesson_AdoptsTheEndByItself()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var closed = rig.NewSync();
        await closed.StartAsync(Turma);
        var lesson = closed.Lesson;
        closed.Dispose();
        var ended = EndOnTheAgents(rig, lesson with { SavedAt = lesson.SavedAt + 5, Ended = true });

        var reopened = rig.NewSync();

        await WaitUntil(() => reopened.Lesson == ended);
        Assert.Equal(ended, ClassLessonFile.Read(rig.LessonPath));
        Assert.False(rig.Store.IsCollected(lesson.LessonId));
    }

    // Only an end of this very lesson, newer than the one the home has.
    [Fact]
    public async Task AnotherLessonOnTheAgents_IsNotAdopted()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        var lesson = sync.Lesson;
        EndOnTheAgents(rig, new ClassLesson(lesson.SavedAt + 5, Guid.NewGuid(), Turma, lesson.StartedAt, Ended: true));
        var changes = new List<ClassLesson>();
        sync.LessonChanged += changes.Add;

        await sync.AdoptAgentsEndAsync();

        Assert.Equal(lesson, sync.Lesson);
        Assert.Empty(changes);
    }

    private static string OtherLessonPath(ClassSyncRig rig) => Path.Combine(rig.Root, "outro-professor", "class-lesson.json");

    private static string OtherHomePath(ClassSyncRig rig) => Path.Combine(rig.Root, "outro-professor", "class-home.json");

    // The other teacher PC of the lab, PROF-2, with its own lesson and generation files.
    private static ClassFolderSync OtherSync(ClassSyncRig rig, bool sameCertificate) =>
        rig.NewSync(
            machineName: OtherPc, master: sameCertificate ? rig.Master : rig.OtherMaster,
            lessonPath: OtherLessonPath(rig), homePath: OtherHomePath(rig));

    // The other teacher PC's tracker, with its own generation file.
    private static ClassHomeTracker OtherTracker(ClassSyncRig rig) =>
        new(rig.OtherMaster, OtherPc, () => rig.Machines, machine => !rig.Offline.ContainsKey(machine.Name),
            TimeSpan.FromHours(1), rig.AgentPort, OtherHomePath(rig));

    // What the other teacher PC's claim does on that Agent.
    private static void ClaimFor(ClassSyncRig rig, StudentPc pc, string name, bool takeOver, long generation)
    {
        var (ok, detail, _) = ClassFolderClient.ClaimHome(
            pc.Machine, rig.OtherMaster, new ClassHomeClaim(name, ClassFolderRemote.Port, takeOver, generation), rig.AgentPort);
        Assert.True(ok, detail);
    }

    // A colleague's PC sends that ended lesson to every Agent itself, as when the home's
    // Master is closed; each PC's Session then sets the lesson's files aside.
    private static ClassLesson EndOnTheAgents(ClassSyncRig rig, ClassLesson ended)
    {
        foreach (var pc in rig.Pcs)
        {
            var (ok, detail) = ClassFolderClient.SetLesson(pc.Machine, rig.OtherMaster, ended, rig.AgentPort);
            Assert.True(ok, detail);
            pc.Follow();
        }
        return ended;
    }
}
