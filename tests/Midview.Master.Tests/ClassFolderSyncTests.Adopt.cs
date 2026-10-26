using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderSync with two teacher PCs in one lab (plan decision 9):
// a lesson another PC ended straight on the Agents, while the home's Master was
// closed, is adopted by the home. The rig's Agents keep the home (homes).
public sealed partial class ClassFolderSyncTests
{
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

    // What is adopted is this lesson, ended when the Agents say, whatever else
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

    // An end saved more than a day ahead of this PC's clock is not believed.
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
