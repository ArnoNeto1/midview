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
    public void List_ShowsFilesWithSizeAndTicks_AndSubfolders()
    {
        var folder = MakeLessonFolder();
        var when = new DateTime(2026, 10, 20, 8, 30, 15, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Write(folder, "A.txt", "abc"), when);
        Write(folder, "b.txt", "bb");
        Write(folder, "site/index.html");
        Write(folder, "site/imagens/logo.png");

        var (current, problem, entries) = NewClassFolder().List();

        Assert.Equal(LessonId, current);
        Assert.Null(problem);
        Assert.Equal(new[] { "A.txt", "b.txt", "site/index.html", "site/imagens/logo.png" }, entries.Select(e => e.Path));
        Assert.All(entries, entry => Assert.Equal(LessonId, entry.LessonId));
        Assert.Equal(3, entries[0].Size);
        Assert.Equal(when.Ticks, entries[0].LastWriteTicks);
        Assert.Equal(2, entries[1].Size);
        Assert.Equal(File.GetLastWriteTimeUtc(Path.Combine(folder, "b.txt")).Ticks, entries[1].LastWriteTicks);
    }

    [Fact]
    public void List_SkipsInProgressAndLockAndHiddenFiles()
    {
        var folder = MakeLessonFolder();
        Write(folder, "ok.txt");
        Write(folder, "a.crdownload");
        Write(folder, "D.CRDOWNLOAD");
        Write(folder, "b.tmp");
        Write(folder, "c.part");
        Write(folder, "y.parcial");
        Write(folder, "~$doc.docx");
        Write(folder, ".midview-x.parcial");
        Write(folder, ".midview-sem-sufixo");
        File.SetAttributes(Write(folder, "oculto.txt"), FileAttributes.Hidden);
        File.SetAttributes(Write(folder, "sistema.txt"), FileAttributes.System);
        Write(folder, "escondida/dentro.txt");
        File.SetAttributes(Path.Combine(folder, "escondida"), FileAttributes.Hidden);

        var (_, _, entries) = NewClassFolder().List();

        Assert.Equal(new[] { "ok.txt" }, entries.Select(e => e.Path));
    }

    [Fact]
    public void List_SkipsAJunctionInsideTheFolder()
    {
        var folder = MakeLessonFolder();
        Write(_outside, "segredo.txt");
        Write(folder, "meu.txt");
        MakeJunction(Path.Combine(folder, "atalho"), _outside);

        var (_, problem, entries) = NewClassFolder().List();

        Assert.Null(problem);
        Assert.Equal(new[] { "meu.txt" }, entries.Select(e => e.Path));
    }

    // A cloud files placeholder (OneDrive) is a reparse point with no link target: an ordinary file.
    [Fact]
    public void List_ListsAFileWhoseReparsePointIsNoLink()
    {
        var folder = MakeLessonFolder();
        Write(folder, "meu.txt");
        NonLinkReparsePoint.Make(Write(folder, "nuvem.txt", "conteudo"));

        var (_, problem, entries) = NewClassFolder().List();

        Assert.Null(problem);
        Assert.Equal(new[] { "meu.txt", "nuvem.txt" }, entries.Select(e => e.Path));
        Assert.Equal(8, entries.Single(e => e.Path == "nuvem.txt").Size);
    }

    [Fact]
    public void Follow_LessonEnded_AReparsePointThatIsNoLink_IsSetAsideLikeAnyFile_AndDoesNotKeepTheFolder()
    {
        var folder = MakeLessonFolder();
        var classFolder = NewClassFolder();
        classFolder.Follow(null);
        var id = LessonId;
        NonLinkReparsePoint.Make(Write(folder, "nuvem.txt", "conteudo"));
        // One that is not listed, so that it is still in the folder when the search for links looks through it.
        NonLinkReparsePoint.Make(Write(folder, "rascunho.tmp", "conteudo"));
        _lesson = _lesson! with { Ended = true };

        classFolder.Follow(null);

        Assert.True(File.Exists(Path.Combine(SetAsideFolder(id), "nuvem.txt")));
        Assert.Equal(new[] { folder }, _bin.Recycled);
        Assert.Empty(Pending());
    }

    [Fact]
    public void List_WhenTheFolderItselfIsAJunction_ReportsItAndListsNothing()
    {
        Write(_outside, "segredo.txt");
        MakeJunction(LessonFolder, _outside);

        var (current, problem, entries) = NewClassFolder().List();

        Assert.Equal(LessonId, current);
        Assert.Equal("pasta vinculada", problem);
        Assert.Empty(entries);
    }

    [Fact]
    public void List_WithLessonOnButNoFolder_ReportsSemPasta()
    {
        // Set-aside files don't go in a listing that has a problem: the wire format has no room for both.
        Write(SetAsideFolder(Guid.NewGuid()), "guardado.txt");

        var (current, problem, entries) = NewClassFolder().List();

        Assert.Equal(LessonId, current);
        Assert.Equal("sem pasta", problem);
        Assert.Empty(entries);
    }

    [Fact]
    public void List_WithAnUnknownDesktop_ReportsSemPasta()
    {
        var (current, problem, entries) = NewClassFolder(desktop: "").List();

        Assert.Equal(LessonId, current);
        Assert.Equal("sem pasta", problem);
        Assert.Empty(entries);
    }

    // The turma comes from a file; one that isn't a single folder name must not reach out of the Desktop.
    [Fact]
    public void ATurmaThatIsNotAFolderName_HasNoFolder()
    {
        _lesson = new ClassLesson(1, Guid.NewGuid(), @"..\..\Documentos", 1, false);
        Directory.CreateDirectory(Path.Combine(_root, "Documentos"));
        var classFolder = NewClassFolder();

        var (_, problem, entries) = classFolder.List();

        Assert.Equal("sem pasta", problem);
        Assert.Empty(entries);
        Assert.Equal("sem pasta", Assert.Throws<InvalidOperationException>(() => classFolder.BeginPush(LessonId, "x.txt")).Message);
    }

    [Fact]
    public void List_IncludesSetAsideLessons_UnderTheirLessonId()
    {
        var folder = MakeLessonFolder();
        Write(folder, "agora.txt");
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        Write(SetAsideFolder(first), "x.txt");
        Write(SetAsideFolder(first), "sub/y.txt");
        Write(SetAsideFolder(second), "z.txt");
        Write(_setAside, "lixo/nao-e-aula.txt"); // not a lesson id
        Write(SetAsideFolder(Guid.Empty), "zero.txt"); // not a lesson

        var (current, problem, entries) = NewClassFolder().List();

        Assert.Equal(LessonId, current);
        Assert.Null(problem);
        var ordered = new[] { first, second }.OrderBy(id => id.ToString("N"), StringComparer.OrdinalIgnoreCase).ToArray();
        var expected = new List<(Guid, string)> { (LessonId, "agora.txt") };
        foreach (var id in ordered)
        {
            expected.AddRange(id == first
                ? new[] { (id, "x.txt"), (id, "sub/y.txt") }
                : new[] { (id, "z.txt") });
        }
        Assert.Equal(expected, entries.Select(e => (e.LessonId, e.Path)));
    }

    // A junction in the set-aside folder, named like a lesson, would list what is outside it, files nobody can fetch or clear.
    [Fact]
    public void List_SkipsASetAsideFolderThatIsAJunction()
    {
        var folder = MakeLessonFolder();
        Write(folder, "agora.txt");
        var real = Guid.NewGuid();
        Write(SetAsideFolder(real), "x.txt");
        var linked = Guid.NewGuid();
        Write(_outside, "segredo.txt");
        Write(_outside, "sub/mais.txt");
        MakeJunction(SetAsideFolder(linked), _outside);

        var (_, problem, entries) = NewClassFolder().List();

        Assert.Null(problem);
        Assert.Equal(new[] { (LessonId, "agora.txt"), (real, "x.txt") }, entries.Select(e => (e.LessonId, e.Path)));
        Assert.Null(NewClassFolder().ResolveExisting(linked, "segredo.txt"));
    }

    [Fact]
    public void List_WithNoLessonOn_ListsOnlyWhatWasSetAside()
    {
        // Desktop folders left over from a lesson that ended aren't this class's business.
        Write(LessonFolder, "ficou.txt");
        _lesson = _lesson! with { Ended = true };
        var aside = Guid.NewGuid();
        Write(SetAsideFolder(aside), "guardado.txt");

        var (current, problem, entries) = NewClassFolder().List();

        Assert.Equal(Guid.Empty, current);
        Assert.Null(problem);
        Assert.Equal(new[] { (aside, "guardado.txt") }, entries.Select(e => (e.LessonId, e.Path)));
    }

    [Fact]
    public void List_WithNoLessonAtAll_ListsOnlyWhatWasSetAside()
    {
        _lesson = null;
        var aside = Guid.NewGuid();
        Write(SetAsideFolder(aside), "guardado.txt");

        var (current, problem, entries) = NewClassFolder().List();

        Assert.Equal(Guid.Empty, current);
        Assert.Null(problem);
        Assert.Equal(new[] { (aside, "guardado.txt") }, entries.Select(e => (e.LessonId, e.Path)));
    }

    [Fact]
    public void List_SkipsASetAsideFolderOfTheCurrentLesson()
    {
        // LessonId equal to the current lesson means "in the Desktop folder", so a set-aside copy would be mistaken for it.
        var folder = MakeLessonFolder();
        Write(folder, "agora.txt");
        Write(SetAsideFolder(LessonId), "guardado.txt");

        var (_, _, entries) = NewClassFolder().List();

        Assert.Equal(new[] { "agora.txt" }, entries.Select(e => e.Path));
    }

    [Fact]
    public void List_StopsAtTheMaximumDepth()
    {
        var folder = MakeLessonFolder();
        var deep = string.Join("/", Enumerable.Range(1, FileCollection.MaxDepth + 2).Select(level => $"n{level}"));
        Write(folder, $"{deep}/fundo.txt");
        // One segment too many: the file name counts, so MaxDepth folders and a file is MaxDepth + 1.
        var justTooDeep = string.Join("/", Enumerable.Range(1, FileCollection.MaxDepth).Select(level => $"m{level}")) + "/fundo.txt";
        Write(folder, justTooDeep);
        var deepest = string.Join("/", Enumerable.Range(1, FileCollection.MaxDepth - 1).Select(level => $"n{level}")) + "/ok.txt";
        Write(folder, deepest);

        var (_, _, entries) = NewClassFolder().List();

        Assert.Equal(new[] { deepest }, entries.Select(e => e.Path));
    }

    [Fact]
    public void List_StopsAtTheEntryLimit()
    {
        var folder = MakeLessonFolder();
        for (var i = 0; i < 10; i++)
        {
            Write(folder, $"f{i}.txt");
        }
        Write(SetAsideFolder(Guid.NewGuid()), "guardado.txt");

        var (_, _, entries) = NewClassFolder(maxEntries: 3).List();

        Assert.Equal(new[] { "f0.txt", "f1.txt", "f2.txt" }, entries.Select(e => e.Path));
    }

    // The listing's wire format refuses a file over FileTransfer.MaxFileSize; one big video must not take the rest down.
    [Fact]
    public void List_LeavesOutAFileOverTheSizeLimit()
    {
        var folder = MakeLessonFolder();
        Write(folder, "pequeno.txt");
        using (var stream = File.Create(Path.Combine(folder, "enorme.mp4")))
        {
            stream.SetLength(FileTransfer.MaxFileSize + 1);
        }
        using (var stream = File.Create(Path.Combine(folder, "no-limite.mp4")))
        {
            stream.SetLength(FileTransfer.MaxFileSize);
        }

        var (current, problem, entries) = NewClassFolder().List();

        Assert.Equal(new[] { "no-limite.mp4", "pequeno.txt" }, entries.Select(e => e.Path));
        Assert.NotEmpty(ClassFolderWire.EncodeListing(current, problem, entries));
    }

    // ... and a path over FileTransfer.MaxNameBytes of UTF-8 (a CJK character is three of them).
    [Fact]
    public void List_LeavesOutAPathOverTheNameLimit()
    {
        var folder = MakeLessonFolder();
        var level = new string('あ', 100);
        var tooLong = string.Join("/", Enumerable.Repeat(level, 4)) + "/fundo.txt";
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(tooLong) > FileTransfer.MaxNameBytes);
        Write(folder, tooLong);
        Write(folder, "curto.txt");

        var (current, problem, entries) = NewClassFolder().List();

        Assert.Equal(new[] { "curto.txt" }, entries.Select(e => e.Path));
        Assert.NotEmpty(ClassFolderWire.EncodeListing(current, problem, entries));
    }

    // Every message names a file by a path that goes through FileCollection.SafeRelativePath, which
    // shortens a name over FileNames.MaxNameLength; a file it would rename can't be asked for, so it isn't listed.
    [Fact]
    public void List_LeavesOutNamesThatNoMessageCouldAsk()
    {
        var folder = MakeLessonFolder();
        Write(folder, "curto.txt");
        Write(folder, new string('a', FileNames.MaxNameLength) + ".txt");
        Write(folder, "longa/" + new string('b', FileNames.MaxNameLength + 1) + ".txt");
        Write(folder, new string('c', FileNames.MaxNameLength + 1) + "/dentro.txt");

        var (_, _, entries) = NewClassFolder().List();

        Assert.Equal(new[] { "curto.txt" }, entries.Select(e => e.Path));
    }

    [Fact]
    public void ResolveExisting_FindsAFileInTheCurrentAndInASetAsideLesson()
    {
        var folder = MakeLessonFolder();
        var inCurrent = Write(folder, "sub/atual.txt");
        var aside = Guid.NewGuid();
        var inAside = Write(SetAsideFolder(aside), "guardado.txt");
        var classFolder = NewClassFolder();

        Assert.Equal(inCurrent, classFolder.ResolveExisting(LessonId, "sub/atual.txt"));
        Assert.Equal(inCurrent, classFolder.ResolveExisting(LessonId, @"sub\atual.txt"));
        Assert.Equal(inAside, classFolder.ResolveExisting(aside, "guardado.txt"));
    }

    [Fact]
    public void ResolveExisting_RejectsEscapes()
    {
        var folder = MakeLessonFolder();
        var inside = Write(folder, "dentro.txt");
        var beside = Write(_desktop, "ao-lado.txt");
        var aside = Guid.NewGuid();
        Write(SetAsideFolder(aside), "guardado.txt");
        var classFolder = NewClassFolder();

        // What climbs out stays in: ".." is dropped, so the file asked for is the one inside the folder.
        Assert.Equal(inside, classFolder.ResolveExisting(LessonId, "../../dentro.txt"));
        Assert.Null(classFolder.ResolveExisting(LessonId, "../ao-lado.txt"));
        Assert.Null(classFolder.ResolveExisting(LessonId, @"..\..\ao-lado.txt"));
        Assert.Null(classFolder.ResolveExisting(LessonId, beside));
        Assert.Null(classFolder.ResolveExisting(LessonId, @"C:\Windows\win.ini"));
        // A set-aside lesson's file isn't in the current lesson's folder, nor the other way round.
        Assert.Null(classFolder.ResolveExisting(LessonId, "guardado.txt"));
        Assert.Null(classFolder.ResolveExisting(aside, "dentro.txt"));
        // Not a lesson at all.
        Assert.Null(classFolder.ResolveExisting(Guid.NewGuid(), "dentro.txt"));
        Assert.Null(classFolder.ResolveExisting(Guid.Empty, "dentro.txt"));
        // Folders aren't files.
        Directory.CreateDirectory(Path.Combine(folder, "pasta"));
        Assert.Null(classFolder.ResolveExisting(LessonId, "pasta"));
    }

    [Fact]
    public void ResolveExisting_RefusesAPathThroughAJunction()
    {
        var folder = MakeLessonFolder();
        Write(_outside, "segredo.txt");
        MakeJunction(Path.Combine(folder, "atalho"), _outside);

        Assert.Null(NewClassFolder().ResolveExisting(LessonId, "atalho/segredo.txt"));
    }
}
