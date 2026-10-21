using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Midview.Protocol;
using Midview.Session;
using Xunit;

public partial class ClassFolderTests
{
    [Fact]
    public void Follow_NewLesson_ShowsTheNoticeOnce()
    {
        MakeLessonFolder();
        var display = new RecordingDisplay();
        var classFolder = NewClassFolder();

        classFolder.Follow(display);
        classFolder.Follow(display);

        var notice = "Aula do 3º A" + Environment.NewLine
            + "O que você baixar ou salvar na Pasta da turma – 3º A (na Área de Trabalho) aparece em todos os computadores da sala.";
        Assert.Equal(new[] { notice }, display.Shown);
        Assert.Equal((LessonId, "3º A"), Remembered());

        // The Session starting again in the middle of the lesson doesn't say it again.
        NewClassFolder().Follow(display);
        Assert.Single(display.Shown);
    }

    [Fact]
    public void Follow_WithNoLessonOnAndNothingBefore_DoesNothing()
    {
        _lesson = ClassLesson.None(1);
        var display = new RecordingDisplay();

        NewClassFolder().Follow(display);

        Assert.Empty(display.Shown);
        Assert.False(File.Exists(StatePath));
        Assert.False(Directory.Exists(_setAside));
    }

    [Fact]
    public void Follow_WithNoDisplay_StillRemembersTheLesson()
    {
        var classFolder = NewClassFolder();
        classFolder.Follow(null);

        var display = new RecordingDisplay();
        classFolder.Follow(display);

        Assert.Empty(display.Shown);
        Assert.Equal((LessonId, "3º A"), Remembered());
    }

