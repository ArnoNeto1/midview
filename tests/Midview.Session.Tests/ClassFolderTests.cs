using System.Diagnostics;
using Midview.Protocol;
using Midview.Session;
using Xunit;

public class ClassFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"midview-classfolder-{Guid.NewGuid():N}");
    private readonly string _desktop;
    private readonly string _setAside;
    private readonly string _outside;
    private readonly List<string> _links = new();
    private readonly RecordingRecycleBin _bin = new();
    private ClassLesson? _lesson = new(1, Guid.NewGuid(), "3º A", 1, false);

    public ClassFolderTests()
    {
        _desktop = Path.Combine(_root, "Desktop");
        _setAside = Path.Combine(_root, "guardada");
        _outside = Path.Combine(_root, "fora");
        Directory.CreateDirectory(_desktop);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        // Links first: deleting one removes the link, never what it points at.
        foreach (var link in _links.Where(Directory.Exists))
        {
            Directory.Delete(link);
        }
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private Guid LessonId => _lesson!.LessonId;

    // The folder the Agent keeps on the Desktop for the current lesson.
    private string LessonFolder => Path.Combine(_desktop, _lesson!.FolderName);

    private string MakeLessonFolder()
    {
        Directory.CreateDirectory(LessonFolder);
        return LessonFolder;
    }

    private ClassFolder NewClassFolder(string? desktop = null, int maxEntries = ClassFolderWire.MaxEntries) =>
        new(desktop ?? _desktop, _setAside, () => _lesson, _bin, Path.Combine(_root, "session.json"), maxEntries);

    private string SetAsideFolder(Guid lessonId) => Path.Combine(_setAside, lessonId.ToString("N"));

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

    [Fact]
    public void BeginPush_WritesThroughATempFile_AndKeepsTheTime()
    {
        var folder = MakeLessonFolder();
        var target = Path.Combine(folder, "notas.txt");
        var ticks = new DateTime(2025, 3, 14, 9, 26, 53, DateTimeKind.Utc).AddTicks(1234567).Ticks;

        using (var incoming = NewClassFolder().BeginPush(LessonId, "notas.txt"))
        {
            incoming.Write("olá "u8);
            incoming.Write("mundo"u8);

            // Half-received: only the temp file exists, never the real name.
            Assert.False(File.Exists(target));
            var temp = Assert.Single(Directory.GetFiles(folder));
            Assert.StartsWith(".midview-", Path.GetFileName(temp));
            Assert.EndsWith(".parcial", temp);

            incoming.Complete(ticks);
        }

        Assert.Equal(new[] { target }, Directory.GetFiles(folder));
        Assert.Equal("olá mundo", File.ReadAllText(target));
        Assert.Equal(ticks, File.GetLastWriteTimeUtc(target).Ticks);
    }

    [Fact]
    public void BeginPush_CreatesMissingSubfolders()
    {
        var folder = MakeLessonFolder();

        using (var incoming = NewClassFolder().BeginPush(LessonId, "site/imagens/logo.png"))
        {
            incoming.Write(new byte[] { 1, 2, 3 });
            incoming.Complete(DateTime.UtcNow.Ticks);
        }

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(folder, "site", "imagens", "logo.png")));
    }

    [Fact]
    public void BeginPush_ForAnotherLesson_Throws()
    {
        var folder = MakeLessonFolder();
        var aside = Guid.NewGuid();
        Directory.CreateDirectory(SetAsideFolder(aside));
        var classFolder = NewClassFolder();

        foreach (var other in new[] { Guid.NewGuid(), aside, Guid.Empty })
        {
            var refusal = Assert.Throws<InvalidOperationException>(() => classFolder.BeginPush(other, "x.txt"));
            Assert.Equal("aula diferente", refusal.Message);
        }
        Assert.Empty(Directory.GetFileSystemEntries(folder));
        Assert.Empty(Directory.GetFileSystemEntries(SetAsideFolder(aside)));
    }

    [Fact]
    public void BeginPush_WithNoLessonOn_Throws()
    {
        MakeLessonFolder();
        var id = LessonId;
        _lesson = _lesson! with { Ended = true };

        Assert.Equal("aula diferente", Assert.Throws<InvalidOperationException>(() => NewClassFolder().BeginPush(id, "x.txt")).Message);
    }

    // The Agent makes and protects the folder; the Session doesn't make one the student owns.
    [Fact]
    public void BeginPush_WithoutTheLessonFolder_Throws()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => NewClassFolder().BeginPush(LessonId, "x.txt"));

        Assert.Equal("sem pasta", refusal.Message);
        Assert.False(Directory.Exists(LessonFolder));
    }

    [Fact]
    public void BeginPush_WithDotDotPath_StaysInsideTheFolder()
    {
        var folder = MakeLessonFolder();

        using (var incoming = NewClassFolder().BeginPush(LessonId, "../../x.txt"))
        {
            incoming.Write(new byte[] { 7 });
            incoming.Complete(DateTime.UtcNow.Ticks);
        }

        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(Path.Combine(folder, "x.txt")));
        Assert.False(File.Exists(Path.Combine(_desktop, "x.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "x.txt")));
    }

    [Fact]
    public void BeginPush_ThroughAJunction_Throws()
    {
        var folder = MakeLessonFolder();
        MakeJunction(Path.Combine(folder, "atalho"), _outside);

        Assert.Throws<InvalidOperationException>(() => NewClassFolder().BeginPush(LessonId, "atalho/novo.txt"));

        Assert.Empty(Directory.GetFileSystemEntries(_outside));
    }

    [Fact]
    public void BeginPush_OntoAFolder_Throws()
    {
        var folder = MakeLessonFolder();
        Directory.CreateDirectory(Path.Combine(folder, "trabalhos"));

        Assert.Throws<InvalidOperationException>(() => NewClassFolder().BeginPush(LessonId, "trabalhos"));

        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(folder, "trabalhos")));
    }

    [Fact]
    public void Complete_ReplacesAFileThatIsThere()
    {
        var folder = MakeLessonFolder();
        var target = Write(folder, "trabalho.docx", "antigo");
        var ticks = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc).Ticks;

        using (var incoming = NewClassFolder().BeginPush(LessonId, "trabalho.docx"))
        {
            incoming.Write("novo"u8);
            incoming.Complete(ticks);
        }

        Assert.Equal("novo", File.ReadAllText(target));
        Assert.Equal(ticks, File.GetLastWriteTimeUtc(target).Ticks);
        Assert.Equal(new[] { target }, Directory.GetFiles(folder));
    }

    [Fact]
    public void Complete_OverAFileHeldOpen_Throws_AndLeavesNoTempFile()
    {
        var folder = MakeLessonFolder();
        var target = Write(folder, "aberto.docx", "antigo");
        var incoming = NewClassFolder().BeginPush(LessonId, "aberto.docx");
        incoming.Write("novo"u8);

        using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var refusal = Assert.Throws<IOException>(() => incoming.Complete(DateTime.UtcNow.Ticks));
            Assert.Equal("o arquivo está em uso por outro programa", ErrorText.Describe(refusal));
            incoming.Dispose();
        }

        Assert.Equal(new[] { target }, Directory.GetFiles(folder));
        Assert.Equal("antigo", File.ReadAllText(target));
    }

    [Fact]
    public void NotCompleted_LeavesNothingBehind()
    {
        var folder = MakeLessonFolder();

        using (var incoming = NewClassFolder().BeginPush(LessonId, "pela-metade.zip"))
        {
            incoming.Write(new byte[] { 1, 2, 3 });
        }

        Assert.Empty(Directory.GetFileSystemEntries(folder));
    }

    [Fact]
    public void Delete_SendsToTheRecycleBin()
    {
        var folder = MakeLessonFolder();
        var file = Write(folder, "sub/velho.txt");
        var keep = Write(folder, "fica.txt");

        var (ok, detail) = NewClassFolder().Delete(LessonId, "sub/velho.txt");

        Assert.True(ok, detail);
        Assert.Equal(new[] { file }, _bin.Recycled);
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(keep));
    }

    [Fact]
    public void Delete_OfAFileThatIsNotThereOrAnotherLesson_Fails()
    {
        MakeLessonFolder();
        var classFolder = NewClassFolder();

        Assert.Equal((false, "arquivo não encontrado"), classFolder.Delete(LessonId, "nada.txt"));
        Assert.Equal((false, "aula diferente"), classFolder.Delete(Guid.NewGuid(), "nada.txt"));
        Assert.Empty(_bin.Recycled);
    }

    [Fact]
    public void Delete_WhenTheBinRefuses_ReportsWhy()
    {
        var folder = MakeLessonFolder();
        var file = Write(folder, "aberto.txt");
        _bin.Refuse = new IOException("in use", unchecked((int)0x80070020));

        var (ok, detail) = NewClassFolder().Delete(LessonId, "aberto.txt");

        Assert.False(ok);
        Assert.Equal("o arquivo está em uso por outro programa", detail);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Rename_MovesTheFile()
    {
        var folder = MakeLessonFolder();
        Write(folder, "sub/images.jpg", "conteudo");

        var (ok, detail) = NewClassFolder().Rename(LessonId, "sub/images.jpg", "sub/images (2).jpg");

        Assert.True(ok, detail);
        Assert.False(File.Exists(Path.Combine(folder, "sub", "images.jpg")));
        Assert.Equal("conteudo", File.ReadAllText(Path.Combine(folder, "sub", "images (2).jpg")));
    }

    [Fact]
    public void Rename_RefusesAnExistingName()
    {
        var folder = MakeLessonFolder();
        Write(folder, "a.txt", "a");
        Write(folder, "b.txt", "b");
        Directory.CreateDirectory(Path.Combine(folder, "pasta"));
        var classFolder = NewClassFolder();

        Assert.False(classFolder.Rename(LessonId, "a.txt", "b.txt").Ok);
        Assert.False(classFolder.Rename(LessonId, "a.txt", "pasta").Ok);

        Assert.Equal("a", File.ReadAllText(Path.Combine(folder, "a.txt")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(folder, "b.txt")));
    }

    [Fact]
    public void Rename_WithDotDotNewName_StaysInsideTheFolder()
    {
        var folder = MakeLessonFolder();
        Write(folder, "a.txt");

        var (ok, detail) = NewClassFolder().Rename(LessonId, "a.txt", "../../fugiu.txt");

        Assert.True(ok, detail);
        Assert.True(File.Exists(Path.Combine(folder, "fugiu.txt")));
        Assert.False(File.Exists(Path.Combine(_desktop, "fugiu.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "fugiu.txt")));
    }

    [Fact]
    public void Rename_OfAMissingFile_ToAMissingFolder_Fails()
    {
        var folder = MakeLessonFolder();
        Write(folder, "a.txt");
        var classFolder = NewClassFolder();

        Assert.Equal((false, "arquivo não encontrado"), classFolder.Rename(LessonId, "nada.txt", "b.txt"));
        Assert.Equal((false, "pasta não encontrada"), classFolder.Rename(LessonId, "a.txt", "nova/b.txt"));
        Assert.Equal((false, "aula diferente"), classFolder.Rename(Guid.NewGuid(), "a.txt", "b.txt"));
        Assert.True(File.Exists(Path.Combine(folder, "a.txt")));
    }

    [Fact]
    public void Rename_ThroughAJunction_Fails()
    {
        var folder = MakeLessonFolder();
        Write(folder, "a.txt");
        MakeJunction(Path.Combine(folder, "atalho"), _outside);

        var (ok, _) = NewClassFolder().Rename(LessonId, "a.txt", "atalho/a.txt");

        Assert.False(ok);
        Assert.Empty(Directory.GetFileSystemEntries(_outside));
        Assert.True(File.Exists(Path.Combine(folder, "a.txt")));
    }

    [Fact]
    public void ClearSetAside_RecyclesThatLessonOnly()
    {
        var folder = MakeLessonFolder();
        Write(folder, "agora.txt");
        var gone = Guid.NewGuid();
        var kept = Guid.NewGuid();
        Write(SetAsideFolder(gone), "sub/a.txt");
        Write(SetAsideFolder(kept), "b.txt");

        var (ok, detail) = NewClassFolder().ClearSetAside(gone);

        Assert.True(ok, detail);
        Assert.Equal(new[] { SetAsideFolder(gone) }, _bin.Recycled);
        Assert.False(Directory.Exists(SetAsideFolder(gone)));
        Assert.True(File.Exists(Path.Combine(SetAsideFolder(kept), "b.txt")));
        Assert.True(File.Exists(Path.Combine(folder, "agora.txt")));
    }

    [Fact]
    public void ClearSetAside_WithNothingSetAside_IsOk()
    {
        var (ok, detail) = NewClassFolder().ClearSetAside(Guid.NewGuid());

        Assert.True(ok, detail);
        Assert.Empty(_bin.Recycled);
    }

    [Fact]
    public void ClearSetAside_WhenTheBinRefuses_ReportsWhy()
    {
        var aside = Guid.NewGuid();
        Write(SetAsideFolder(aside), "a.txt");
        _bin.Refuse = new UnauthorizedAccessException();

        var (ok, detail) = NewClassFolder().ClearSetAside(aside);

        Assert.False(ok);
        Assert.Equal("acesso negado", detail);
        Assert.True(Directory.Exists(SetAsideFolder(aside)));
    }

    [Fact]
    public void ClearSetAside_OfALinkedFolder_LeavesItAlone()
    {
        var aside = Guid.NewGuid();
        Write(_outside, "segredo.txt");
        Directory.CreateDirectory(_setAside);
        MakeJunction(SetAsideFolder(aside), _outside);

        var (ok, _) = NewClassFolder().ClearSetAside(aside);

        Assert.False(ok);
        Assert.Empty(_bin.Recycled);
        Assert.True(File.Exists(Path.Combine(_outside, "segredo.txt")));
    }

    private static string Write(string folder, string relativePath, string content = "x")
    {
        var path = Path.Combine(folder, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private void MakeJunction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        process.WaitForExit();
        Assert.True(Directory.Exists(link), "mklink /J did not make the junction");
        _links.Add(link);
    }

    // Takes what it's given out of the way, as the Recycle Bin does, and remembers the paths.
    private sealed class RecordingRecycleBin : IRecycleBin
    {
        public List<string> Recycled { get; } = new();

        public Exception? Refuse { get; set; }

        public void Recycle(string path)
        {
            if (Refuse is not null)
            {
                throw Refuse;
            }

            Recycled.Add(path);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }
        }
    }
}
