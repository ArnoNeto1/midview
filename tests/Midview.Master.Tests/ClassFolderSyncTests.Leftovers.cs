using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderSync and what a PC set aside for a lesson this PC doesn't know
// (another teacher PC's, or this PC's own from before its data was set up
// again) or knows only as gone: kept on the PC while it may still be another
// home's, cleared to the student's Recycle Bin once old, or at once for a
// class deleted here, as before two teacher PCs.
public sealed partial class ClassFolderSyncTests
{
    // Kept while the newest of its files is at most 60 days old, then
    // cleared, without ever coming into a class.
    [Fact]
    public async Task SetAsideFilesOfAnUnknownLesson_AreClearedOnceTheirNewestIsOver60DaysOld()
    {
        using var rig = new ClassSyncRig();
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        var pc1 = rig.Pcs[0];
        var nowUtc = new DateTimeOffset(rig.Now).UtcDateTime;
        var old = Guid.NewGuid();
        var recent = Guid.NewGuid();
        SetAside(pc1, old, "velho.txt", nowUtc.AddDays(-61));
        SetAside(pc1, old, "mais-velho.txt", nowUtc.AddDays(-90));
        SetAside(pc1, recent, "novo.txt", nowUtc.AddDays(-59));
        SetAside(pc1, recent, "antigo.txt", nowUtc.AddDays(-200));

        await rig.Rounds(sync, 2);

        var entries = pc1.Folder.List().Entries;
        Assert.DoesNotContain(entries, entry => entry.LessonId == old);
        Assert.Equal(new[] { "antigo.txt", "novo.txt" }, entries.Where(entry => entry.LessonId == recent).Select(entry => entry.Path).Order());
        Assert.Equal(new[] { "mais-velho.txt", "velho.txt" }, pc1.Bin.FileNames());
        Assert.Empty(rig.StoreFiles(Turma));
    }

    // A class deleted here ("Excluir"): what a PC set aside for one of its
    // lessons, never collected, is cleared at the next round, as before.
    [Fact]
    public async Task SetAsideFilesOfADeletedClassesLesson_AreCleared()
    {
        using var rig = new ClassSyncRig();
        var sync = rig.NewSync();
        await sync.StartAsync("3º B");
        var lesson = sync.Lesson;
        var pc1 = rig.Pcs[0];
        pc1.Follow();
        pc1.Write("trabalho.txt", Bytes(10, 1), Utc(1)); // never reached this PC
        await sync.StartAsync(Turma); // 3º B ends uncollected
        pc1.Follow();
        Assert.Contains(pc1.Folder.List().Entries, entry => entry.LessonId == lesson.LessonId);
        Assert.True(rig.Store.Delete("3º B").Ok);

        await sync.RunRoundAsync(pc1.Machine, CancellationToken.None);

        Assert.DoesNotContain(pc1.Folder.List().Entries, entry => entry.LessonId == lesson.LessonId);
        Assert.Equal(new[] { "trabalho.txt" }, pc1.Bin.FileNames());
        Assert.False(rig.Store.Exists("3º B"));
    }

    // A file in the PC's set-aside folder of that lesson, written at utc.
    private static void SetAside(StudentPc pc, Guid lessonId, string name, DateTime utc) =>
        ClassSyncRig.WriteFile(Path.Combine(pc.Root, "guardada", lessonId.ToString("N"), name), Bytes(5, 3), utc);
}