    [Fact]
    public void Follow_LessonEnded_MovesFilesAsideAndRemovesTheFolder()
    {
        var folder = MakeLessonFolder();
        var display = new RecordingDisplay();
        var classFolder = NewClassFolder();
        classFolder.Follow(display);
        var id = LessonId;
        Write(folder, "a.png", "A");
        Write(folder, "sub/b.txt", "B");
        _lesson = _lesson! with { Ended = true };

        classFolder.Follow(display);

        Assert.False(Directory.Exists(folder));
        Assert.Equal("A", File.ReadAllText(Path.Combine(SetAsideFolder(id), "a.png")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(SetAsideFolder(id), "sub", "b.txt")));
        Assert.Equal((Guid.Empty, ""), Remembered());
        Assert.Single(display.Shown); // the start of the lesson; the end has no notice

        var (current, problem, entries) = classFolder.List();
        Assert.Equal(Guid.Empty, current);
        Assert.Null(problem);
        Assert.Equal(new[] { (id, "a.png"), (id, "sub/b.txt") }, entries.Select(e => (e.LessonId, e.Path)));

        // Nothing left to do the next time.
        classFolder.Follow(display);
        Assert.Equal(2, Directory.GetFiles(SetAsideFolder(id), "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void Follow_LessonEnded_SetsAsideOnlyWhatTheListingShows_AndRecyclesTheRestWithTheFolder()
    {
        var folder = MakeLessonFolder();
        var classFolder = NewClassFolder();
        classFolder.Follow(null);
        var id = LessonId;
        Write(folder, "a.png", "A");
        Write(folder, "b.crdownload");
        Write(folder, "~$c.docx");
        _lesson = _lesson! with { Ended = true };

        classFolder.Follow(null);

        var aside = SetAsideFolder(id);
        Assert.Equal(new[] { Path.Combine(aside, "a.png") }, Directory.GetFiles(aside, "*", SearchOption.AllDirectories));
        Assert.Equal(new[] { folder }, _bin.Recycled);
        Assert.Equal(new[] { "b.crdownload", "~$c.docx" }, _bin.Contents[folder]);
        Assert.False(Directory.Exists(folder));
        Assert.Equal((Guid.Empty, ""), Remembered());
    }

    // What is set aside is what the Master was shown: the very same filters, not a copy of them.
    [Fact]
    public void Follow_LessonEnded_SetsAsideExactlyWhatTheListingHadShown()
    {
        var folder = MakeLessonFolder();
        var kept = new[] { "a.png", "sub/b.txt", "sub/fundo/c.txt" };
        foreach (var path in kept)
        {
            Write(folder, path, path);
        }
        foreach (var path in new[] { "x.crdownload", "D.CRDOWNLOAD", "y.tmp", "z.part", "w.parcial", "~$doc.docx",
                     ".midview-q.parcial", "sub/p.tmp" })
        {
            Write(folder, path);
        }
        File.SetAttributes(Write(folder, "oculto.txt"), FileAttributes.Hidden);
        File.SetAttributes(Write(folder, "sistema.txt"), FileAttributes.System);
        Write(folder, "escondida/dentro.txt");
        File.SetAttributes(Path.Combine(folder, "escondida"), FileAttributes.Hidden);
        // Over the limits the wire and the other messages have.
        Write(folder, new string('a', FileNames.MaxNameLength) + ".txt");
        Write(folder, string.Join("/", Enumerable.Repeat(new string('あ', 100), 4)) + "/fundo.txt");
        Write(folder, string.Join("/", Enumerable.Range(1, FileCollection.MaxDepth + 2).Select(level => $"n{level}")) + "/fundo.txt");
        using (var stream = File.Create(Path.Combine(folder, "enorme.mp4")))
        {
            stream.SetLength(FileTransfer.MaxFileSize + 1);
        }
        var classFolder = NewClassFolder();
        classFolder.Follow(null);
        var id = LessonId;
        Assert.Equal(kept, classFolder.List().Entries.Select(e => e.Path));
        _lesson = _lesson! with { Ended = true };

        classFolder.Follow(null);

        var aside = SetAsideFolder(id);
        var onDisk = Directory.GetFiles(aside, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(aside, file).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal);
        Assert.Equal(kept.OrderBy(path => path, StringComparer.Ordinal), onDisk);
        Assert.Equal(kept, classFolder.List().Entries.Select(e => e.Path));
        Assert.Equal(new[] { folder }, _bin.Recycled);
        Assert.False(Directory.Exists(folder));
    }

    // A failed read of the lesson file mid-lesson is not the end of the lesson: nothing leaves the Desktop folder.
    [Fact]
    public void Follow_WithAnUnknownLesson_DoesNothing()
    {
        var folder = MakeLessonFolder();
        var display = new RecordingDisplay();
        var classFolder = NewClassFolder();
        classFolder.Follow(display);
        var lesson = _lesson;
        Write(folder, "a.png", "A");
        _lesson = null;

        classFolder.Follow(display);

        Assert.Equal("A", File.ReadAllText(Path.Combine(folder, "a.png")));
        Assert.False(Directory.Exists(_setAside));
        Assert.Empty(_bin.Recycled);
        Assert.Single(display.Shown);
        Assert.Equal((lesson!.LessonId, "3º A"), Remembered());

        // The lesson is read again, as it was: still nothing to do, and no second notice.
        _lesson = lesson;
        classFolder.Follow(display);
        Assert.True(File.Exists(Path.Combine(folder, "a.png")));
        Assert.Single(display.Shown);

        // A lesson that really ended is an ended lesson.
        _lesson = lesson with { Ended = true };
        classFolder.Follow(display);
        Assert.Equal("A", File.ReadAllText(Path.Combine(SetAsideFolder(lesson.LessonId), "a.png")));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Follow_WithAnUnknownLesson_AndNothingBefore_DoesNothing()
    {
        MakeLessonFolder();
        var display = new RecordingDisplay();
        var classFolder = NewClassFolder();
        _lesson = null;

        classFolder.Follow(display);

        Assert.Empty(display.Shown);
        Assert.False(File.Exists(StatePath));
    }
    [Fact]
    public void Follow_LessonEnded_WithAnEmptyFolder_LeavesNoSetAsideFolder()
    {
        var folder = MakeLessonFolder();
        Directory.CreateDirectory(Path.Combine(folder, "vazia"));
        var classFolder = NewClassFolder();
        classFolder.Follow(null);
        var id = LessonId;
        _lesson = _lesson! with { Ended = true };

        classFolder.Follow(null);

        Assert.False(Directory.Exists(folder));
        Assert.False(Directory.Exists(SetAsideFolder(id)));
        Assert.Equal((Guid.Empty, ""), Remembered());
    }

    [Fact]
    public void Follow_LessonEnded_AndTheFolderWasRemovedAlready_JustMovesOn()
    {
        var folder = MakeLessonFolder();
        var classFolder = NewClassFolder();
        classFolder.Follow(null);
        Directory.Delete(folder);
        _lesson = _lesson! with { Ended = true };

        classFolder.Follow(null);

        Assert.Equal((Guid.Empty, ""), Remembered());
        Assert.False(Directory.Exists(_setAside));
    }

    [Fact]
    public void Follow_FolderStillProtected_RetriesLater()
    {
        var folder = MakeLessonFolder();
        var classFolder = NewClassFolder();
        classFolder.Follow(null);
        var id = LessonId;
        Write(folder, "a.png");
        Protect(folder);
        _lesson = _lesson! with { Ended = true };

        classFolder.Follow(null);

        // What the student saved is already safe, the folder waits for the Agent to release it.
        Assert.True(Directory.Exists(folder));
        Assert.True(File.Exists(Path.Combine(SetAsideFolder(id), "a.png")));
        Assert.Equal((Guid.Empty, ""), Remembered());
        Assert.Equal(new[] { (id, "3º A") }, Pending());

        // Saved in the meantime: goes aside as well.
        Write(folder, "tarde.txt");
        classFolder.Follow(null);
        Assert.True(File.Exists(Path.Combine(SetAsideFolder(id), "tarde.txt")));
        Assert.Equal(new[] { (id, "3º A") }, Pending());

        Unprotect(folder);
        classFolder.Follow(null);

        Assert.False(Directory.Exists(folder));
        Assert.Empty(Pending());
    }

    // A folder the Agent hasn't released yet doesn't hold the next lesson back.
    [Fact]
    public void Follow_FolderStillProtected_StillAdoptsTheNextLesson()
    {
        var folder = MakeLessonFolder();
        var display = new RecordingDisplay();
        var classFolder = NewClassFolder();
        classFolder.Follow(display);
        var first = LessonId;
        Write(folder, "a.png", "A");
        Protect(folder);
        _lesson = new ClassLesson(2, Guid.NewGuid(), "3º B", 1, false);
        var newFolder = MakeLessonFolder();
        Write(newFolder, "b.png");

        classFolder.Follow(display);

        // The notice and the new lesson at once; the old one is waiting, its files already aside.
        Assert.Equal(2, display.Shown.Count);
        Assert.StartsWith("Aula do 3º B" + Environment.NewLine, display.Shown[1]);
        Assert.Equal((LessonId, "3º B"), Remembered());
        Assert.Equal(new[] { (first, "3º A") }, Pending());
        Assert.True(Directory.Exists(folder));
        Assert.Equal("A", File.ReadAllText(Path.Combine(SetAsideFolder(first), "a.png")));
        Assert.True(File.Exists(Path.Combine(newFolder, "b.png")));

        // Still blocked, the Session started again: nothing is said twice, the old lesson is still waited for.
        var restarted = NewClassFolder();
        restarted.Follow(display);
        Assert.Equal(2, display.Shown.Count);
        Assert.Equal(new[] { (first, "3º A") }, Pending());

        Unprotect(folder);
        restarted.Follow(display);

        Assert.False(Directory.Exists(folder));
        Assert.Empty(Pending());
        Assert.Equal(2, display.Shown.Count);
        Assert.Equal((LessonId, "3º B"), Remembered());
        Assert.True(File.Exists(Path.Combine(newFolder, "b.png")));
    }

    [Fact]
    public void Follow_TwoChangesWhileTheFirstFolderIsBlocked_SetsBothAsideUnderTheirOwnLesson()
    {
        var display = new RecordingDisplay();
        var classFolder = NewClassFolder();
        var folderA = MakeLessonFolder();
        classFolder.Follow(display);
        var idA = LessonId;
        Write(folderA, "a.png", "A");
        Protect(folderA);

        _lesson = new ClassLesson(2, Guid.NewGuid(), "3º B", 1, false);
        var folderB = MakeLessonFolder();
        Write(folderB, "b.png", "B");
        classFolder.Follow(display);
        var idB = LessonId;
        Protect(folderB);

        _lesson = new ClassLesson(3, Guid.NewGuid(), "3º C", 1, false);
        var folderC = MakeLessonFolder();
        Write(folderC, "c.png", "C");
        Write(folderB, "b2.png", "B2"); // saved by a student before the Agent let go of it
        classFolder.Follow(display);
        var idC = LessonId;

        Assert.Equal(3, display.Shown.Count);
        Assert.Equal((idC, "3º C"), Remembered());
        Assert.Equal(new[] { (idA, "3º A"), (idB, "3º B") }, Pending());
        Assert.Equal("A", File.ReadAllText(Path.Combine(SetAsideFolder(idA), "a.png")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(SetAsideFolder(idB), "b.png")));
        Assert.Equal("B2", File.ReadAllText(Path.Combine(SetAsideFolder(idB), "b2.png")));

        // One is let go, the other still not: only that one is settled.
        Unprotect(folderB);
        classFolder.Follow(display);
        Assert.False(Directory.Exists(folderB));
        Assert.True(Directory.Exists(folderA));
        Assert.Equal(new[] { (idA, "3º A") }, Pending());

        Unprotect(folderA);
        classFolder.Follow(display);

        Assert.False(Directory.Exists(folderA));
        Assert.Empty(Pending());
        Assert.Equal(3, display.Shown.Count);
        Assert.Equal((idC, "3º C"), Remembered());
        Assert.Equal(new[] { "a.png" }, Directory.GetFiles(SetAsideFolder(idA)).Select(Path.GetFileName));
        Assert.Equal(new[] { "b.png", "b2.png" }, Directory.GetFiles(SetAsideFolder(idB)).Select(Path.GetFileName).OrderBy(name => name));
        Assert.False(Directory.Exists(SetAsideFolder(idC)));
        Assert.Equal("C", File.ReadAllText(Path.Combine(folderC, "c.png")));
    }
}
