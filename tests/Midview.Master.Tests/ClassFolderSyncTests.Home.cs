using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderSync with two teacher PCs in one lab (plan decisions 2, 8 and 9):
// the claim on the class folders' home before a start, standing down when
// another PC keeps them, and adopting an end another PC made on the Agents.
// The rig's Agents keep the home (ClassSyncRig's homes).
public sealed partial class ClassFolderSyncTests
{
    private const string ThisPc = ClassSyncRig.MachineName;
    private const string OtherPc = "PROF-2";

    [Fact]
    public async Task StartAsync_ClaimsTheHome_ThenStarts()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var sync = rig.NewSync();

        var started = await sync.StartAsync(Turma);

        Assert.Equal(3, started.Count);
        Assert.All(started, result => Assert.True(result.Ok, $"{result.Machine.Name}: {result.Detail}"));
        foreach (var pc in rig.Pcs)
        {
            Assert.Equal(sync.Lesson, pc.Agent.Current);
            var home = pc.Agent.CurrentHome;
            Assert.NotNull(home);
            Assert.Equal(ThisPc, home!.Name);
            Assert.Equal(rig.Master.Thumbprint, home.Thumbprint, ignoreCase: true);
            Assert.Equal(ClassFolderRemote.Port, home.Port);
        }
    }

    // Another teacher PC of the lab, with another teacher's certificate or the
    // same one (two PCs can share one teacher identity): the room says no.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASecondTeacherPc_IsRefusedNamingTheHome_AndSavesNoLesson(bool sameCertificate)
    {
        using var rig = new ClassSyncRig(homes: 3);
        var home = rig.NewSync();
        await home.StartAsync(Turma);
        var lesson = home.Lesson;
        var otherLessonPath = Path.Combine(rig.Root, "outro-professor", "class-lesson.json");
        var other = rig.NewSync(
            machineName: OtherPc, master: sameCertificate ? rig.Master : rig.OtherMaster, lessonPath: otherLessonPath);
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
        Assert.False(File.Exists(otherLessonPath));
        Assert.All(rig.Pcs, pc => Assert.Equal(lesson, pc.Agent.Current));
        Assert.All(rig.Pcs, pc => Assert.Equal(ThisPc, pc.Agent.CurrentHome?.Name));
    }

    // Most of the room keeps another PC as home: the start is cancelled even though one Agent had none.
    [Fact]
    public async Task MostAgentsNamingAnotherHome_CancelTheStart()
    {
        using var rig = new ClassSyncRig(homes: 3);
        foreach (var pc in rig.Pcs.Take(2))
        {
            ClaimFor(rig, pc, OtherPc, takeOver: false);
        }
        var sync = rig.NewSync();

        var refused = await sync.StartAsync(Turma);

        Assert.Equal(new[] { "PC1", "PC2" }, refused.Select(result => result.Machine.Name).Order());
        Assert.All(refused, result => Assert.Equal(ClassHomeClaim.OtherHomeText + OtherPc, result.Detail));
        Assert.False(sync.Lesson.IsOn);
        Assert.Null(ClassLessonFile.Read(rig.LessonPath));
        Assert.All(rig.Pcs, pc => Assert.False(pc.Agent.Current.IsOn));
    }

    // A PC that another teacher PC claimed between two lessons (it was off when this PC's
    // lessons started, say) comes back at this PC's next start: the rest of the room says so.
    [Fact]
    public async Task AStraggler_ThatAnotherPcClaimed_IsTakenBackByTheHomesNextStart()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        await sync.EndAsync(EndChoice.Keep);
        var pc3 = rig.Pcs[2];
        ClaimFor(rig, pc3, OtherPc, takeOver: true);
        Assert.Equal(OtherPc, pc3.Agent.CurrentHome?.Name);

        var started = await sync.StartAsync("3º B");

        Assert.Equal(3, started.Count);
        Assert.All(started, result => Assert.True(result.Ok, $"{result.Machine.Name}: {result.Detail}"));
        Assert.All(rig.Pcs, pc => Assert.Equal(sync.Lesson, pc.Agent.Current));
        Assert.All(rig.Pcs, pc => Assert.Equal(ThisPc, pc.Agent.CurrentHome?.Name));
    }

    // Agents from before two teacher PCs take any start, as in part 1.
    [Fact]
    public async Task OldAgents_CountAsAccepting()
    {
        using var rig = new ClassSyncRig(homes: 1);
        ClaimFor(rig, rig.Pcs[0], OtherPc, takeOver: false);
        var sync = rig.NewSync();

        var started = await sync.StartAsync(Turma);

        Assert.True(sync.Lesson.IsOn);
        Assert.Equal(3, started.Count);
        Assert.All(started, result => Assert.True(result.Ok, $"{result.Machine.Name}: {result.Detail}"));
        Assert.Equal(ThisPc, rig.Pcs[0].Agent.CurrentHome?.Name);
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

        sync.StandDown();
        sync.StandDown();

        Assert.False(sync.Lesson.IsOn);
        Assert.Equal(Guid.Empty, sync.Lesson.LessonId);
        Assert.True(sync.Lesson.SavedAt >= lesson.SavedAt);
        Assert.Equal(sync.Lesson, ClassLessonFile.Read(rig.LessonPath));
        Assert.Equal(new[] { sync.Lesson }, changes);
        Assert.Equal(new[] { "mapa.png" }, rig.StoreFiles());
        Assert.False(rig.Store.IsCollected(lesson.LessonId));

        // No more rounds: what a student saves stays on their PC.
        rig.Pcs[0].Write("novo.txt", Bytes(10, 2), Utc(2));
        await rig.Rounds(sync, 2);
        Assert.Equal(new[] { "mapa.png" }, rig.StoreFiles());
        Assert.All(rig.Pcs.Skip(1), pc => Assert.DoesNotContain("novo.txt", pc.Files()));

        // The folders back on this PC: it starts lessons again.
        var again = await sync.StartAsync("3º B");
        Assert.All(again, result => Assert.True(result.Ok, $"{result.Machine.Name}: {result.Detail}"));
        Assert.True(sync.Lesson.IsOn);
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
        var ended = EndOnTheAgents(rig, lesson);
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

    [Fact]
    public async Task AHomeOpenedAfterAnotherPcEndedItsLesson_AdoptsTheEndByItself()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var closed = rig.NewSync();
        await closed.StartAsync(Turma);
        var lesson = closed.Lesson;
        closed.Dispose();
        var ended = EndOnTheAgents(rig, lesson);

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
        var other = new ClassLesson(lesson.SavedAt + 5, Guid.NewGuid(), Turma, lesson.StartedAt, Ended: true);
        foreach (var pc in rig.Pcs)
        {
            Assert.True(ClassFolderClient.SetLesson(pc.Machine, rig.OtherMaster, other, rig.AgentPort).Succeeded);
        }
        var changes = new List<ClassLesson>();
        sync.LessonChanged += changes.Add;

        await sync.AdoptAgentsEndAsync();

        Assert.Equal(lesson, sync.Lesson);
        Assert.Empty(changes);
    }

    // What another teacher PC's claim does on that Agent.
    private static void ClaimFor(ClassSyncRig rig, StudentPc pc, string name, bool takeOver)
    {
        var (ok, detail, _) = ClassFolderClient.ClaimHome(
            pc.Machine, rig.OtherMaster, new ClassHomeClaim(name, ClassFolderRemote.Port, takeOver), rig.AgentPort);
        Assert.True(ok, detail);
    }

    // A colleague's PC ends the lesson on every Agent itself, as when the home's Master is closed.
    private static ClassLesson EndOnTheAgents(ClassSyncRig rig, ClassLesson lesson)
    {
        var ended = lesson with { SavedAt = lesson.SavedAt + 5, Ended = true };
        foreach (var pc in rig.Pcs)
        {
            var (ok, detail) = ClassFolderClient.SetLesson(pc.Machine, rig.OtherMaster, ended, rig.AgentPort);
            Assert.True(ok, detail);
            pc.Follow(); // the Session sets the lesson's files aside
        }
        return ended;
    }
}
