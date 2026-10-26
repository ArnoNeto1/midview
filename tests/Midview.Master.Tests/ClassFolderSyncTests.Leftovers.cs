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
    // Kept until 60 days after the later of when this PC first saw them on
    // that PC and the newest file's time, then cleared, without ever coming
    // into a class: files that kept old dates (from a USB stick, a zip, the
    // teacher's own material) aren't old at the first look. The first look is
    // remembered across a Master closed and opened again.
    [Fact]
    public async Task SetAsideFilesOfAnUnknownLesson_AreCleared60DaysAfterTheyWereFirstSeen()
    {
        using var rig = new ClassSyncRig();
        var sync = rig.NewSync();
        await sync.StartAsync(Turma);
        var pc1 = rig.Pcs[0];
        var day0 = new DateTimeOffset(rig.Now).UtcDateTime;
        var start = rig.Now;
        var old = Guid.NewGuid(); // old-dated files, first seen on day 0
        var touched = Guid.NewGuid(); // the same, and one more written on day 10
        var later = Guid.NewGuid(); // old-dated files, first seen on day 30
        SetAside(pc1, old, "velho.txt", day0.AddDays(-200));
        SetAside(pc1, touched, "antigo.txt", day0.AddDays(-200));

        await sync.RunRoundAsync(pc1.Machine, CancellationToken.None);

        Assert.Equal(2, pc1.Folder.List().Entries.Count(entry => entry.LessonId == old || entry.LessonId == touched));
        Assert.Empty(pc1.Bin.FileNames());
        SetAside(pc1, touched, "novo.txt", day0.AddDays(10));
        rig.Now = start.AddDays(30);
        SetAside(pc1, later, "outro.txt", day0.AddDays(-300));
        await sync.RunRoundAsync(pc1.Machine, CancellationToken.None);
        sync.Dispose(); // the Master closed

        rig.Now = start.AddDays(61);
        var reopened = rig.NewSync();
        await reopened.StartAsync("3º B");
        await reopened.RunRoundAsync(pc1.Machine, CancellationToken.None);

        var entries = pc1.Folder.List().Entries;
        Assert.DoesNotContain(entries, entry => entry.LessonId == old);
        Assert.Equal(new[] { "antigo.txt", "novo.txt" }, entries.Where(entry => entry.LessonId == touched).Select(entry => entry.Path).Order());
        Assert.Contains(entries, entry => entry.LessonId == later);
        Assert.Equal(new[] { "velho.txt" }, pc1.Bin.FileNames());

        rig.Now = start.AddDays(72);
        await reopened.RunRoundAsync(pc1.Machine, CancellationToken.None);

        Assert.DoesNotContain(pc1.Folder.List().Entries, entry => entry.LessonId == touched);
        Assert.Contains(pc1.Folder.List().Entries, entry => entry.LessonId == later);
        Assert.Empty(rig.StoreFiles(Turma));
        Assert.Empty(rig.StoreFiles("3º B"));
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
