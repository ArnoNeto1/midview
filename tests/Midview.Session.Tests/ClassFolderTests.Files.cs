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
}
