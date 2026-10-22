using Midview.Master;
using Midview.Protocol;
using Xunit;

// LocalClassFolderController: what the "Pasta da turma" window did with the
// store and the sync (creating, renaming, deleting, starting, ending, reading
// the disk and the PCs' states), now through IClassFolderController, against
// ClassSyncRig's loopback student PCs and temp store. And the guards a second
// window (a colleague's PC, through ClassFolderHost) needs: one command at a
// time, never a start over a lesson on, never an end of another lesson.
public sealed class LocalClassFolderControllerTests
{
    private const string Turma = ClassSyncRig.Turma;

    private static DateTime Utc(int minutes) => new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc).AddMinutes(minutes);

    private static byte[] Bytes(int length, byte seed) =>
        Enumerable.Range(0, length).Select(i => (byte)(i * 31 + seed)).ToArray();

    // The home PC's controller over the rig's store and that sync, on the rig's clock.
    internal static LocalClassFolderController NewController(ClassSyncRig rig, ClassFolderSync sync) =>
        new(rig.Store, sync, () => rig.Machines, machine => !rig.Offline.ContainsKey(machine.Name), () => rig.Now,
            ClassSyncRig.MachineName);

    [Fact]
    public async Task CreateRenameAndDelete_GoToTheStore_AsTheWindowDidThem()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var controller = NewController(rig, rig.NewSync());

        Assert.Equal((true, string.Empty, "Turma 1-2"), await controller.CreateAsync("Turma 1/2"));
        Assert.True(rig.Store.Exists("Turma 1-2"));
        var taken = await controller.CreateAsync("Turma 1/2");
        Assert.False(taken.Ok);
        Assert.NotEmpty(taken.Detail);
        Assert.Equal(string.Empty, taken.Turma);

        Assert.Equal((true, string.Empty), await controller.RenameAsync("Turma 1-2", "9º A"));
        Assert.False(rig.Store.Exists("Turma 1-2"));
        Assert.True(rig.Store.Exists("9º A"));

        rig.WriteInStore("mapa.png", Bytes(10, 1), Utc(1), "9º A");
        Assert.Equal((true, string.Empty), await controller.DeleteAsync("9º A"));
        Assert.False(rig.Store.Exists("9º A"));
        Assert.Equal(new[] { "mapa.png" }, rig.TeacherBin.FileNames());

        var gone = await controller.DeleteAsync("9º A");
        Assert.False(gone.Ok);
        Assert.NotEmpty(gone.Detail);
    }

    [Fact]
    public async Task TheLessonsClass_IsNeitherRenamedNorDeleted()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var controller = NewController(rig, rig.NewSync());
        await controller.StartAsync(Turma);

        Assert.Equal((false, ClassFolderText.LessonClassNotRenamed), await controller.RenameAsync(Turma, "9º A"));
        Assert.Equal((false, ClassFolderText.LessonClassNotDeleted), await controller.DeleteAsync(Turma));
        Assert.True(rig.Store.Exists(Turma));
        Assert.False(rig.Store.Exists("9º A"));
        Assert.True(controller.Lesson.IsOn);
    }

    [Fact]
    public async Task StartAndEnd_SayWhatEachPcDid_ByName_AndTellTheWindow()
    {
        using var rig = new ClassSyncRig();
        var sync = rig.NewSync();
        var controller = NewController(rig, sync);
        var changes = 0;
        controller.Changed += () => Interlocked.Increment(ref changes);

        var started = await controller.StartAsync(Turma);

        Assert.Equal(new[] { "PC1", "PC2", "PC3" }, started.Select(line => line.Name));
        Assert.All(started, line => Assert.True(line.Ok, $"{line.Name}: {line.Detail}"));
        Assert.True(controller.Lesson.IsOn);
        Assert.Equal(Turma, controller.Lesson.Turma);
        Assert.Equal(sync.Lesson, controller.Lesson);
        Assert.True(Volatile.Read(ref changes) > 0);
        Assert.Null(controller.HomeName);
        Assert.False(controller.HomeClosed);

        rig.Pcs[0].Write("hoje.txt", Bytes(10, 2), Utc(1));
        await rig.Rounds(sync, 2);
        Assert.Equal(new[] { "PC1", "PC2", "PC3" }, controller.Pcs.Select(pc => pc.Name));
        Assert.All(controller.Pcs, pc => Assert.Equal(nameof(PcSyncState.InSync), pc.State));

        var before = Volatile.Read(ref changes);
        var ended = await controller.EndAsync(Turma, EndChoice.Keep);

        Assert.Equal(new[] { "PC1", "PC2", "PC3" }, ended.Select(line => line.Name));
        Assert.All(ended, line => Assert.True(line.Ok, $"{line.Name}: {line.Detail}"));
        Assert.False(controller.Lesson.IsOn);
        Assert.Equal(new[] { "hoje.txt" }, rig.StoreFiles());
        Assert.Empty(controller.LastEndProblems);
        Assert.True(Volatile.Read(ref changes) > before);
    }

    // The window lists every PC of the classroom: as the sync last saw it, or,
    // before its first round of this lesson, copying when it answers and off when not.
    [Fact]
    public async Task Pcs_BeforeTheirFirstRound_AreCopyingOrOff_ThenAsTheSyncSawThem()
    {
        using var rig = new ClassSyncRig(pcs: 2);
        var sync = rig.NewSync();
        var controller = NewController(rig, sync);
        rig.Offline[rig.Pcs[1].Name] = true;
        await controller.StartAsync(Turma);

        var pcs = controller.Pcs;
        Assert.Equal(new[] { "PC1", "PC2" }, pcs.Select(pc => pc.Name));
        Assert.Equal(nameof(PcSyncState.Offline), pcs[1].State);

        await rig.Rounds(sync, 2);

        Assert.Equal(nameof(PcSyncState.InSync), controller.Pcs[0].State);
        Assert.Equal(nameof(PcSyncState.Offline), controller.Pcs[1].State);
    }

    // A second window (a colleague's) may still show the list after this one started a lesson.
    [Fact]
    public async Task AStartWhileALessonIsOn_IsRefused_AndChangesNothing()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var controller = NewController(rig, rig.NewSync());
        await controller.StartAsync(Turma);
        var lesson = controller.Lesson;

        var refused = await Assert.ThrowsAsync<ClassFolderRefusedException>(() => controller.StartAsync("3º B"));

        Assert.Equal(ClassFolderText.LessonAlreadyOn(Turma), refused.Message);
        Assert.Equal(lesson, controller.Lesson);
        Assert.Equal(lesson, rig.Pcs[0].Agent.Current);
    }

    // A second window may still show a lesson this one ended (and started another).
    [Fact]
    public async Task AnEndOfALessonThatIsNotOn_IsRefused_AndChangesNothing()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        rig.WriteInStore("mapa.png", Bytes(10, 1), Utc(1));
        var controller = NewController(rig, rig.NewSync());
        await controller.StartAsync(Turma);
        var lesson = controller.Lesson;

        var other = await Assert.ThrowsAsync<ClassFolderRefusedException>(() => controller.EndAsync("3º B", EndChoice.DeleteAll));
        Assert.Equal(ClassFolderText.LessonAlreadyOver("3º B"), other.Message);
        Assert.Equal(lesson, controller.Lesson);

        await controller.EndAsync(Turma, EndChoice.Keep);
        var again = await Assert.ThrowsAsync<ClassFolderRefusedException>(() => controller.EndAsync(Turma, EndChoice.DeleteAll));
        Assert.Equal(ClassFolderText.LessonAlreadyOver(Turma), again.Message);
        Assert.Equal(new[] { "mapa.png" }, rig.StoreFiles());
    }

    [Fact]
    public async Task AStartOfAClassThatIsGone_IsRefused()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var controller = NewController(rig, rig.NewSync());

        var refused = await Assert.ThrowsAsync<ClassFolderRefusedException>(() => controller.StartAsync("9º Z"));

        Assert.Equal(ClassFolderText.ClassGone, refused.Message);
        Assert.False(controller.Lesson.IsOn);
    }

    // Another teacher PC keeps the class folders: the start is cancelled, the
    // Agents' refusals are the lines, and no lesson is on (the window looks).
    [Fact]
    public async Task AStartWhileAnotherPcIsTheHome_ReturnsTheRefusals_AndNoLesson()
    {
        using var rig = new ClassSyncRig(homes: 3);
        var other = rig.NewSync(
            machineName: "PROF-2", master: rig.OtherMaster, lessonPath: Path.Combine(rig.Root, "outro", "class-lesson.json"),
            homePath: Path.Combine(rig.Root, "outro", "class-home.json"));
        await other.StartAsync(Turma);
        await other.EndAsync(EndChoice.Keep);
        var controller = NewController(rig, rig.NewSync());

        var refused = await controller.StartAsync("3º B");

        Assert.Equal(new[] { "PC1", "PC2", "PC3" }, refused.Select(line => line.Name));
        Assert.All(refused, line =>
        {
            Assert.False(line.Ok);
            Assert.Equal(ClassHomeClaim.OtherHomeText + "PROF-2", line.Detail);
        });
        Assert.False(controller.Lesson.IsOn);
    }

    [Fact]
    public async Task Read_GivesTheClassList_OrTheLessonsNumbers()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        rig.FreeSpace = 54_321_000_000; // room to pull the files
        // From an earlier lesson: it arrived on the day of its own time.
        rig.WriteInStore("antigo.pdf", Bytes(30, 1), new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));
        var sync = rig.NewSync();
        var controller = NewController(rig, sync);

        var list = await controller.ReadAsync(lessonFiles: false);

        Assert.Equal(ClassSyncRig.MachineName, list.HomeName);
        Assert.False(list.Lesson.ToLesson().IsOn);
        Assert.Equal(new[] { Turma, "3º B" }, list.Classes.Select(summary => summary.Turma));
        Assert.Equal((1, 30L), (list.Classes[0].Files, list.Classes[0].Bytes));
        Assert.Null(list.LessonFiles);
        Assert.Equal(54_321_000_000, list.FreeBytes);
        Assert.Equal(new[] { "PC1" }, list.Pcs.Select(pc => pc.Name));
        Assert.Null((await controller.ReadAsync(lessonFiles: true)).LessonFiles); // no lesson on

        await controller.StartAsync(Turma);
        rig.Pcs[0].Write("hoje.txt", Bytes(10, 2), Utc(1));
        await rig.Rounds(sync, 3);
        Assert.Equal(new[] { "antigo.pdf", "hoje.txt" }, rig.StoreFiles());
        var lesson = await controller.ReadAsync(lessonFiles: true);

        Assert.Equal(controller.Lesson, lesson.Lesson.ToLesson());
        Assert.Empty(lesson.Classes);
        Assert.Equal(new LessonFilesDto(Turma, 2, 40, 1, 10), lesson.LessonFiles);
        Assert.Equal(54_321_000_000, lesson.FreeBytes);
    }

    // The window's clicks and a colleague's commands go one at a time: a class
    // created while a lesson is ending waits for the end.
    [Fact]
    public async Task Commands_RunOneAtATime()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var sync = rig.NewSync();
        var controller = NewController(rig, sync);
        await controller.StartAsync(Turma);
        rig.Pcs[0].Write("ultimo.txt", Bytes(10, 3), Utc(2));
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        sync.CopyingStarts = _ =>
        {
            reached.Set();
            release.Wait(TimeSpan.FromSeconds(60));
        };

        var ending = Task.Run(() => controller.EndAsync(Turma, EndChoice.Keep));
        Assert.True(reached.Wait(TimeSpan.FromSeconds(30)));
        var creating = Task.Run(() => controller.CreateAsync("9º A"));

        await Task.WhenAny(creating, Task.Delay(500));
        Assert.False(creating.IsCompleted);
        Assert.False(rig.Store.Exists("9º A"));

        release.Set();
        var ended = await ending.WaitAsync(TimeSpan.FromSeconds(60));
        var created = await creating.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(Assert.Single(ended).Ok);
        Assert.True(created.Ok, created.Detail);
        Assert.Equal(new[] { "ultimo.txt" }, rig.StoreFiles());
    }
}
