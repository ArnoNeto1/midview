using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderHandOver.TakeOverUnreachableAsync and what the PCs set aside for
// the old home's lessons: the confirmation promises "os arquivos que ainda
// estão nos PCs dos alunos vêm na próxima aula". The taking PC shares the rig's
// certificate (the rig's Sessions trust only it), with another computer name:
// another PC to the Agents.
public sealed partial class ClassFolderHandOverTests
{
    // The home's Master closed; a colleague ended its lesson straight on the
    // Agents (the files set aside on the PCs); the colleague takes the folders
    // over from the home that doesn't answer, with no classes of its own. At
    // its next lesson, of another class, what the PCs set aside for the old
    // home's lesson comes into a class of that name, never to the students'
    // Recycle Bins.
    [Fact]
    public async Task AfterAnUnreachableTakeOver_TheOldHomesSetAsideFiles_ComeInAtTheNextLesson()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var oldSync = rig.NewSync();
        await oldSync.StartAsync(Turma);
        var lesson = oldSync.Lesson;
        var pc1 = rig.Pcs[0];
        pc1.Follow();
        pc1.Write("novo.txt", new byte[] { 7, 7, 7 }, Earlier); // never reached the home
        oldSync.Dispose(); // the home's Master closed

        var ended = lesson with { SavedAt = lesson.SavedAt + 1, Ended = true };
        foreach (var pc in rig.Pcs)
        {
            Assert.True(ClassLessonOnAgents.Send(pc.Machine, rig.OtherMaster, ended, rig.AgentPort).Ok);
        }
        pc1.Follow();
        Assert.Contains(pc1.Folder.List().Entries, entry => entry.LessonId == lesson.LessonId);

        var root = Path.Combine(rig.Root, "prof-2");
        var store = new ClassStore(
            Path.Combine(root, "Turmas"), Path.Combine(root, "dados", "turmas"), new FolderBin(Path.Combine(root, "lixeira")),
            () => rig.Now, lessonsPath: Path.Combine(root, "dados", "class-lessons.json"));
        var homePath = Path.Combine(root, "dados", "class-home.json");
        using var sync = new ClassFolderSync(
            store, rig.Master, () => rig.Machines, _ => true, () => rig.Now, NewHome, agentPort: rig.AgentPort,
            sessionPort: rig.SessionPort, interval: TimeSpan.FromHours(1), lessonPath: Path.Combine(root, "dados", "class-lesson.json"),
            homePath: homePath);
        var taker = new ClassFolderHandOver(
            store, sync, rig.Master, NewHome, () => rig.Machines, _ => true, () => rig.Now, rig.AgentPort, homePath: homePath, log: _ => { });

        var result = await taker.TakeOverUnreachableAsync(OldHome);

        Assert.True(result.Ok, result.Detail);
        Assert.All(rig.Pcs, pc => Assert.Equal(NewHome, pc.Agent.CurrentHome?.Name));
        // The class of the lesson the room followed is here now, empty, for what its PCs set aside.
        Assert.True(store.Exists(Turma));
        Assert.Empty(store.Files(Turma));

        rig.Now = rig.Now.AddMinutes(5);
        Assert.True(store.Create("3º B").Ok);
        await sync.StartAsync("3º B");
        Assert.True(sync.Lesson.IsOn);
        await sync.RunRoundAsync(pc1.Machine, CancellationToken.None);

        // In the class; only then was the PC's copy cleared.
        Assert.Equal(new byte[] { 7, 7, 7 }, File.ReadAllBytes(Path.Combine(root, "Turmas", Turma, "novo.txt")));
        Assert.DoesNotContain(pc1.Folder.List().Entries, entry => entry.LessonId == lesson.LessonId);
        Assert.Empty(store.Files("3º B"));
    }
}
