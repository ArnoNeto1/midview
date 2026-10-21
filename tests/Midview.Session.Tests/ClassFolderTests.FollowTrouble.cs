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
    public void Follow_WhenAFolderCantBeRead_SetsAsideWhatItCanAndTriesAgain()
    {
        var folder = MakeLessonFolder();
        var classFolder = NewClassFolder();
        classFolder.Follow(null);
        var id = LessonId;
        Write(folder, "a.png", "A");
        var closed = Path.Combine(folder, "sub");
        Write(closed, "b.txt", "B");
        CloseToTheStudent(closed);
        _lesson = _lesson! with { Ended = true };

        classFolder.Follow(null);

        // A folder that couldn't be read isn't an empty one: what is in it must not go to the Recycle Bin unseen.
        Assert.Empty(_bin.Recycled);
        Assert.True(Directory.Exists(folder));
        Assert.Equal("A", File.ReadAllText(Path.Combine(SetAsideFolder(id), "a.png")));
        Assert.Equal(new[] { (id, "3º A") }, Pending());

        OpenToTheStudent(closed);
        classFolder.Follow(null);

        Assert.Equal("B", File.ReadAllText(Path.Combine(SetAsideFolder(id), "sub", "b.txt")));
        Assert.Equal(new[] { folder }, _bin.Recycled);
        Assert.Empty(Pending());
    }

    // The search for links stops at the first link, so it would settle the folder before it ever tried the closed one.
    [Fact]
    public void Follow_WithALinkAndAFolderThatCantBeRead_DoesNotSettleUntilItIsRead()
    {
        var folder = MakeLessonFolder();
        var classFolder = NewClassFolder();
        classFolder.Follow(null);
        var id = LessonId;
        Write(_outside, "segredo.txt");
        MakeJunction(Path.Combine(folder, "atalho"), _outside);
        var closed = Path.Combine(folder, "sub");
        Write(closed, "b.txt", "B");
        CloseToTheStudent(closed);
        _lesson = _lesson! with { Ended = true };

        classFolder.Follow(null);

        Assert.Equal(new[] { (id, "3º A") }, Pending());
        Assert.False(File.Exists(Path.Combine(SetAsideFolder(id), "sub", "b.txt")));

        OpenToTheStudent(closed);
        classFolder.Follow(null);

        Assert.Equal("B", File.ReadAllText(Path.Combine(SetAsideFolder(id), "sub", "b.txt")));
        Assert.Empty(Pending());
        Assert.Empty(_bin.Recycled);
        Assert.True(File.Exists(Path.Combine(_outside, "segredo.txt")));
    }

    // The search for links can't read it either: that is not "a link", and not "none": it is asked again.
    [Fact]
    public void Follow_WhenAHiddenFolderCantBeRead_NeitherRecyclesNorSettles()
    {
        var folder = MakeLessonFolder();
        var classFolder = NewClassFolder();
        classFolder.Follow(null);
        var id = LessonId;
        Write(folder, "a.png", "A");
        var closed = Path.Combine(folder, "escondida");
        Write(closed, "dentro.txt");
        File.SetAttributes(closed, FileAttributes.Hidden);
        CloseToTheStudent(closed);
        _lesson = _lesson! with { Ended = true };

        classFolder.Follow(null);

        Assert.Empty(_bin.Recycled);
        Assert.True(Directory.Exists(folder));
        Assert.Equal("A", File.ReadAllText(Path.Combine(SetAsideFolder(id), "a.png")));
        Assert.Equal(new[] { (id, "3º A") }, Pending());

        OpenToTheStudent(closed);
        classFolder.Follow(null);

        Assert.Equal(new[] { folder }, _bin.Recycled);
        Assert.Equal(new[] { "escondida/dentro.txt" }, _bin.Contents[folder].Where(entry => entry.EndsWith(".txt")));
        Assert.Empty(Pending());
    }
    [Fact]
    public void Follow_ChangeOfClass_SetsTheOldFolderAside_AndLeavesTheNewOne()
    {
        var oldFolder = MakeLessonFolder();
        var display = new RecordingNotice();
        var classFolder = NewClassFolder();
        classFolder.Follow(display);
        var oldId = LessonId;
        Write(oldFolder, "a.png");
        _lesson = new ClassLesson(2, Guid.NewGuid(), "3º B", 1, false);
        var newFolder = MakeLessonFolder();
        Write(newFolder, "b.png");

        classFolder.Follow(display);

        Assert.False(Directory.Exists(oldFolder));
        Assert.True(File.Exists(Path.Combine(SetAsideFolder(oldId), "a.png")));
        Assert.True(File.Exists(Path.Combine(newFolder, "b.png")));
        Assert.False(Directory.Exists(SetAsideFolder(LessonId)));
        Assert.Equal(2, display.Shown.Count);
        Assert.Contains("Aula do 3º B", display.Shown[1]);
        Assert.Equal((LessonId, "3º B"), Remembered());
    }

    // A new lesson of the same turma uses the very folder the last one had, which stays: it is the new
    // lesson's now. What the last lesson left in it is set aside under that lesson all the same, so the
    // teacher's Master decides what of it the class gets (nothing, if that lesson was collected).
    [Fact]
    public void Follow_NewLessonOfTheSameTurma_SetsTheOldFilesAside_AndKeepsTheFolder()
    {
        var folder = MakeLessonFolder();
        var display = new RecordingNotice();
        var classFolder = NewClassFolder();
        classFolder.Follow(display);
        var oldId = LessonId;
        Write(folder, "a.png", "A");
        Write(folder, "fotos/b.png", "B");
        Write(folder, "baixando.crdownload");
        _lesson = new ClassLesson(2, Guid.NewGuid(), "3º A", 1, false);

        classFolder.Follow(display);

        Assert.True(Directory.Exists(folder));
        Assert.Equal("A", File.ReadAllText(Path.Combine(SetAsideFolder(oldId), "a.png")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(SetAsideFolder(oldId), "fotos", "b.png")));
        Assert.Equal(new[] { "baixando.crdownload" }, Directory.GetFileSystemEntries(folder).Select(Path.GetFileName));
        Assert.Empty(_bin.Recycled);
        Assert.Empty(Pending());
        Assert.Equal(2, display.Shown.Count);
        Assert.Equal((LessonId, "3º A"), Remembered());
        var (current, _, entries) = classFolder.List();
        Assert.Equal(LessonId, current);
        Assert.Equal(new[] { (oldId, "a.png"), (oldId, "fotos/b.png") }, entries.Select(entry => (entry.LessonId, entry.Path)));
    }

    // Set aside once, when the change is seen: a file held open stays, as the new lesson's, rather than
    // a later try sweeping up what the new lesson has put in the folder meanwhile.
    [Fact]
    public void Follow_NewLessonOfTheSameTurma_WithAFileHeldOpen_SetsAsideTheRestOnce()
    {
        var folder = MakeLessonFolder();
        var classFolder = NewClassFolder();
        classFolder.Follow(null);
        var oldId = LessonId;
        Write(folder, "a.png", "A");
        var held = Write(folder, "aberto.docx", "D");
        _lesson = new ClassLesson(2, Guid.NewGuid(), "3º A", 1, false);

        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            classFolder.Follow(null);
        }
        Write(folder, "novo.png", "N"); // the new lesson's
        classFolder.Follow(null);

        Assert.Empty(Pending());
        Assert.True(File.Exists(Path.Combine(SetAsideFolder(oldId), "a.png")));
        Assert.Equal(new[] { "aberto.docx", "novo.png" }, Directory.GetFiles(folder).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Follow_WhenAFileIsHeldOpen_KeepsItAndRetries()
    {
        var folder = MakeLessonFolder();
        var classFolder = NewClassFolder();
        classFolder.Follow(null);
        var id = LessonId;
        Write(folder, "livre.txt");
        var held = Write(folder, "sub/aberto.docx", "meu");
        _lesson = _lesson! with { Ended = true };

        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            classFolder.Follow(null);

            Assert.True(File.Exists(held));
            Assert.True(File.Exists(Path.Combine(SetAsideFolder(id), "livre.txt")));
            Assert.Equal(new[] { (id, "3º A") }, Pending());
        }

        classFolder.Follow(null);

        Assert.False(Directory.Exists(folder));
        Assert.Equal("meu", File.ReadAllText(Path.Combine(SetAsideFolder(id), "sub", "aberto.docx")));
        Assert.Equal((Guid.Empty, ""), Remembered());
        Assert.Empty(Pending());
    }

    [Fact]
    public void Follow_WhenAFileIsHeldOpen_LeavesNoEmptyFoldersAside()
    {
        var folder = MakeLessonFolder();
        var classFolder = NewClassFolder();
        classFolder.Follow(null);
        var id = LessonId;
        var held = Write(folder, "sub/aberto.docx");
        _lesson = _lesson! with { Ended = true };

        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            classFolder.Follow(null);
        }

        Assert.False(Directory.Exists(SetAsideFolder(id)));
    }

    [Fact]
    public void Follow_WhenTheNameIsTakenAside_KeepsBothFiles()
    {
        var folder = MakeLessonFolder();
        var classFolder = NewClassFolder();
        classFolder.Follow(null);
        var id = LessonId;
        Write(SetAsideFolder(id), "a.png", "primeiro");
        Write(folder, "a.png", "segundo");
        _lesson = _lesson! with { Ended = true };

        classFolder.Follow(null);

        Assert.False(Directory.Exists(folder));
        Assert.Equal("primeiro", File.ReadAllText(Path.Combine(SetAsideFolder(id), "a.png")));
        Assert.Equal("segundo", File.ReadAllText(Path.Combine(SetAsideFolder(id), "a (2).png")));
    }

    [Fact]
    public void Follow_LeavesALinkInsideTheFolderAlone_AndMovesOn()
    {
        var folder = MakeLessonFolder();
        var classFolder = NewClassFolder();
        classFolder.Follow(null);
        var id = LessonId;
        Write(_outside, "segredo.txt");
        Write(folder, "meu.txt");
        MakeJunction(Path.Combine(folder, "atalho"), _outside);
        _lesson = _lesson! with { Ended = true };

        classFolder.Follow(null);

        Assert.True(File.Exists(Path.Combine(_outside, "segredo.txt")));
        Assert.True(Directory.Exists(Path.Combine(folder, "atalho")));
        Assert.True(File.Exists(Path.Combine(SetAsideFolder(id), "meu.txt")));
        Assert.Equal(new[] { "meu.txt" }, Directory.GetFileSystemEntries(SetAsideFolder(id)).Select(Path.GetFileName));
        Assert.Empty(_bin.Recycled); // a folder with a link in it isn't handed to the Recycle Bin
        Assert.Equal((Guid.Empty, ""), Remembered());
        Assert.Empty(Pending()); // the folder stays: not worth trying again
    }

    [Fact]
    public void Follow_LeavesALinkDeepInsideTheFolderAlone()
    {
        var folder = MakeLessonFolder();
        var classFolder = NewClassFolder();
        classFolder.Follow(null);
        Write(_outside, "segredo.txt");
        Write(folder, "x.crdownload");
        Write(folder, "a/b/c/d/e/f/g/h/i/j/k/fundo.txt"); // deeper than a listing goes
        Directory.CreateDirectory(Path.Combine(folder, "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l"));
        MakeJunction(Path.Combine(folder, "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "atalho"), _outside);
        _lesson = _lesson! with { Ended = true };

        classFolder.Follow(null);

        Assert.True(File.Exists(Path.Combine(_outside, "segredo.txt")));
        Assert.True(File.Exists(Path.Combine(folder, "x.crdownload")));
        Assert.Empty(_bin.Recycled);
        Assert.Equal((Guid.Empty, ""), Remembered());
    }

    [Fact]
    public void Follow_WhenTheOldFolderIsALink_DoesNotFollowIt()
    {
        var classFolder = NewClassFolder();
        classFolder.Follow(null);
        Write(_outside, "segredo.txt");
        MakeJunction(LessonFolder, _outside);
        _lesson = _lesson! with { Ended = true };

        classFolder.Follow(null);

        Assert.True(File.Exists(Path.Combine(_outside, "segredo.txt")));
        Assert.False(Directory.Exists(_setAside));
        Assert.Empty(_bin.Recycled);
        Assert.Equal((Guid.Empty, ""), Remembered());
    }

    [Fact]
    public void Follow_WithTheSetAsideFolderALink_DoesNotFollowIt()
    {
        var folder = MakeLessonFolder();
        var classFolder = NewClassFolder();
        classFolder.Follow(null);
        Write(folder, "a.png");
        Directory.CreateDirectory(_setAside);
        MakeJunction(SetAsideFolder(LessonId), _outside);
        _lesson = _lesson! with { Ended = true };

        classFolder.Follow(null);

        Assert.Empty(Directory.GetFileSystemEntries(_outside));
        Assert.True(File.Exists(Path.Combine(folder, "a.png")));
    }

    // The state file comes from the student's own profile: it can't point the Session outside the Desktop.
    [Fact]
    public void Follow_WithATurmaThatReachesOutOfTheDesktop_MovesNothing()
    {
        // "Pasta da turma – x\..\..\Documentos" is, written out as a path, the Documentos folder next to the Desktop.
        Write(_root, "Documentos/meu.txt");
        _lesson = ClassLesson.None(1);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(new { LessonId = Guid.NewGuid(), Turma = @"x\..\..\Documentos" }));

        NewClassFolder().Follow(null);

        Assert.True(File.Exists(Path.Combine(_root, "Documentos", "meu.txt")));
        Assert.False(Directory.Exists(_setAside));
        Assert.Equal((Guid.Empty, ""), Remembered());
    }

    [Fact]
    public void Follow_NoDesktop_DoesNotThrow()
    {
        var display = new RecordingNotice();
        var missing = Path.Combine(_root, "nao-existe");
        var classFolder = NewClassFolder(missing);

        classFolder.Follow(display);

        Assert.Single(display.Shown);
        var (current, problem, entries) = classFolder.List();
        Assert.Equal(LessonId, current);
        Assert.Equal("sem pasta", problem);
        Assert.Empty(entries);

        _lesson = _lesson! with { Ended = true };
        classFolder.Follow(display);
        NewClassFolder(desktop: "").Follow(display);
        Assert.Equal((Guid.Empty, ""), Remembered());
    }

    [Fact]
    public void Follow_NeverThrows()
    {
        MakeLessonFolder();
        // A notice that can't be shown, and a state that can't be written (a folder where the file goes).
        Directory.CreateDirectory(StatePath);
        var display = new RecordingNotice { Throw = new InvalidOperationException("sem tela") };
        var classFolder = NewClassFolder();

        classFolder.Follow(display);
        classFolder.Follow(display);

        // It isn't shown again every few seconds for want of a place to remember it.
        Assert.Single(display.Shown);
    }

    [Fact]
    public void Follow_WithACorruptState_TreatsItAsNothingBefore()
    {
        File.WriteAllText(StatePath, "{ not json");
        var display = new RecordingNotice();

        NewClassFolder().Follow(display);

        Assert.Single(display.Shown);
        Assert.Equal((LessonId, "3º A"), Remembered());
    }
}
