using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderSync with another teacher PC in the lab: what the PCs set aside
// for that PC's lessons is that PC's to bring in, never this one's to clear;
// its end of its own lesson never takes a PC out of this home's; and a PC
// that left this home's lesson is asked again, not taken to be in it.
public sealed partial class ClassFolderSyncTests
{
    // PROF-2 (the rig's OtherMaster, a store of its own) is the home. PROF-1
    // (the rig's Master) opened again with a lesson of its own still on file,
    // before its role found out that PROF-2 keeps the folders: its rounds leave
    // alone what a PC set aside for PROF-2's lesson, which it never knew.
    [Fact]
    public async Task ARoundOfAStaleHome_LeavesWhatAPcSetAsideForTheHomesLesson()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var root = Path.Combine(rig.Root, "outro-professor");
        var store = new ClassStore(
            Path.Combine(root, "Turmas"), Path.Combine(root, "dados", "turmas"), new FolderBin(Path.Combine(root, "lixeira")),
            () => rig.Now, lessonsPath: Path.Combine(root, "dados", "class-lessons.json"));
        Assert.True(store.Create(Turma).Ok);
        var home = new ClassFolderSync(
            store, rig.OtherMaster, () => rig.Machines, _ => true, () => rig.Now, OtherPc, agentPort: rig.AgentPort,
            sessionPort: rig.SessionPort, interval: TimeSpan.FromHours(1), lessonPath: OtherLessonPath(rig),
            homePath: OtherHomePath(rig));
        ClassLesson theirs;
        try
        {
            Assert.All(await home.StartAsync(Turma), line => Assert.True(line.Ok, $"{line.Machine.Name}: {line.Detail}"));
            theirs = home.Lesson;
        }
        finally
        {
            home.Dispose(); // the home's Master closed
        }
        var pc1 = rig.Pcs[0];
        pc1.Follow();
        pc1.Write("trabalho.txt", Bytes(10, 1), Utc(1)); // never reached the home
        EndOnTheAgents(rig, theirs with { SavedAt = theirs.SavedAt + 5, Ended = true });
        Assert.Contains(pc1.Folder.List().Entries, entry => entry.LessonId == theirs.LessonId);

        var now = new DateTimeOffset(rig.Now).ToUnixTimeSeconds();
        ClassLessonFileMaster.Write(new ClassLesson(theirs.SavedAt - 60, Guid.NewGuid(), "3º B", now - 60, Ended: false), rig.LessonPath);
        var stale = rig.NewSync();
        Assert.True(stale.Lesson.IsOn);

        await stale.RunRoundAsync(pc1.Machine, CancellationToken.None);

        Assert.Equal(
            new[] { "trabalho.txt" },
            pc1.Folder.List().Entries.Where(entry => entry.LessonId == theirs.LessonId).Select(entry => entry.Path));
        Assert.Empty(pc1.Bin.FileNames());
        Assert.Empty(rig.StoreFiles(Turma));
        Assert.Empty(rig.StoreFiles("3º B"));
    }

    // Another teacher PC hands a PC the end of ITS lesson, saved after this
    // home's lesson started (a lesson it held, ended): the PC stays in the
    // home's lesson, and what its student saved reaches the class, through the
    // end ("manter") and the next lesson.
    [Fact]
    public async Task AnotherTeacherPcsEndOfItsLesson_LeavesThePcInTheHomesLesson()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var home = rig.NewSync();
        await home.StartAsync(Turma);
        var lesson = home.Lesson;
        var pc1 = rig.Pcs[0];
        await rig.Rounds(home, 1);
        pc1.Follow();
        pc1.Write("trabalho.txt", Bytes(10, 1), Utc(1));
        var at = lesson.SavedAt + 60;
        var theirs = new ClassLesson(at, Guid.NewGuid(), "3º B", at - 600, Ended: true);

        var (ok, detail) = ClassLessonOnAgents.Send(pc1.Machine, rig.OtherMaster, theirs, rig.AgentPort);
        pc1.Follow();

        Assert.False(ok);
        Assert.Equal($"{ClassLesson.KeptNewerText}: aula do {Turma}", detail);
        Assert.Equal(lesson, pc1.Agent.Current);
        rig.Now = rig.Now.AddMinutes(2);
        await rig.Rounds(home, 3);
        Assert.Equal(new[] { "trabalho.txt" }, rig.StoreFiles(Turma));
        await home.EndAsync(EndChoice.Keep);
        rig.Now = rig.Now.AddMinutes(2);
        await home.StartAsync("3º B");
        await home.RunRoundAsync(pc1.Machine, CancellationToken.None);

        Assert.Equal(Bytes(10, 1), File.ReadAllBytes(rig.StorePath("trabalho.txt")));
        Assert.Equal(home.Lesson, pc1.Agent.Current);
        Assert.DoesNotContain(pc1.Folder.List().Entries, entry => entry.LessonId == lesson.LessonId);
    }

    // A PC whose Agent took this lesson once but follows another one now (a
    // teacher PC with the same certificate handed it a newer lesson): the
    // round asks the Agent again rather than waiting for its Session, and
    // the PC's line says why it isn't in the lesson.
    [Fact]
    public async Task APcThatLeftTheLessonItTook_IsAskedAgain_AndItsLineSaysWhy()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var home = rig.NewSync();
        var statuses = ClassSyncRig.Watch(home);
        await home.StartAsync(Turma);
        var pc1 = rig.Pcs[0];
        await rig.Rounds(home, 1);
        Assert.Equal(PcSyncState.InSync, statuses[pc1.Name].State);
        var at = home.Lesson.SavedAt + 60;
        var theirs = new ClassLesson(at, Guid.NewGuid(), "3º B", at, Ended: false);
        Assert.True(ClassLessonOnAgents.Send(pc1.Machine, rig.Master, theirs, rig.AgentPort).Ok);

        await home.RunRoundAsync(pc1.Machine, CancellationToken.None);

        Assert.Equal(theirs, pc1.Agent.Current);
        Assert.Equal(
            (PcSyncState.Failed, $"{ClassLesson.KeptNewerText}: aula do 3º B"), (statuses[pc1.Name].State, statuses[pc1.Name].Detail));
    }
}
