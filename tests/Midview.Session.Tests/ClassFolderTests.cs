using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
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
    private readonly List<string> _protected = new();
    private readonly List<string> _closed = new();
    private readonly RecordingRecycleBin _bin;
    private ClassLesson? _lesson = new(1, Guid.NewGuid(), "3º A", 1, false);

    public ClassFolderTests()
    {
        _desktop = Path.Combine(_root, "Desktop");
        _setAside = Path.Combine(_root, "guardada");
        _outside = Path.Combine(_root, "fora");
        _bin = new RecordingRecycleBin(Path.Combine(_root, "lixeira"));
        Directory.CreateDirectory(_desktop);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        foreach (var folder in _protected.Where(Directory.Exists))
        {
            Unprotect(folder);
        }
        if (_closed.Count > 0)
        {
            OpenEverythingBelow(_root); // a folder closed to us can't be deleted, nor can what it holds
        }
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

    // What a push is asked before it is accepted: the Desktop's disk, in FileInbox's words.
    [Fact]
    public void RefusalFor_ASizeThatFits_IsNull()
    {
        Assert.Null(NewClassFolder().RefusalFor(1024));
    }

    [Fact]
    public void RefusalFor_MoreThanTheDiskHolds_SaysThereIsNoRoom()
    {
        Assert.Equal("não há espaço livre suficiente no disco do aluno", NewClassFolder().RefusalFor(long.MaxValue));
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

    // Follow: the Session noticing that the lesson changed.

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
        var display = new RecordingDisplay();
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

    // A new lesson of the same turma uses the very folder the last one had: it is the new lesson's now.
    [Fact]
    public void Follow_NewLessonOfTheSameTurma_LeavesTheFolderAlone()
    {
        var folder = MakeLessonFolder();
        var display = new RecordingDisplay();
        var classFolder = NewClassFolder();
        classFolder.Follow(display);
        Write(folder, "a.png");
        _lesson = new ClassLesson(2, Guid.NewGuid(), "3º A", 1, false);

        classFolder.Follow(display);

        Assert.True(File.Exists(Path.Combine(folder, "a.png")));
        Assert.False(Directory.Exists(_setAside));
        Assert.Equal(2, display.Shown.Count);
        Assert.Equal((LessonId, "3º A"), Remembered());
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
        var display = new RecordingDisplay();
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
        var display = new RecordingDisplay { Throw = new InvalidOperationException("sem tela") };
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
        var display = new RecordingDisplay();

        NewClassFolder().Follow(display);

        Assert.Single(display.Shown);
        Assert.Equal((LessonId, "3º A"), Remembered());
    }

    private string StatePath => Path.Combine(_root, "session.json");

    // The finished lessons the state file still has to settle, oldest first.
    private (Guid LessonId, string Turma)[] Pending()
    {
        if (!File.Exists(StatePath))
        {
            return Array.Empty<(Guid, string)>();
        }

        using var document = JsonDocument.Parse(File.ReadAllText(StatePath));
        if (!document.RootElement.TryGetProperty("Pending", out var pending) || pending.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<(Guid, string)>();
        }

        return pending.EnumerateArray()
            .Select(entry => (entry.GetProperty("LessonId").GetGuid(), entry.GetProperty("Turma").GetString()!))
            .ToArray();
    }

    // A folder the student can't look into, as an ACL can make it: listing it is "access denied".
    private void CloseToTheStudent(string folder)
    {
        var info = new DirectoryInfo(folder);
        var security = info.GetAccessControl();
        security.AddAccessRule(NoListing());
        info.SetAccessControl(security);
        _closed.Add(folder);
    }

    private static void OpenToTheStudent(string folder)
    {
        var info = new DirectoryInfo(folder);
        var security = info.GetAccessControl();
        security.RemoveAccessRuleSpecific(NoListing());
        info.SetAccessControl(security);
    }

    private static FileSystemAccessRule NoListing() =>
        new(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);

    // For the cleanup of a test that failed with a folder still closed: opens each folder before looking inside it.
    private static void OpenEverythingBelow(string folder)
    {
        try
        {
            OpenToTheStudent(folder);
            foreach (var subfolder in Directory.EnumerateDirectories(folder))
            {
                OpenEverythingBelow(subfolder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }
    }

    // What the Session's state file says it follows, or null without a file.
    private (Guid LessonId, string Turma)? Remembered()
    {
        if (!File.Exists(StatePath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(StatePath));
        return (document.RootElement.GetProperty("LessonId").GetGuid(), document.RootElement.GetProperty("Turma").GetString()!);
    }

    // Until the Agent releases it, the student can't delete the lesson's folder: a read-only folder says "access denied" the same way.
    private void Protect(string folder)
    {
        File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
        _protected.Add(folder);
    }

    private static void Unprotect(string folder) =>
        File.SetAttributes(folder, File.GetAttributes(folder) & ~FileAttributes.ReadOnly);

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

    private sealed class RecordingDisplay : IMessageDisplay
    {
        public List<string> Shown { get; } = new();

        public Exception? Throw { get; set; }

        public void Show(string text)
        {
            Shown.Add(text);
            if (Throw is not null)
            {
                throw Throw;
            }
        }
    }

    // Takes what it's given out of the way, as the Recycle Bin does, and remembers the paths.
    private sealed class RecordingRecycleBin : IRecycleBin
    {
        private readonly string _binFolder;

        public RecordingRecycleBin(string binFolder) => _binFolder = binFolder;

        public List<string> Recycled { get; } = new();

        // What was inside each folder when it went.
        public Dictionary<string, string[]> Contents { get; } = new();

        public Exception? Refuse { get; set; }

        public void Recycle(string path)
        {
            if (Refuse is not null)
            {
                throw Refuse;
            }

            if (Directory.Exists(path))
            {
                // The Agent's folder, until it lets go of it, can't be deleted by the student: the Bin can't have it either.
                if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
                {
                    throw new UnauthorizedAccessException($"{path} is protected");
                }

                // What went with the folder. A folder that can't be looked into goes with it unseen, as with the shell's one move.
                try
                {
                    Contents[path] = Directory.GetFileSystemEntries(path, "*", SearchOption.AllDirectories)
                        .Select(entry => Path.GetRelativePath(path, entry).Replace('\\', '/'))
                        .OrderBy(entry => entry, StringComparer.Ordinal)
                        .ToArray();
                }
                catch (UnauthorizedAccessException)
                {
                }

                Directory.CreateDirectory(_binFolder);
                Directory.Move(path, Path.Combine(_binFolder, Guid.NewGuid().ToString("N")));
            }
            else
            {
                File.Delete(path);
            }

            Recycled.Add(path);
        }
    }
}
